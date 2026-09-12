using Bunit;
using Microsoft.AspNetCore.Components;
using ZWarden.Web.Components.Account.Shared;

namespace ZWarden.Web.Tests.Account;

/// <summary>
/// F4 UI (#63) bUnit coverage for the ZWarden-owned auth presentation components — the wrapper-seam
/// pieces (ADR 0003) the static auth pages compose. Markup/behaviour only; the cookie flows are proven
/// separately over the real host (<see cref="LoginCookieFlowTests"/>).
/// </summary>
public class AccountComponentTests
{
    [Test]
    [Arguments("Error", "alert")]
    [Arguments("Success", "status")]
    [Arguments("Info", "status")]
    public async Task StatusAlert_renders_the_message_with_the_right_role(string kind, string expectedRole)
    {
        using BunitContext ctx = new();
        StatusAlert.AlertKind alertKind = Enum.Parse<StatusAlert.AlertKind>(kind);

        IRenderedComponent<StatusAlert> cut = ctx.Render<StatusAlert>(p => p
            .Add(c => c.Message, "Something to report")
            .Add(c => c.Kind, alertKind));

        await Assert.That(cut.Find("div").GetAttribute("role")).IsEqualTo(expectedRole);
        await Assert.That(cut.Find("div").GetAttribute("data-alert-kind")).IsEqualTo(kind.ToLowerInvariant());
        await Assert.That(cut.Markup).Contains("Something to report");
    }

    [Test]
    public async Task StatusAlert_renders_nothing_when_the_message_is_blank()
    {
        using BunitContext ctx = new();

        IRenderedComponent<StatusAlert> cut = ctx.Render<StatusAlert>(p => p.Add(c => c.Message, "   "));

        await Assert.That(cut.Markup.Trim()).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task RecoveryCodesPanel_lists_every_code()
    {
        using BunitContext ctx = new();
        string[] codes = ["aaaa-1111", "bbbb-2222", "cccc-3333"];

        IRenderedComponent<RecoveryCodesPanel> cut = ctx.Render<RecoveryCodesPanel>(p => p.Add(c => c.Codes, codes));

        await Assert.That(cut.FindAll("[data-recovery-code]").Count).IsEqualTo(3);
        await Assert.That(cut.Markup).Contains("bbbb-2222");
    }

    [Test]
    public async Task RecoveryCodesPanel_renders_nothing_without_codes()
    {
        using BunitContext ctx = new();

        IRenderedComponent<RecoveryCodesPanel> cut = ctx.Render<RecoveryCodesPanel>(p => p.Add(c => c.Codes, []));

        await Assert.That(cut.Markup.Trim()).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task AuthenticatorSetupPanel_shows_the_formatted_key_and_a_qr()
    {
        using BunitContext ctx = new();

        IRenderedComponent<AuthenticatorSetupPanel> cut = ctx.Render<AuthenticatorSetupPanel>(p => p
            .Add(c => c.UnformattedKey, "JBSWY3DPEHPK3PXP")
            .Add(c => c.Email, "pilot@zwarden.test"));

        // Key grouped in fours, lower-cased for manual entry.
        await Assert.That(cut.Find("[data-authenticator-key]").TextContent).IsEqualTo("jbsw y3dp ehpk 3pxp");
        // Server-side QR rendered inline, no JS interop.
        await Assert.That(cut.FindAll("svg").Count).IsGreaterThanOrEqualTo(1);
    }

    [Test]
    public async Task AuthLayout_carries_the_wordmark_and_tagline()
    {
        using BunitContext ctx = new();
        RenderFragment body = builder => builder.AddMarkupContent(0, "<p id=\"probe\">panel body</p>");

        IRenderedComponent<AuthLayout> cut = ctx.Render<AuthLayout>(p => p.Add(c => c.Body, body));

        await Assert.That(cut.Markup).Contains("ZWarden");
        await Assert.That(cut.Markup).Contains("This is how you deploy.");
        await Assert.That(cut.Find("#probe").TextContent).IsEqualTo("panel body");
    }
}
