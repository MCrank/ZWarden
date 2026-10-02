using System.Net;
using ZWarden.Web.Hosting;
using ZWarden.Web.Tests.Account;

namespace ZWarden.Web.Tests.Hosting;

/// <summary>
/// #224: a static form post the form reader refuses (over its value-count limit) is redirected back to the same page
/// with <c>formRejected=too-large</c> rather than ending on a bare HTTP 400. Server Detail no longer posts forms
/// (#299), so this is exercised on a static page that does: the sign-in form.
/// </summary>
public sealed class FormLimitGuardTests
{
    [Test]
    public async Task An_oversized_static_form_post_redirects_back_instead_of_a_bare_400()
    {
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = factory.CreateWebClient();
        IEnumerable<KeyValuePair<string, string>> form =
            Enumerable.Range(0, 10_000).Select(i => new KeyValuePair<string, string>($"x{i}", "1"));

        HttpResponseMessage post = await client.PostAsync(new Uri("/login?returnUrl=%2F", UriKind.Relative), new FormUrlEncodedContent(form));

        await Assert.That(post.StatusCode).IsEqualTo(HttpStatusCode.SeeOther);
        string location = post.Headers.Location!.OriginalString;
        await Assert.That(location).StartsWith("/login?returnUrl=%2F");
        await Assert.That(location).Contains($"{FormLimitGuardMiddleware.RejectedQueryKey}=too-large");
        client.Dispose();
    }

    [Test]
    public async Task An_api_post_is_never_guarded()
    {
        // Only antiforgery-validated endpoints (the static forms) are guarded; an API keeps its own answer.
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = factory.CreateWebClient();
        IEnumerable<KeyValuePair<string, string>> form =
            Enumerable.Range(0, 10_000).Select(i => new KeyValuePair<string, string>($"x{i}", "1"));

        HttpResponseMessage post = await client.PostAsync(
            new Uri("/api/servers/srv-00000000-0000-7000-8000-000000000000/start", UriKind.Relative), new FormUrlEncodedContent(form));

        await Assert.That(post.StatusCode).IsNotEqualTo(HttpStatusCode.SeeOther);
        client.Dispose();
    }
}
