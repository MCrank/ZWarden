using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ZWarden.Web.Hosting;

namespace ZWarden.Web.Tests.Hosting;

/// <summary>
/// F32: ZWarden.Web must trust the reference Caddy ingress that terminates TLS in front of it (ADR 0035), so it
/// honours X-Forwarded-Proto/Host/For and clears the default loopback-only proxy allowlists. These are the
/// deterministic, offline assertions over that decision; the end-to-end proxy behaviour is proven by the
/// networked <c>CaddyReferenceDeploymentTests</c>.
/// </summary>
public class ForwardedHeadersExtensionsTests
{
    [Test]
    public async Task AddProxyForwardedHeaders_honours_proto_host_and_for()
    {
        await using ServiceProvider provider = new ServiceCollection()
            .AddProxyForwardedHeaders()
            .BuildServiceProvider();

        ForwardedHeadersOptions options = provider.GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value;

        await Assert.That(options.ForwardedHeaders.HasFlag(ForwardedHeaders.XForwardedProto)).IsTrue();
        await Assert.That(options.ForwardedHeaders.HasFlag(ForwardedHeaders.XForwardedHost)).IsTrue();
        await Assert.That(options.ForwardedHeaders.HasFlag(ForwardedHeaders.XForwardedFor)).IsTrue();
    }

    [Test]
    public async Task AddProxyForwardedHeaders_trusts_the_proxy_by_clearing_the_loopback_allowlists()
    {
        await using ServiceProvider provider = new ServiceCollection()
            .AddProxyForwardedHeaders()
            .BuildServiceProvider();

        ForwardedHeadersOptions options = provider.GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value;

        await Assert.That(options.KnownIPNetworks).IsEmpty();
        await Assert.That(options.KnownProxies).IsEmpty();
    }
}
