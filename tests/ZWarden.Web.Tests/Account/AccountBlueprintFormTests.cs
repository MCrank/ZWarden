using System.Net;
using System.Text.RegularExpressions;

namespace ZWarden.Web.Tests.Account;

/// <summary>
/// Issue #84: after the Account pages adopt the Blueprint form primitives behind the wrapper seam
/// (ADR 0003), the static-SSR forms must still render the exact model-path field names the auth
/// handlers bind (<c>[SupplyParameterFromForm]</c>), and a POST through those controls must still work.
/// These GET-render assertions guard the explicit-name contract (BbInput/BbCheckbox derive the full
/// <c>Input.*</c> path from the bound expression) alongside the end-to-end flows in
/// <see cref="LoginCookieFlowTests"/> and <see cref="ZWarden.Web.Tests.MfaTests"/>.
/// </summary>
public sealed class AccountBlueprintFormTests
{
    private const string StrongPassword = "correct horse battery staple";

    [Test]
    public async Task The_login_form_renders_the_blueprint_controls_with_the_binding_names()
    {
        await using ZWardenWebAppFactory factory = new();
        using HttpClient client = factory.CreateWebClient();

        string html = await (await client.GetAsync(new Uri("/login", UriKind.Relative))).Content.ReadAsStringAsync();

        // BbInput derives the full model path from @bind-Value; the password input carries type=password.
        await Assert.That(html).Contains("name=\"Input.Email\"");
        await Assert.That(html).Contains("name=\"Input.Password\"");
        await Assert.That(html).Contains("type=\"password\"");
        // BbCheckbox must post the full model path Input.RememberMe (auto-derived from @bind-Checked).
        await Assert.That(html).Contains("name=\"Input.RememberMe\"");
        // Splatted HTML attributes survive the wrapper (accessibility/UX affordances).
        await Assert.That(html).Contains("autocomplete=\"username\"");
    }

    [Test]
    public async Task Remember_me_binds_through_the_blueprint_checkbox_on_post()
    {
        // The checkbox POST end to end: a checked BbCheckbox makes the sign-in persistent, so the auth
        // cookie carries an expiry. This proves the checkbox's rendered name binds to Input.RememberMe.
        await using ZWardenWebAppFactory factory = new();
        await factory.CreateConfirmedUserAsync("remember@zwarden.test", StrongPassword);
        using HttpClient client = factory.CreateWebClient();

        HttpResponseMessage page = await client.GetAsync(new Uri("/login", UriKind.Relative));
        Dictionary<string, string> form = ParseHiddenInputs(await page.Content.ReadAsStringAsync());
        form["Input.Email"] = "remember@zwarden.test";
        form["Input.Password"] = StrongPassword;
        form["Input.RememberMe"] = "true";

        HttpResponseMessage response = await client.PostAsync(
            new Uri("/login", UriKind.Relative), new FormUrlEncodedContent(form));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Redirect);
        string setCookie = AuthSetCookie(response);
        // Persistent sign-in stamps an expires attribute; a session cookie would not.
        await Assert.That(setCookie).Contains("expires");
    }

    [Test]
    public async Task The_reset_password_form_renders_the_blueprint_controls_with_the_binding_names()
    {
        await using ZWardenWebAppFactory factory = new();
        using HttpClient client = factory.CreateWebClient();

        string html = await (await client.GetAsync(new Uri("/account/reset-password", UriKind.Relative)))
            .Content.ReadAsStringAsync();

        await Assert.That(html).Contains("name=\"Input.Email\"");
        await Assert.That(html).Contains("name=\"Input.Password\"");
        await Assert.That(html).Contains("name=\"Input.ConfirmPassword\"");
        // The token still round-trips as a raw hidden input under its exact name.
        await Assert.That(html).Contains("name=\"Input.Code\"");
    }

    [Test]
    public async Task The_manage_page_renders_the_two_factor_action_as_a_blueprint_link_button()
    {
        // #121: the "Enable authenticator" action was a hand-styled <a>; it now rides BbButton (Href → anchor),
        // so the copied primary-button utilities are gone while the navigation target is unchanged.
        await using ZWardenWebAppFactory factory = new();
        await factory.CreateConfirmedUserAsync("manage@zwarden.test", StrongPassword);
        using HttpClient client = factory.CreateWebClient();
        await SignInAsync(client, "manage@zwarden.test", StrongPassword);

        string html = await (await client.GetAsync(new Uri("/account/manage", UriKind.Relative)))
            .Content.ReadAsStringAsync();

        await Assert.That(html).Contains("href=\"/account/manage/enable-authenticator\"");
        await Assert.That(html).Contains("Enable authenticator (2FA)");
        // The removed hand-rolled primary-button hover utility must not survive the conversion.
        await Assert.That(html).DoesNotContain("hover:opacity-90");
    }

    private static async Task SignInAsync(HttpClient client, string email, string password)
    {
        HttpResponseMessage page = await client.GetAsync(new Uri("/login", UriKind.Relative));
        Dictionary<string, string> form = ParseHiddenInputs(await page.Content.ReadAsStringAsync());
        form["Input.Email"] = email;
        form["Input.Password"] = password;
        await client.PostAsync(new Uri("/login", UriKind.Relative), new FormUrlEncodedContent(form));
    }

    private static Dictionary<string, string> ParseHiddenInputs(string html)
    {
        Dictionary<string, string> inputs = new(StringComparer.Ordinal);
        foreach (Match tag in Regex.Matches(html, "<input\\b[^>]*?type=\"hidden\"[^>]*?>"))
        {
            Match name = Regex.Match(tag.Value, "name=\"([^\"]+)\"");
            Match value = Regex.Match(tag.Value, "value=\"([^\"]*)\"");
            if (name.Success)
            {
                inputs[name.Groups[1].Value] =
                    value.Success ? WebUtility.HtmlDecode(value.Groups[1].Value) : string.Empty;
            }
        }

        return inputs;
    }

    private static string AuthSetCookie(HttpResponseMessage response)
    {
        IEnumerable<string> cookies = response.Headers.TryGetValues("Set-Cookie", out IEnumerable<string>? values)
            ? values
            : [];
        return cookies.FirstOrDefault(c => c.StartsWith("zwarden.auth=", StringComparison.OrdinalIgnoreCase))
            ?.ToLowerInvariant()
            ?? throw new InvalidOperationException("No zwarden.auth Set-Cookie header on the response.");
    }
}
