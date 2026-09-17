using ZWarden.Web.Components.Layout;

namespace ZWarden.Web.Tests.Layout;

/// <summary>
/// The app shell's route→section-label helper (ADR 0040). Static SSR computes the top-bar breadcrumb
/// from the request path, so the mapping is a pure function tested directly.
/// </summary>
public sealed class ShellNavigationTests
{
    [Test]
    [Arguments("/", "Fleet")]
    [Arguments("/servers", "Fleet")]
    [Arguments("/servers/", "Fleet")]
    [Arguments("/servers/01ab23cd", "Fleet")]
    [Arguments("/hosts", "Hosts")]
    [Arguments("/settings", "Settings")]
    [Arguments("/audit", "Audit")]
    [Arguments("/account/manage", "Account")]
    [Arguments("/setup/tls", "Setup")]
    public async Task SectionLabelFor_maps_top_level_sections(string path, string expected)
    {
        await Assert.That(ShellNavigation.SectionLabelFor(path)).IsEqualTo(expected);
    }

    [Test]
    [Arguments("/nope")]
    [Arguments("")]
    public async Task SectionLabelFor_falls_back_for_unmapped_or_empty_paths(string path)
    {
        // Root ("") resolves to Fleet (the dashboard); a genuinely unknown path falls back to ZWarden.
        string expected = path.Length == 0 ? "Fleet" : "ZWarden";
        await Assert.That(ShellNavigation.SectionLabelFor(path)).IsEqualTo(expected);
    }

    [Test]
    public async Task SectionLabelFor_is_case_insensitive()
    {
        await Assert.That(ShellNavigation.SectionLabelFor("/AUDIT")).IsEqualTo("Audit");
    }
}
