using System.Text;
using ZWarden.PzConfig;
using ZWarden.PzConfig.Internal;
using ZWarden.PzConfig.Model;

namespace ZWarden.PzConfig.Tests;

/// <summary>
/// The write half for the Lua files (F20b, ADR 0010): a surgical, single-value replacement that keeps
/// the Loretta parse tree, splices the new value over exactly the old value's span in the source, and
/// re-emits BOM-less UTF-8 — byte-identical everywhere except the one value, comments and all. This is
/// the correctness heart of the ADR: a value edit must not disturb a mod-added key or a machine-emitted
/// comment.
/// </summary>
public class LuaConfigEditTests
{
    private static IPzConfigDocument Open(PzConfigKind kind, string content) =>
        LuaConfigReader.Read(kind, Encoding.UTF8.GetBytes(content)).Document!;

    [Test]
    public async Task Emit_of_an_unedited_document_is_byte_identical()
    {
        const string src = """
            SandboxVars = {
                VERSION = 6,
                Zombies = 4, -- population multiplier
                XpMultiplier = 1.0,
            }
            """;

        IPzConfigDocument doc = Open(PzConfigKind.SandboxVars, src);

        await Assert.That(Encoding.UTF8.GetString(doc.Emit())).IsEqualTo(src);
    }

    [Test]
    public async Task Setting_a_scalar_changes_only_that_value_and_keeps_every_comment()
    {
        const string src = """
            SandboxVars = {
                VERSION = 6,
                Zombies = 4, -- population multiplier
                XpMultiplier = 1.0,
            }
            """;

        IPzConfigDocument doc = Open(PzConfigKind.SandboxVars, src);
        PzConfigEditResult result = doc.TrySetValue("Zombies", new PzNumber(1, "1", isInteger: true));

        await Assert.That(result.Ok).IsTrue();

        const string expected = """
            SandboxVars = {
                VERSION = 6,
                Zombies = 1, -- population multiplier
                XpMultiplier = 1.0,
            }
            """;
        await Assert.That(Encoding.UTF8.GetString(doc.Emit())).IsEqualTo(expected);
    }

    [Test]
    public async Task Setting_a_nested_value_touches_only_that_token()
    {
        const string src = """
            SandboxVars = {
                Map = {
                    AllowMiniMap = false,
                    AllowWorldMap = true,
                },
            }
            """;

        IPzConfigDocument doc = Open(PzConfigKind.SandboxVars, src);
        await Assert.That(doc.TrySetValue("Map.AllowMiniMap", new PzBoolean(true)).Ok).IsTrue();

        const string expected = """
            SandboxVars = {
                Map = {
                    AllowMiniMap = true,
                    AllowWorldMap = true,
                },
            }
            """;
        await Assert.That(Encoding.UTF8.GetString(doc.Emit())).IsEqualTo(expected);
    }

    [Test]
    public async Task An_edited_value_reads_back_through_the_model()
    {
        IPzConfigDocument doc = Open(PzConfigKind.SandboxVars, "SandboxVars = {\n    Zombies = 4,\n}");

        doc.TrySetValue("Zombies", new PzNumber(1, "1", isInteger: true));

        await Assert.That(doc.TryGetValue("Zombies", out PzValue v)).IsTrue();
        await Assert.That(((PzNumber)v).Value).IsEqualTo(1d);
    }

    [Test]
    public async Task A_string_value_is_rewritten_as_a_quoted_lua_literal()
    {
        const string src = "SandboxVars = {\n    Name = \"servertest\",\n}";

        IPzConfigDocument doc = Open(PzConfigKind.SandboxVars, src);
        await Assert.That(doc.TrySetValue("Name", new PzString("My Server")).Ok).IsTrue();

        await Assert.That(Encoding.UTF8.GetString(doc.Emit())).IsEqualTo("SandboxVars = {\n    Name = \"My Server\",\n}");
        await Assert.That(doc.TryGetValue("Name", out PzValue v)).IsTrue();
        await Assert.That(((PzString)v).Value).IsEqualTo("My Server");
    }

    [Test]
    public async Task A_string_value_with_quotes_is_escaped()
    {
        IPzConfigDocument doc = Open(PzConfigKind.SandboxVars, "SandboxVars = {\n    Name = \"a\",\n}");

        await Assert.That(doc.TrySetValue("Name", new PzString("a \"b\" c")).Ok).IsTrue();

        await Assert.That(Encoding.UTF8.GetString(doc.Emit())).IsEqualTo("SandboxVars = {\n    Name = \"a \\\"b\\\" c\",\n}");
    }

    [Test]
    public async Task Replacing_a_number_with_a_different_shaped_number_preserves_the_new_lexeme()
    {
        IPzConfigDocument doc = Open(PzConfigKind.SandboxVars, "SandboxVars = {\n    XpMultiplier = 1.0,\n}");

        await Assert.That(doc.TrySetValue("XpMultiplier", new PzNumber(-2, "-2", isInteger: true)).Ok).IsTrue();

        await Assert.That(Encoding.UTF8.GetString(doc.Emit())).IsEqualTo("SandboxVars = {\n    XpMultiplier = -2,\n}");
    }

    [Test]
    public async Task Two_edits_compose()
    {
        const string src = "SandboxVars = {\n    Zombies = 4,\n    Speed = 2,\n}";

        IPzConfigDocument doc = Open(PzConfigKind.SandboxVars, src);
        doc.TrySetValue("Zombies", new PzNumber(1, "1", isInteger: true));
        doc.TrySetValue("Speed", new PzNumber(3, "3", isInteger: true));

        await Assert.That(Encoding.UTF8.GetString(doc.Emit())).IsEqualTo("SandboxVars = {\n    Zombies = 1,\n    Speed = 3,\n}");
    }

    [Test]
    public async Task Emit_never_writes_a_bom_even_when_the_input_had_one()
    {
        byte[] withBom = [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("SandboxVars = {\n    Zombies = 4,\n}")];

        IPzConfigDocument doc = LuaConfigReader.Read(PzConfigKind.SandboxVars, withBom).Document!;
        doc.TrySetValue("Zombies", new PzNumber(1, "1", isInteger: true));
        byte[] emitted = doc.Emit();

        await Assert.That(emitted.Length >= 3 && emitted[0] == 0xEF && emitted[1] == 0xBB && emitted[2] == 0xBF).IsFalse();
    }

    [Test]
    public async Task A_lua_document_opened_from_bytes_is_editable()
    {
        IPzConfigDocument doc = Open(PzConfigKind.SandboxVars, "SandboxVars = {\n    Zombies = 4,\n}");
        await Assert.That(doc.IsEditable).IsTrue();
    }

    [Test]
    public async Task A_spawnregions_file_round_trips_byte_exact()
    {
        const string src = "function SpawnRegions()\n\treturn {\n\t\t{ name = \"Muldraugh, KY\", file = \"media/maps/Muldraugh, KY/spawnpoints.lua\" },\n\t}\nend\n";

        IPzConfigDocument doc = Open(PzConfigKind.SpawnRegions, src);

        await Assert.That(doc.IsEditable).IsTrue();
        await Assert.That(Encoding.UTF8.GetString(doc.Emit())).IsEqualTo(src);
    }

    [Test]
    public async Task A_spawnpoints_file_round_trips_byte_exact()
    {
        const string src = "function SpawnPoints()\n\treturn {\n\t\t[\"park ranger\"] = {\n\t\t\t{ worldX = 40, worldY = 22 }\n\t\t},\n\t}\nend\n";

        IPzConfigDocument doc = Open(PzConfigKind.SpawnPoints, src);

        await Assert.That(Encoding.UTF8.GetString(doc.Emit())).IsEqualTo(src);
    }

    [Test]
    public async Task Setting_a_missing_key_is_path_not_found()
    {
        IPzConfigDocument doc = Open(PzConfigKind.SandboxVars, "SandboxVars = {\n    Zombies = 4,\n}");

        PzConfigEditResult result = doc.TrySetValue("NoSuchKey", new PzNumber(1, "1", isInteger: true));

        await Assert.That(result.Status).IsEqualTo(PzEditStatus.PathNotFound);
    }

    [Test]
    public async Task Setting_a_table_valued_path_is_path_is_table()
    {
        IPzConfigDocument doc = Open(PzConfigKind.SandboxVars, "SandboxVars = {\n    Map = {\n        AllowMiniMap = false,\n    },\n}");

        PzConfigEditResult result = doc.TrySetValue("Map", new PzBoolean(true));

        await Assert.That(result.Status).IsEqualTo(PzEditStatus.PathIsTable);
    }

    [Test]
    public async Task Descending_through_a_scalar_is_path_not_found()
    {
        IPzConfigDocument doc = Open(PzConfigKind.SandboxVars, "SandboxVars = {\n    Zombies = 4,\n}");

        // Zombies is a scalar, so "Zombies.Something" has nothing to descend into.
        PzConfigEditResult result = doc.TrySetValue("Zombies.Something", new PzNumber(1, "1", isInteger: true));

        await Assert.That(result.Status).IsEqualTo(PzEditStatus.PathNotFound);
    }

    [Test]
    public async Task A_quoted_profession_key_that_holds_a_table_is_path_is_table()
    {
        const string src = "function SpawnPoints()\n\treturn {\n\t\t[\"park ranger\"] = {\n\t\t\t{ worldX = 40 }\n\t\t},\n\t}\nend\n";

        IPzConfigDocument doc = Open(PzConfigKind.SpawnPoints, src);

        PzConfigEditResult result = doc.TrySetValue("park ranger", new PzBoolean(true));

        await Assert.That(result.Status).IsEqualTo(PzEditStatus.PathIsTable);
    }

    [Test]
    public async Task An_empty_path_segment_is_path_not_found()
    {
        IPzConfigDocument doc = Open(PzConfigKind.SandboxVars, "SandboxVars = {\n    Map = {\n        AllowMiniMap = false,\n    },\n}");

        await Assert.That(doc.TrySetValue("Map..AllowMiniMap", new PzBoolean(true)).Status).IsEqualTo(PzEditStatus.PathNotFound);
    }
}
