using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;

namespace ZWarden.Web.Tests;

/// <summary>
/// Regression guard for #295: BlazorBlueprint v4 ships its utilities <c>bb:</c>-prefixed in a cascade
/// layer, and an unlayered rule beats any layered one regardless of specificity. If ZWarden's generated
/// <c>wwwroot/app.css</c> ever writes its reset (Tailwind preflight) unlayered again, as Tailwind v3 did,
/// <c>*{border:0}</c> and <c>button{background:transparent;padding:0}</c> silently strip every Bb button,
/// input and card. No component test can see that, so this asserts the shipped stylesheet's shape.
/// </summary>
public sealed class AppCssLayeringTests
{
    private static string GeneratedAppCssPath([CallerFilePath] string thisFile = "")
    {
        string repoRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", ".."));
        return Path.Combine(repoRoot, "src", "ZWarden.Web", "wwwroot", "app.css");
    }

    /// <summary>The stylesheet with every <c>@layer name { … }</c> block removed: what's left is unlayered.</summary>
    internal static string UnlayeredRemainder(string css)
    {
        StringBuilder outside = new();
        int i = 0;
        while (i < css.Length)
        {
            Match layer = Regex.Match(css[i..], @"^@layer\s+[\w-]+\s*\{");
            if (layer.Success)
            {
                int depth = 0;
                int j = i + layer.Length - 1;
                for (; j < css.Length; j++)
                {
                    if (css[j] == '{')
                    {
                        depth++;
                    }
                    else if (css[j] == '}' && --depth == 0)
                    {
                        break;
                    }
                }
                i = j + 1;
                continue;
            }
            outside.Append(css[i]);
            i++;
        }
        return outside.ToString();
    }

    [Test]
    public async Task Unlayered_remainder_drops_layer_blocks_and_keeps_the_rest()
    {
        string css = "a{x:1}@layer base{*{border:0 solid}@media (x){b{y:2}}}.c{z:3}";

        await Assert.That(UnlayeredRemainder(css)).IsEqualTo("a{x:1}.c{z:3}");
    }

    [Test]
    public async Task Generated_app_css_keeps_the_reset_inside_the_base_layer()
    {
        string css = await File.ReadAllTextAsync(GeneratedAppCssPath());
        string unlayered = UnlayeredRemainder(css);

        await Assert.That(Regex.IsMatch(css, @"@layer\s+base\s*\{[^@]*border:\s*0\s+solid"))
            .IsTrue()
            .Because("the Tailwind v4 reset must be emitted inside @layer base");
        await Assert.That(Regex.IsMatch(unlayered, @"(^|[},])\s*\*[^{}]*\{[^}]*border(-width)?:\s*0"))
            .IsFalse()
            .Because("an unlayered border reset beats BlazorBlueprint's layered bb: utilities (#295)");
        await Assert.That(Regex.IsMatch(unlayered, @"(^|[},\s])button[^{}]*\{[^}]*background-color:\s*transparent"))
            .IsFalse()
            .Because("an unlayered button reset strips every BbButton's fill (#295)");
    }

    [Test]
    public async Task Generated_app_css_dark_variant_follows_the_dark_class()
    {
        string css = await File.ReadAllTextAsync(GeneratedAppCssPath());

        await Assert.That(css).Contains(":where(.dark, .dark *)")
            .Because("dark: must follow the .dark class the theme toggle sets, not prefers-color-scheme");
        await Assert.That(css).DoesNotContain("prefers-color-scheme: dark")
            .Because("dark: utilities must not follow the OS preference");
    }
}
