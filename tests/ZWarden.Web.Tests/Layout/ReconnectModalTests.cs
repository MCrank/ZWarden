using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using ZWarden.Web.Tests.Account;

namespace ZWarden.Web.Tests.Layout;

/// <summary>
/// #299: the circuit reconnect dialog. Every page carries Blazor's well-known <c>#components-reconnect-modal</c>, so
/// the framework uses it instead of its unstyled default, and the shipped stylesheet keeps each state's message hidden
/// until that state's class is on the dialog (the reconnect/resume flow itself is a browser test).
/// </summary>
public sealed partial class ReconnectModalTests
{
    [Test]
    public async Task Every_page_carries_the_reconnect_dialog_with_its_retry_and_resume_controls()
    {
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = factory.CreateWebClient();

        string html = await (await client.GetAsync(new Uri("/login", UriKind.Relative))).Content.ReadAsStringAsync();

        await Assert.That(html).Contains("<dialog id=\"components-reconnect-modal\"");
        await Assert.That(html).Contains("id=\"components-reconnect-button\"");
        await Assert.That(html).Contains("id=\"components-resume-button\"");
        await Assert.That(html).Contains("id=\"components-seconds-to-next-attempt\"");
        await Assert.That(html).Contains("js/reconnect");
        client.Dispose();
    }

    [Test]
    public async Task Generated_app_css_hides_every_state_until_its_class_is_set()
    {
        string css = await File.ReadAllTextAsync(GeneratedAppCssPath());

        await Assert.That(HiddenByDefault().IsMatch(css)).IsTrue()
            .Because("wwwroot/app.css must hide the reconnect dialog's state messages by default (#299)");
        foreach (string state in new[] { "show", "retrying", "failed", "paused", "resume-failed" })
        {
            await Assert.That(css).Contains($".zw-reconnect.components-reconnect-{state}");
        }
    }

    private static string GeneratedAppCssPath([CallerFilePath] string thisFile = "")
    {
        // thisFile: <repo>/tests/ZWarden.Web.Tests/Layout/ReconnectModalTests.cs
        string repoRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", "..", ".."));
        return Path.Combine(repoRoot, "src", "ZWarden.Web", "wwwroot", "app.css");
    }

    [GeneratedRegex(@"\.zw-reconnect \.zw-reconnect-busy,\s*\.zw-reconnect \[class\*=""?zw-reconnect-on-""?\]\s*\{\s*display:\s*none")]
    private static partial Regex HiddenByDefault();
}
