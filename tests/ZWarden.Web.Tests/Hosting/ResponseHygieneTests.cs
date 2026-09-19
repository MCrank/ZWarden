using System.Net;
using Microsoft.AspNetCore.Hosting;
using ZWarden.Web.Tests.Account;

namespace ZWarden.Web.Tests.Hosting;

/// <summary>
/// #187: HTTP response hygiene behind the Caddy front door (ADR 0035). Caddy terminates TLS, redirects
/// HTTP→HTTPS, and emits the single canonical HSTS header, so the app must not emit its own HSTS (which
/// produced a duplicate, non-conformant header), and the referenced <c>/favicon.png</c> must actually be
/// served (it 404'd on every page).
/// </summary>
public sealed class ResponseHygieneTests
{
    [Test]
    public async Task Favicon_png_is_served()
    {
        await using ZWardenWebAppFactory factory = new();
        using HttpClient client = factory.CreateWebClient();

        using HttpResponseMessage response = await client.GetAsync(new Uri("/favicon.png", UriKind.Relative));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(response.Content.Headers.ContentType?.MediaType).IsEqualTo("image/png");
    }

    [Test]
    public async Task The_app_does_not_emit_its_own_hsts_header_behind_the_proxy()
    {
        // Production is where the app used to add UseHsts. Requesting under a non-localhost allowed host means
        // the HSTS middleware's localhost exclusion would NOT apply — so if the app still emitted HSTS, the
        // header would be present. Its absence proves the app leaves HSTS to Caddy (ADR 0035).
        await using ProductionFactory factory = new();
        using HttpClient client = factory.CreateWebClient();

        using HttpRequestMessage request = new(HttpMethod.Get, new Uri("/healthz", UriKind.Relative));
        request.Headers.Host = "app.example.test";
        using HttpResponseMessage response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(response.Headers.Contains("Strict-Transport-Security")).IsFalse();
    }

    /// <summary>A Production-environment host that also trusts a non-localhost host, so the HSTS middleware's
    /// built-in localhost exclusion cannot mask an app-emitted HSTS header.</summary>
    private sealed class ProductionFactory : ZWardenWebAppFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            ArgumentNullException.ThrowIfNull(builder);
            base.ConfigureWebHost(builder);
            builder.UseEnvironment("Production");
            builder.UseSetting("ZWarden:AllowedHosts:0", "app.example.test");
            builder.UseSetting("ZWarden:AllowedHosts:1", "localhost");
        }
    }
}
