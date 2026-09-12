using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace ZWarden.Web.Tests;

/// <summary>
/// Regression guard for issue #70: the default Blazor <c>#blazor-error-ui</c> bar must be hidden on
/// normal page loads and only appear on a circuit error. The stock template ships a
/// <c>#blazor-error-ui { display: none }</c> rule in its site CSS; when F0 (ADR 0003) swapped that
/// pipeline for Tailwind (<c>Styles/app.tailwind.css</c> → committed <c>wwwroot/app.css</c>) the rule
/// was dropped, so the element defaulted to visible on every page. This asserts the rule survives in
/// the <b>generated</b> stylesheet that actually ships — the artifact CI's stale-css guard diffs.
/// </summary>
public sealed class BlazorErrorUiStyleTests
{
    private static string GeneratedAppCssPath([CallerFilePath] string thisFile = "")
    {
        // thisFile: <repo>/tests/ZWarden.Web.Tests/BlazorErrorUiStyleTests.cs
        string repoRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", ".."));
        return Path.Combine(repoRoot, "src", "ZWarden.Web", "wwwroot", "app.css");
    }

    [Test]
    public async Task Generated_app_css_hides_the_blazor_error_ui_by_default()
    {
        string css = await File.ReadAllTextAsync(GeneratedAppCssPath());

        // The base #blazor-error-ui rule (not the .show variant) must set display:none, so the bar is
        // hidden until the framework toggles it on a circuit error.
        Match baseRule = Regex.Match(css, @"#blazor-error-ui\s*\{(?<body>[^}]*)\}");

        await Assert.That(baseRule.Success)
            .IsTrue()
            .Because("wwwroot/app.css must carry a #blazor-error-ui rule (issue #70)");
        await Assert.That(Regex.IsMatch(baseRule.Groups["body"].Value, @"display:\s*none"))
            .IsTrue()
            .Because("the default #blazor-error-ui rule must set display:none so the bar is hidden on normal loads");
    }
}
