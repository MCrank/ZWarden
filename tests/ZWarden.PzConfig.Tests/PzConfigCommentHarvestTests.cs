using System.Text;
using ZWarden.PzConfig;
using ZWarden.PzConfig.Internal;

namespace ZWarden.PzConfig.Tests;

/// <summary>
/// The read path harvests each setting's leading <c>--</c> / <c># </c> comment and returns it as a
/// side-map keyed by dotted path (F20c, PR-A). The comment block above a Project Zomboid sandbox
/// setting <em>is</em> its in-game tooltip (research §2.1) — including the enum-label lines — so this
/// is the raw material a config editor turns into per-setting help. Comments are kept off the value
/// model, the snapshot, the diff and the drift check by design (ADR 0011: a regenerated comment is not
/// drift); they live only in this side-map. Slice 1 harvests the raw text (markers stripped, lines
/// joined); the sanitizer (slice 2) turns it into display help.
/// </summary>
public class PzConfigCommentHarvestTests
{
    private static readonly PzConfigParser Parser = new();

    private static PzConfigReadResult ReadLua(PzConfigKind kind, string content) =>
        LuaConfigReader.Read(kind, Encoding.UTF8.GetBytes(content));

    [Test]
    public async Task Harvests_a_top_level_sandbox_comment_with_its_enum_lines()
    {
        const string src = """
            SandboxVars = {
                VERSION = 6,
                -- Changing this also sets the "Population Multiplier" in Advanced Zombie Options. Default = Normal
                -- 1 = Insane
                -- 4 = Normal
                -- 6 = None
                Zombies = 4,
            }
            """;

        PzConfigReadResult result = ReadLua(PzConfigKind.SandboxVars, src);

        await Assert.That(result.Comments.TryGetValue("Zombies", out string? comment)).IsTrue();
        // Markers stripped; the descriptive line and each enum line survive, joined by newlines.
        await Assert.That(comment!).IsEqualTo(
            "Changing this also sets the \"Population Multiplier\" in Advanced Zombie Options. Default = Normal\n"
            + "1 = Insane\n4 = Normal\n6 = None");
    }

    [Test]
    public async Task Harvests_a_comment_on_the_first_field_of_a_nested_table()
    {
        const string src = """
            SandboxVars = {
                Map = {
                    -- If enabled, a mini-map window will be available.
                    AllowMiniMap = false,
                    -- If enabled, the world map can be accessed.
                    AllowWorldMap = true,
                },
            }
            """;

        PzConfigReadResult result = ReadLua(PzConfigKind.SandboxVars, src);

        await Assert.That(result.Comments["Map.AllowMiniMap"]).IsEqualTo("If enabled, a mini-map window will be available.");
        await Assert.That(result.Comments["Map.AllowWorldMap"]).IsEqualTo("If enabled, the world map can be accessed.");
    }

    [Test]
    public async Task A_setting_with_no_comment_has_no_entry()
    {
        const string src = "SandboxVars = {\n    VERSION = 6,\n    Zombies = 4,\n}\n";

        PzConfigReadResult result = ReadLua(PzConfigKind.SandboxVars, src);

        await Assert.That(result.Comments.ContainsKey("Zombies")).IsFalse();
        await Assert.That(result.Comments.ContainsKey("VERSION")).IsFalse();
    }

    [Test]
    public async Task Comments_is_an_empty_map_never_null_when_the_file_has_none()
    {
        const string src = "SandboxVars = {\n    Zombies = 4,\n}\n";

        PzConfigReadResult result = ReadLua(PzConfigKind.SandboxVars, src);

        await Assert.That(result.Comments).IsNotNull();
        await Assert.That(result.Comments.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Harvests_a_multi_line_block_comment()
    {
        const string src = """
            SandboxVars = {
                --[[ A block comment
                    over two lines ]]
                Zombies = 4,
            }
            """;

        PzConfigReadResult result = ReadLua(PzConfigKind.SandboxVars, src);

        await Assert.That(result.Comments.TryGetValue("Zombies", out string? c)).IsTrue();
        await Assert.That(c!.Contains("A block comment", StringComparison.Ordinal)).IsTrue();
        await Assert.That(c!.Contains("--", StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    public async Task Harvests_an_ini_comment_above_a_key()
    {
        const string src = "# Allow player vs player combat.\nPVP=true\nMaxPlayers=32\n";

        PzConfigReadResult result = Parser.Open(PzConfigKind.Ini, Encoding.UTF8.GetBytes(src));

        await Assert.That(result.Comments["PVP"]).IsEqualTo("Allow player vs player combat.");
        await Assert.That(result.Comments.ContainsKey("MaxPlayers")).IsFalse();
    }

    [Test]
    public async Task A_blank_line_detaches_an_ini_comment_from_a_later_key()
    {
        // The comment describes nothing directly below it; a blank line separates it from PVP.
        const string src = "# A file header, not a setting description.\n\nPVP=true\n";

        PzConfigReadResult result = Parser.Open(PzConfigKind.Ini, Encoding.UTF8.GetBytes(src));

        await Assert.That(result.Comments.ContainsKey("PVP")).IsFalse();
    }

    [Test]
    public async Task Joins_consecutive_ini_comment_lines()
    {
        const string src = "# First line.\n# Second line.\nPublic=false\n";

        PzConfigReadResult result = Parser.Open(PzConfigKind.Ini, Encoding.UTF8.GetBytes(src));

        await Assert.That(result.Comments["Public"]).IsEqualTo("First line.\nSecond line.");
    }
}
