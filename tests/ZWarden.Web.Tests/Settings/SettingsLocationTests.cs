using ZWarden.Web.Components.Pages.Settings;

namespace ZWarden.Web.Tests.Settings;

/// <summary>
/// #344: Settings keeps its own URL (an in-place section switch is a <c>history.pushState</c> the circuit's
/// <c>NavigationManager</c> never sees), so it reads the section from the path itself: <c>/settings/{section}</c>.
/// </summary>
public sealed class SettingsLocationTests
{
    [Test]
    public async Task The_bare_settings_path_is_the_settings_page_with_no_section()
    {
        SettingsLocation location = SettingsLocation.Parse("http://localhost/settings");

        await Assert.That(location.IsSettingsPage).IsTrue();
        await Assert.That(location.Section).IsNull();
    }

    [Test]
    public async Task The_segment_after_settings_is_the_section()
    {
        SettingsLocation location = SettingsLocation.Parse("http://localhost/settings/security?x=1");

        await Assert.That(location.IsSettingsPage).IsTrue();
        await Assert.That(location.Section).IsEqualTo("security");
    }

    [Test]
    public async Task Case_and_a_trailing_slash_do_not_matter()
    {
        SettingsLocation location = SettingsLocation.Parse("http://localhost/Settings/About/");

        await Assert.That(location.IsSettingsPage).IsTrue();
        await Assert.That(location.Section).IsEqualTo("about");
    }

    [Test]
    public async Task Another_page_is_not_settings()
    {
        await Assert.That(SettingsLocation.Parse("http://localhost/servers/srv-1").IsSettingsPage).IsFalse();
        await Assert.That(SettingsLocation.Parse("http://localhost/settingsx").IsSettingsPage).IsFalse();
        await Assert.That(SettingsLocation.Parse("http://localhost/settings/a/b").IsSettingsPage).IsFalse();
        await Assert.That(SettingsLocation.Parse("not a url").IsSettingsPage).IsFalse();
    }
}
