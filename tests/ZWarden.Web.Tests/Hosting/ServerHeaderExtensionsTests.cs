using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ZWarden.Web.Hosting;

namespace ZWarden.Web.Tests.Hosting;

/// <summary>
/// #187: ZWarden.Web should not disclose the <c>Server: Kestrel</c> header through the Caddy front door
/// (ADR 0035). The composition suppresses it on Kestrel's options.
/// </summary>
public sealed class ServerHeaderExtensionsTests
{
    [Test]
    public async Task SuppressKestrelServerHeader_disables_the_server_header()
    {
        await using ServiceProvider provider = new ServiceCollection()
            .SuppressKestrelServerHeader()
            .BuildServiceProvider();

        KestrelServerOptions options = provider.GetRequiredService<IOptions<KestrelServerOptions>>().Value;

        await Assert.That(options.AddServerHeader).IsFalse();
    }
}
