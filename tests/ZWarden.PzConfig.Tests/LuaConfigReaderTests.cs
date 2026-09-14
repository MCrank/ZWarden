using System.Text;
using ZWarden.PzConfig;
using ZWarden.PzConfig.Internal;
using ZWarden.PzConfig.Model;

namespace ZWarden.PzConfig.Tests;

/// <summary>
/// The Loretta-backed reader for the three Lua files, parse-only in Lua51 mode (ADR 0010). It reads
/// the measured shapes (research §2): the <c>SandboxVars = { }</c> global assignment, and the
/// <c>function SpawnRegions()/SpawnPoints() return { } end</c> function wrappers, with bare and
/// quoted keys and positional sequences. A syntax error is a first-class not-parsed result with a
/// line and column, not an exception.
/// </summary>
public class LuaConfigReaderTests
{
    private static PzConfigReadResult Read(PzConfigKind kind, string content) =>
        LuaConfigReader.Read(kind, Encoding.UTF8.GetBytes(content));

    [Test]
    public async Task Reads_sandbox_scalars_of_each_type()
    {
        const string src = """
            SandboxVars = {
                VERSION = 6,
                Zombies = 4,
                XpMultiplier = 1.0,
                ZombieVoronoiNoise = true,
                Name = "servertest",
            }
            """;

        PzConfigReadResult result = Read(PzConfigKind.SandboxVars, src);

        await Assert.That(result.Parsed).IsTrue();
        IPzConfigDocument doc = result.Document!;

        await Assert.That(doc.TryGetValue("VERSION", out PzValue version)).IsTrue();
        await Assert.That(((PzNumber)version).Value).IsEqualTo(6d);
        await Assert.That(((PzNumber)version).IsInteger).IsTrue();

        await Assert.That(doc.TryGetValue("XpMultiplier", out PzValue xp)).IsTrue();
        await Assert.That(((PzNumber)xp).IsInteger).IsFalse();
        await Assert.That(((PzNumber)xp).Lexeme).IsEqualTo("1.0");

        await Assert.That(doc.TryGetValue("ZombieVoronoiNoise", out PzValue flag)).IsTrue();
        await Assert.That(((PzBoolean)flag).Value).IsTrue();

        await Assert.That(doc.TryGetValue("Name", out PzValue name)).IsTrue();
        await Assert.That(((PzString)name).Value).IsEqualTo("servertest");
    }

    [Test]
    public async Task Reads_a_nested_sandbox_table()
    {
        const string src = """
            SandboxVars = {
                Map = {
                    AllowMiniMap = false,
                    AllowWorldMap = true,
                },
            }
            """;

        PzConfigReadResult result = Read(PzConfigKind.SandboxVars, src);

        await Assert.That(result.Parsed).IsTrue();
        await Assert.That(result.Document!.TryGetValue("Map.AllowWorldMap", out PzValue v)).IsTrue();
        await Assert.That(((PzBoolean)v).Value).IsTrue();
    }

    [Test]
    public async Task Preserves_a_key_with_no_schema_a_mod_added()
    {
        const string src = """
            SandboxVars = {
                Zombies = 4,
                SomeModAddedKey = 12,
            }
            """;

        PzConfigReadResult result = Read(PzConfigKind.SandboxVars, src);

        // The unknown key is preserved in the model - never silently dropped (ADR 0010).
        await Assert.That(result.Document!.TryGetValue("SomeModAddedKey", out PzValue v)).IsTrue();
        await Assert.That(((PzNumber)v).Value).IsEqualTo(12d);
    }

    [Test]
    public async Task Reads_negative_numbers()
    {
        const string src = """
            SandboxVars = {
                Offset = -2,
            }
            """;

        PzConfigReadResult result = Read(PzConfigKind.SandboxVars, src);

        await Assert.That(result.Document!.TryGetValue("Offset", out PzValue v)).IsTrue();
        await Assert.That(((PzNumber)v).Value).IsEqualTo(-2d);
        await Assert.That(((PzNumber)v).Lexeme).IsEqualTo("-2");
    }

    [Test]
    public async Task Reads_spawnregions_function_wrapper_with_positional_entries()
    {
        const string src = "function SpawnRegions()\n\treturn {\n\t\t{ name = \"Muldraugh, KY\", file = \"media/maps/Muldraugh, KY/spawnpoints.lua\" },\n\t\t{ name = \"West Point, KY\", file = \"media/maps/West Point, KY/spawnpoints.lua\" },\n\t}\nend\n";

        PzConfigReadResult result = Read(PzConfigKind.SpawnRegions, src);

        await Assert.That(result.Parsed).IsTrue();
        PzTable root = result.Document!.Root;
        await Assert.That(root.PositionalEntries.Count()).IsEqualTo(2);

        var first = (PzTable)root.PositionalEntries.First().Value;
        await Assert.That(first.TryGet("name", out PzValue name)).IsTrue();
        await Assert.That(((PzString)name).Value).IsEqualTo("Muldraugh, KY");
    }

    [Test]
    public async Task Reads_spawnpoints_with_a_quoted_profession_key()
    {
        const string src = "function SpawnPoints()\n\treturn {\n\t\t[\"park ranger\"] = {\n\t\t\t{ worldX = 40, worldY = 22, posX = 67, posY = 201 }\n\t\t},\n\t\tunemployed = {\n\t\t\t{ posX = 100, posY = 200, posZ = 0 }\n\t\t}\n\t}\nend\n";

        PzConfigReadResult result = Read(PzConfigKind.SpawnPoints, src);

        await Assert.That(result.Parsed).IsTrue();
        PzTable root = result.Document!.Root;

        PzTableEntry ranger = root.NamedEntries.First(e => e.Key!.Name == "park ranger");
        await Assert.That(ranger.Key!.WasQuoted).IsTrue();
        await Assert.That(((PzTable)ranger.Value).PositionalEntries.Count()).IsEqualTo(1);

        await Assert.That(root.TryGet("unemployed", out _)).IsTrue();
    }

    [Test]
    public async Task A_syntax_error_is_a_not_parsed_result_with_a_position()
    {
        // Missing closing brace.
        const string src = "SandboxVars = {\n    Zombies = 4,\n";

        PzConfigReadResult result = Read(PzConfigKind.SandboxVars, src);

        await Assert.That(result.Parsed).IsFalse();
        await Assert.That(result.Document).IsNull();
        PzConfigDiagnostic diag = result.Diagnostics.First(d => d.Severity == PzDiagnosticSeverity.Error);
        await Assert.That(diag.Code).IsEqualTo(PzConfigDiagnostic.Codes.ParseError);
        await Assert.That(diag.Position).IsNotNull();
    }

    [Test]
    public async Task The_wrong_root_shape_is_reported()
    {
        // A SandboxVars file that is actually a spawn function - the root is not SandboxVars = { }.
        const string src = "function SpawnRegions() return { } end\n";

        PzConfigReadResult result = Read(PzConfigKind.SandboxVars, src);

        await Assert.That(result.Parsed).IsFalse();
        await Assert.That(result.Diagnostics.Any(d => d.Code == PzConfigDiagnostic.Codes.UnexpectedRoot)).IsTrue();
    }
}
