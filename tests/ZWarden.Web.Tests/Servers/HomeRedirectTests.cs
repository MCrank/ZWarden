using System.Net;
using ZWarden.Web.Tests.Account;

namespace ZWarden.Web.Tests.Servers;

/// <summary>
/// The application entry point routes to something real. A completed install serving <c>/</c> redirects to the
/// fleet dashboard (which in turn redirects a signed-out visitor on to /login), rather than showing a landing
/// page — so opening ZWarden never dead-ends on scaffolding. Tier-1 offline.
/// </summary>
public class HomeRedirectTests
{
    [Test]
    public async Task The_root_redirects_to_the_servers_dashboard()
    {
        await using ZWardenWebAppFactory factory = new(); // setup complete by default, so the first-run gate is off
        HttpClient client = factory.CreateWebClient();     // does not auto-follow redirects

        HttpResponseMessage response = await client.GetAsync("/");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Redirect);
        await Assert.That(response.Headers.Location!.ToString()).Contains("/servers");
    }
}
