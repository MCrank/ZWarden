using System.Globalization;
using ZWarden.PzConfig.Validation;

namespace ZWarden.PzConfig.Tests;

/// <summary>
/// #227: harvesting, comment ranges and the hand-curated schema, run against full-key B42-shaped files rather than
/// three-line snippets. PZ writes a <c>Min: … Max: … Default: …</c> phrase into most numeric settings' comments in
/// both files; the sanitizer lifts it out as the setting's range (and drops it from the prose so the tooltip does not
/// repeat it), and the schema's own ranges must agree with the game's wherever both exist.
/// </summary>
public class PzB42FixtureTests
{
    [Test]
    public async Task Sanitizer_lifts_the_min_max_default_phrase_out_of_the_prose()
    {
        PzSettingHelp help = PzCommentSanitizer.Sanitize("The time it takes to enter PVP mode Min: 0 Max: 1000 Default: 2");

        await Assert.That(help.Text).IsEqualTo("The time it takes to enter PVP mode");
        await Assert.That(help.Min).IsEqualTo(0d);
        await Assert.That(help.Max).IsEqualTo(1000d);
        await Assert.That(help.Default).IsEqualTo("2");
    }

    [Test]
    public async Task Sanitizer_reads_fractional_and_negative_ranges()
    {
        PzSettingHelp help = PzCommentSanitizer.Sanitize("Water shutoff. Min: -1 Max: 2147483647 Default: 14");
        PzSettingHelp rate = PzCommentSanitizer.Sanitize("Rate. Min: 0.00 Max: 1000.00 Default: 1.00");

        await Assert.That(help.Min).IsEqualTo(-1d);
        await Assert.That(help.Max).IsEqualTo(2147483647d);
        await Assert.That(rate.Max).IsEqualTo(1000d);
        await Assert.That(rate.Default).IsEqualTo("1.00");
    }

    [Test]
    public async Task A_comment_without_a_range_has_none()
    {
        PzSettingHelp help = PzCommentSanitizer.Sanitize("Players can hurt and kill other players");

        await Assert.That(help.Min).IsNull();
        await Assert.That(help.Max).IsNull();
        await Assert.That(help.Default).IsNull();
    }

    [Test]
    public async Task Every_commented_ini_key_in_a_full_b42_file_gets_its_comment()
    {
        PzConfigReadResult read = B42Fixtures.ReadIni();

        await Assert.That(read.Comments.TryGetValue("PVP", out string? pvp)).IsTrue();
        await Assert.That(pvp!).Contains("PVP");
        await Assert.That(read.Comments.TryGetValue("SafetyToggleTimer", out string? timer)).IsTrue();
        await Assert.That(PzCommentSanitizer.Sanitize(timer!).Max).IsEqualTo(1000d);

        // An uncommented key (PZ leaves a few bare) simply has none.
        await Assert.That(read.Comments.ContainsKey("ChatStreams")).IsFalse();
    }

    [Test]
    public async Task Nested_sandbox_comments_are_harvested_by_dotted_path()
    {
        PzConfigReadResult read = B42Fixtures.ReadSandboxVars();

        await Assert.That(read.Comments.TryGetValue("MultiplierConfig.Glassmaking", out string? glass)).IsTrue();
        await Assert.That(PzCommentSanitizer.Sanitize(glass!).Max).IsEqualTo(1000d);
        await Assert.That(read.Comments.ContainsKey($"{B42Fixtures.ModTable}.Difficulty")).IsTrue();
    }

    [Test]
    public async Task Schema_ranges_agree_with_the_games_own_ranges()
    {
        // The schema is hand-maintained; the file states the game's range. A schema narrower than the game would
        // reject a legitimate value on apply (MaxPlayers once capped at 100 while PZ allows 254).
        List<string> disagreements = [];
        Check(PzSchema.Ini, B42Fixtures.ReadIni(), disagreements);
        Check(PzSchema.SandboxVars, B42Fixtures.ReadSandboxVars(), disagreements);

        await Assert.That(disagreements).IsEmpty();
    }

    private static void Check(PzSchema schema, PzConfigReadResult read, List<string> disagreements)
    {
        foreach (PzSchemaEntry entry in schema.Entries.Where(e => e.Min is not null || e.Max is not null))
        {
            if (!read.Comments.TryGetValue(entry.Path, out string? comment))
            {
                continue;
            }

            PzSettingHelp help = PzCommentSanitizer.Sanitize(comment);
            double? gameMin = help.Min ?? (help.Options.Count > 0 ? help.Options.Min(o => double.Parse(o.Value, CultureInfo.InvariantCulture)) : null);
            double? gameMax = help.Max ?? (help.Options.Count > 0 ? help.Options.Max(o => double.Parse(o.Value, CultureInfo.InvariantCulture)) : null);
            if (gameMin is null && gameMax is null)
            {
                continue;
            }

            if (entry.Min != gameMin || entry.Max != gameMax)
            {
                disagreements.Add($"{entry.Path}: schema {entry.Min}..{entry.Max}, game {gameMin}..{gameMax}");
            }
        }
    }
}
