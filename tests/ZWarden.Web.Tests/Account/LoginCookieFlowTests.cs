using System.Net;
using System.Text.RegularExpressions;
using ZWarden.TestSupport;

namespace ZWarden.Web.Tests.Account;

/// <summary>
/// F4 UI (#63): the cookie flows proven end-to-end over the real host — sign-in issues the hardened
/// <c>zwarden.auth</c> cookie, wrong credentials and lockout are handled, protected pages redirect
/// anonymous users to /login, sign-out clears the cookie, MFA gates the app cookie behind the second
/// factor, and a password-reset token is single-use. These exercise the actual cookie middleware,
/// antiforgery, and host filtering — the parts a bUnit render cannot reach.
/// </summary>
public sealed class LoginCookieFlowTests
{
    private const string StrongPassword = "correct horse battery staple";
    private const string AuthCookieName = "zwarden.auth";

    [Test]
    public async Task Get_login_renders_a_form_with_an_antiforgery_token()
    {
        await using ZWardenWebAppFactory factory = new();
        using HttpClient client = factory.CreateWebClient();

        HttpResponseMessage response = await client.GetAsync(new Uri("/login", UriKind.Relative));
        string html = await response.Content.ReadAsStringAsync();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(html).Contains("__RequestVerificationToken");
    }

    [Test]
    public async Task Valid_credentials_sign_in_and_set_the_hardened_cookie()
    {
        await using ZWardenWebAppFactory factory = new();
        await factory.CreateConfirmedUserAsync("pilot@zwarden.test", StrongPassword);
        using HttpClient client = factory.CreateWebClient();

        HttpResponseMessage response = await PostFormAsync(client, "/login", "/login", new()
        {
            ["Input.Email"] = "pilot@zwarden.test",
            ["Input.Password"] = StrongPassword,
        });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Redirect);
        await Assert.That(LocationPath(response)).IsEqualTo("/");

        string setCookie = AuthSetCookie(response);
        await Assert.That(setCookie).Contains("httponly");
        await Assert.That(setCookie).Contains("secure");
        await Assert.That(setCookie).Contains("samesite=lax");
    }

    [Test]
    public async Task Wrong_password_does_not_sign_in()
    {
        await using ZWardenWebAppFactory factory = new();
        await factory.CreateConfirmedUserAsync("pilot@zwarden.test", StrongPassword);
        using HttpClient client = factory.CreateWebClient();

        HttpResponseMessage response = await PostFormAsync(client, "/login", "/login", new()
        {
            ["Input.Email"] = "pilot@zwarden.test",
            ["Input.Password"] = "the wrong password entirely",
        });
        string html = await response.Content.ReadAsStringAsync();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK); // re-rendered form, not a redirect
        await Assert.That(html).Contains("Invalid login attempt.");
        await Assert.That(HasAuthCookie(response)).IsFalse();
    }

    [Test]
    public async Task Repeated_failures_lock_the_account()
    {
        await using ZWardenWebAppFactory factory = new();
        await factory.CreateConfirmedUserAsync("locked@zwarden.test", StrongPassword);
        using HttpClient client = factory.CreateWebClient();

        bool lockedOut = false;
        for (int attempt = 0; attempt < 6 && !lockedOut; attempt++)
        {
            HttpResponseMessage response = await PostFormAsync(client, "/login", "/login", new()
            {
                ["Input.Email"] = "locked@zwarden.test",
                ["Input.Password"] = "still not the password",
            });

            lockedOut = response.StatusCode == HttpStatusCode.Redirect
                && LocationPath(response) == "/login/lockout";
        }

        // MaxFailedAccessAttempts = 5, so lockout is reached within the loop.
        await Assert.That(lockedOut).IsTrue();
    }

    [Test]
    public async Task Anonymous_request_to_a_protected_page_redirects_to_login()
    {
        await using ZWardenWebAppFactory factory = new();
        using HttpClient client = factory.CreateWebClient();

        HttpResponseMessage response = await client.GetAsync(new Uri("/account/manage", UriKind.Relative));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Redirect);
        await Assert.That(LocationPath(response)).StartsWith("/login");
    }

    [Test]
    public async Task Sign_out_clears_the_cookie()
    {
        await using ZWardenWebAppFactory factory = new();
        await factory.CreateConfirmedUserAsync("bye@zwarden.test", StrongPassword);
        using HttpClient client = factory.CreateWebClient();

        await PostFormAsync(client, "/login", "/login", new()
        {
            ["Input.Email"] = "bye@zwarden.test",
            ["Input.Password"] = StrongPassword,
        });
        await Assert.That((await client.GetAsync(new Uri("/account/manage", UriKind.Relative))).StatusCode)
            .IsEqualTo(HttpStatusCode.OK); // confirms the session is live

        HttpResponseMessage logout = await PostFormAsync(client, "/account/manage", "/logout", []);

        await Assert.That(logout.StatusCode).IsEqualTo(HttpStatusCode.Redirect);
        await Assert.That(AuthSetCookie(logout)).StartsWith($"{AuthCookieName}=;");
    }

    [Test]
    public async Task Mfa_user_is_challenged_then_signs_in_with_a_totp_code()
    {
        await using ZWardenWebAppFactory factory = new();
        await factory.CreateConfirmedUserAsync("mfa@zwarden.test", StrongPassword);
        string key = await factory.EnableAuthenticatorAsync("mfa@zwarden.test");
        using HttpClient client = factory.CreateWebClient();

        HttpResponseMessage passwordStep = await PostFormAsync(client, "/login", "/login", new()
        {
            ["Input.Email"] = "mfa@zwarden.test",
            ["Input.Password"] = StrongPassword,
        });

        await Assert.That(passwordStep.StatusCode).IsEqualTo(HttpStatusCode.Redirect);
        await Assert.That(LocationPath(passwordStep)).StartsWith("/login/2fa");
        await Assert.That(HasAuthCookie(passwordStep)).IsFalse(); // app cookie withheld until the 2nd factor

        HttpResponseMessage codeStep = await PostFormAsync(client, "/login/2fa", "/login/2fa", new()
        {
            ["Input.Code"] = Totp.Compute(key),
        });

        await Assert.That(codeStep.StatusCode).IsEqualTo(HttpStatusCode.Redirect);
        await Assert.That(HasAuthCookie(codeStep)).IsTrue();
    }

    [Test]
    public async Task A_password_reset_token_works_once()
    {
        await using ZWardenWebAppFactory factory = new();
        await factory.CreateConfirmedUserAsync("reset@zwarden.test", StrongPassword);
        string token = await factory.CreatePasswordResetTokenAsync("reset@zwarden.test");
        using HttpClient client = factory.CreateWebClient();

        Dictionary<string, string> fields = new()
        {
            ["Input.Email"] = "reset@zwarden.test",
            ["Input.Code"] = token,
            ["Input.Password"] = "a whole new passphrase",
            ["Input.ConfirmPassword"] = "a whole new passphrase",
        };

        HttpResponseMessage first = await PostFormAsync(client, "/account/reset-password", "/account/reset-password", fields);
        await Assert.That(first.StatusCode).IsEqualTo(HttpStatusCode.Redirect);
        await Assert.That(LocationPath(first)).IsEqualTo("/login");

        HttpResponseMessage replay = await PostFormAsync(client, "/account/reset-password", "/account/reset-password", fields);
        string replayHtml = await replay.Content.ReadAsStringAsync();
        await Assert.That(replay.StatusCode).IsEqualTo(HttpStatusCode.OK); // rejected, form re-rendered
        await Assert.That(replayHtml).Contains("Invalid or expired reset link.");
    }

    // --- helpers -------------------------------------------------------------

    // Posts a Blazor SSR form: GET the page to collect its hidden inputs (antiforgery token + form
    // handler), merge the caller's field values over them, and POST. HandleCookies carries the
    // antiforgery cookie between the two requests.
    private static async Task<HttpResponseMessage> PostFormAsync(
        HttpClient client, string getUrl, string postUrl, Dictionary<string, string> fields)
    {
        HttpResponseMessage page = await client.GetAsync(new Uri(getUrl, UriKind.Relative));
        string html = await page.Content.ReadAsStringAsync();

        Dictionary<string, string> form = ParseHiddenInputs(html);
        foreach (KeyValuePair<string, string> field in fields)
        {
            form[field.Key] = field.Value;
        }

        return await client.PostAsync(new Uri(postUrl, UriKind.Relative), new FormUrlEncodedContent(form));
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
                inputs[name.Groups[1].Value] = value.Success ? WebUtility.HtmlDecode(value.Groups[1].Value) : string.Empty;
            }
        }

        return inputs;
    }

    // Static-SSR NavigateTo emits an absolute Location (https://localhost/...); compare the path only.
    private static string LocationPath(HttpResponseMessage response)
    {
        Uri location = response.Headers.Location
            ?? throw new InvalidOperationException("No Location header on the response.");
        return location.IsAbsoluteUri ? location.PathAndQuery : location.OriginalString;
    }

    private static bool HasAuthCookie(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out IEnumerable<string>? cookies)
        && cookies.Any(c => c.StartsWith($"{AuthCookieName}=", StringComparison.OrdinalIgnoreCase)
                            && !c.StartsWith($"{AuthCookieName}=;", StringComparison.OrdinalIgnoreCase));

    private static string AuthSetCookie(HttpResponseMessage response)
    {
        IEnumerable<string> cookies = response.Headers.TryGetValues("Set-Cookie", out IEnumerable<string>? values)
            ? values
            : [];
        return cookies.FirstOrDefault(c => c.StartsWith($"{AuthCookieName}=", StringComparison.OrdinalIgnoreCase))
            ?.ToLowerInvariant()
            ?? throw new InvalidOperationException("No zwarden.auth Set-Cookie header on the response.");
    }
}
