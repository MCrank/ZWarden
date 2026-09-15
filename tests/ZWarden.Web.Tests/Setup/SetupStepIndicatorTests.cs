using System.Text.RegularExpressions;
using Bunit;
using ZWarden.Web.Components.Pages.Setup;

namespace ZWarden.Web.Tests.Setup;

/// <summary>
/// F33: the static, no-circuit <see cref="SetupStepIndicator"/> that gives the first-run wizard its "feel"
/// without BbFormWizard's interactive circuit (ADR 0036). It renders the five steps and marks the current
/// one active, earlier ones complete, and later ones pending.
/// </summary>
public class SetupStepIndicatorTests
{
    [Test]
    public async Task It_renders_all_five_setup_steps()
    {
        using BunitContext ctx = new();

        var cut = ctx.Render<SetupStepIndicator>(p => p.Add(c => c.Current, 1));

        string markup = cut.Markup;
        await Assert.That(markup).Contains("data-setup-steps");
        await Assert.That(markup).Contains("Administrator");
        await Assert.That(markup).Contains("TLS mode");
        await Assert.That(markup).Contains("Enroll Agent");
        await Assert.That(Regex.Count(markup, "data-setup-step=")).IsEqualTo(5);
    }

    [Test]
    [Arguments(1)]
    [Arguments(2)]
    public async Task It_marks_current_active_earlier_complete_and_later_pending(int current)
    {
        using BunitContext ctx = new();

        var cut = ctx.Render<SetupStepIndicator>(p => p.Add(c => c.Current, current));

        string markup = cut.Markup;
        await Assert.That(markup).Contains($"data-setup-step=\"{current}\" data-state=\"active\"");
        await Assert.That(markup).Contains("data-state=\"pending\""); // steps after the current one
        if (current > 1)
        {
            await Assert.That(markup).Contains("data-state=\"complete\""); // steps before the current one
        }
    }
}
