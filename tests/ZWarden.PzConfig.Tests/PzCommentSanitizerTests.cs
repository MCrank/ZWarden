using ZWarden.PzConfig;

namespace ZWarden.PzConfig.Tests;

/// <summary>
/// The sanitizer turns a harvested raw comment (research §2.1 — PZ's own tooltip text) into display
/// help: prose with the UI rich-text markup stripped, and the enum-label lines (<c>1 = Insane</c>)
/// pulled out as selectable options. This is what lets a config editor show a labelled dropdown and a
/// clean tooltip instead of the raw <c>--</c> block (F20c, PR-A slice 2).
/// </summary>
public class PzCommentSanitizerTests
{
    [Test]
    public async Task Strips_pz_ui_markup_and_collapses_whitespace()
    {
        // The real RollsMultiplier tooltip (research §2.1), markup and all.
        const string raw = "<BHC> [!] It is recommended that you DO NOT change this. [!] <RGB:1,1,1>   Can be used to adjust the number of rolls made on loot tables when spawning loot.";

        PzSettingHelp help = PzCommentSanitizer.Sanitize(raw);

        await Assert.That(help.Text).IsEqualTo(
            "It is recommended that you DO NOT change this. Can be used to adjust the number of rolls made on loot tables when spawning loot.");
        await Assert.That(help.Options.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Pulls_enum_lines_out_as_options_and_keeps_the_rest_as_text()
    {
        const string raw = "Changing this also sets the \"Population Multiplier\" in Advanced Zombie Options. Default = Normal\n"
            + "1 = Insane\n4 = Normal\n6 = None";

        PzSettingHelp help = PzCommentSanitizer.Sanitize(raw);

        await Assert.That(help.Text).IsEqualTo(
            "Changing this also sets the \"Population Multiplier\" in Advanced Zombie Options. Default = Normal");
        await Assert.That(help.Options.Count).IsEqualTo(3);
        await Assert.That(help.Options[0].Value).IsEqualTo("1");
        await Assert.That(help.Options[0].Label).IsEqualTo("Insane");
        await Assert.That(help.Options[2].Value).IsEqualTo("6");
        await Assert.That(help.Options[2].Label).IsEqualTo("None");
    }

    [Test]
    public async Task A_default_equals_line_is_prose_not_an_enum_option()
    {
        // "Default = Normal" is not an "N = Label" enum line — it must stay in the text.
        const string raw = "How zombies are distributed across the map. Default = Urban Focused";

        PzSettingHelp help = PzCommentSanitizer.Sanitize(raw);

        await Assert.That(help.Options.Count).IsEqualTo(0);
        await Assert.That(help.Text).IsEqualTo("How zombies are distributed across the map. Default = Urban Focused");
    }

    [Test]
    public async Task Multi_word_enum_labels_are_preserved()
    {
        const string raw = "1 = Urban Focused\n2 = Uniform";

        PzSettingHelp help = PzCommentSanitizer.Sanitize(raw);

        await Assert.That(help.Text).IsEqualTo(string.Empty);
        await Assert.That(help.Options[0].Label).IsEqualTo("Urban Focused");
    }

    [Test]
    public async Task Whitespace_or_markup_only_input_yields_empty_help()
    {
        PzSettingHelp help = PzCommentSanitizer.Sanitize("<RGB:1,1,1>   ");

        await Assert.That(help.Text).IsEqualTo(string.Empty);
        await Assert.That(help.Options.Count).IsEqualTo(0);
    }
}
