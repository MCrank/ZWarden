using ZWarden.Diagnostics.SupportPackage;

namespace ZWarden.Diagnostics.Tests.SupportPackage;

/// <summary>F30 PR-A: the "Sanitize" stage strips ANSI/CSI/OSC escapes and C0/C1 control bytes, keeps tab, and caps
/// length — the F27 <c>LogLineSanitizer</c> posture at the packaging boundary.</summary>
public class SupportPackageSanitizerTests
{
    [Test]
    public async Task Plain_text_is_unchanged()
    {
        await Assert.That(SupportPackageSanitizer.Sanitize("Base.Vanilla loaded")).IsEqualTo("Base.Vanilla loaded");
    }

    [Test]
    public async Task Null_becomes_empty()
    {
        await Assert.That(SupportPackageSanitizer.Sanitize(null)).IsEqualTo("");
    }

    [Test]
    public async Task A_csi_colour_escape_is_stripped()
    {
        string coloured = "[31mred[0m";

        await Assert.That(SupportPackageSanitizer.Sanitize(coloured)).IsEqualTo("red");
    }

    [Test]
    public async Task An_osc_sequence_is_stripped()
    {
        string osc = "]0;titletext";

        await Assert.That(SupportPackageSanitizer.Sanitize(osc)).IsEqualTo("text");
    }

    [Test]
    public async Task Control_bytes_are_stripped_but_tab_is_kept()
    {
        string mixed = "a\tb" + (char)0x00 + (char)0x07 + "\nc";

        await Assert.That(SupportPackageSanitizer.Sanitize(mixed)).IsEqualTo("a\tbc");
    }

    [Test]
    public async Task A_c1_control_byte_is_stripped()
    {
        string withC1 = "a" + (char)0x85 + "b"; // U+0085 (NEL), a C1 control.

        await Assert.That(SupportPackageSanitizer.Sanitize(withC1)).IsEqualTo("ab");
    }

    [Test]
    public async Task Output_is_capped_to_max_length()
    {
        string huge = new('x', SupportPackageSanitizer.MaxLength + 500);

        await Assert.That(SupportPackageSanitizer.Sanitize(huge).Length).IsEqualTo(SupportPackageSanitizer.MaxLength);
    }
}
