using System.Net;
using System.Net.WebSockets;
using System.Text;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;

namespace ZWarden.IntegrationTests;

/// <summary>
/// F32 tier-2: the reference Caddy ingress (<c>deploy/caddy/Caddyfile</c>) in front of a stub upstream, proving
/// the two behaviours the reverse proxy must guarantee for a self-hosted deployment (PRD §45):
/// the automatic <b>HTTP→HTTPS 308 redirect</b>, and transparent <b>WebSocket upgrade forwarding</b> — the path
/// the SignalR agent hub (F10) and the live-log/console streams (F27/F28) ride. It runs the real
/// <c>caddy:2.11.4</c> image with Caddy's internal CA (the site address is <c>localhost</c>, so Caddy
/// auto-selects its internal issuer and never contacts ACME — D-4). The upstream is an in-process Kestrel echo
/// the Caddy container reaches over <c>host.docker.internal</c>. A missing or malformed Caddyfile fails the
/// container start, so this test is also the semantic backstop the <c>caddy validate</c> gate cannot give.
/// </summary>
[ParallelLimiter<ContainerParallelLimit>]
public sealed class CaddyReferenceDeploymentTests : IAsyncDisposable
{
    private const string CaddyImage = "caddy:2.11.4";

    private WebApplication? _upstream;
    private IContainer? _caddy;

    [Test]
    [Category("Networked")]
    [Timeout(300_000)]
    public async Task Http_is_permanently_redirected_to_https(CancellationToken ct)
    {
        await StartStackAsync(ct);

        int httpPort = _caddy!.GetMappedPublicPort(80);
        using var handler = new HttpClientHandler { AllowAutoRedirect = false };
        using var client = new HttpClient(handler);

        HttpResponseMessage response = await client.GetAsync($"http://localhost:{httpPort}/", ct);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.PermanentRedirect); // 308
        await Assert.That(response.Headers.Location!.Scheme).IsEqualTo("https");
    }

    [Test]
    [Category("Networked")]
    [Timeout(300_000)]
    public async Task Websocket_upgrade_is_forwarded_to_the_upstream(CancellationToken ct)
    {
        await StartStackAsync(ct);

        int httpsPort = _caddy!.GetMappedPublicPort(443);
        using var socket = new ClientWebSocket();
        // Caddy's internal CA is not in the OS trust store; the test asserts forwarding, not chain validation.
#pragma warning disable CA5359 // Test-only: the internal CA cert is untrusted by design here.
        socket.Options.RemoteCertificateValidationCallback = static (_, _, _, _) => true;
#pragma warning restore CA5359

        await socket.ConnectAsync(new Uri($"wss://localhost:{httpsPort}/agent/hub"), ct);
        await socket.SendAsync(Encoding.UTF8.GetBytes("ping"), WebSocketMessageType.Text, endOfMessage: true, ct);

        var buffer = new byte[64];
        WebSocketReceiveResult received = await socket.ReceiveAsync(buffer, ct);

        await Assert.That(socket.State).IsEqualTo(WebSocketState.Open);
        await Assert.That(Encoding.UTF8.GetString(buffer, 0, received.Count)).IsEqualTo("ping");
    }

    /// <summary>Boots the in-process echo upstream, then the Caddy container pointed at it via the real Caddyfile.</summary>
    private async Task StartStackAsync(CancellationToken ct)
    {
        int upstreamPort = await StartUpstreamAsync(ct);

        _caddy = new ContainerBuilder(CaddyImage)
            .WithResourceMapping(await File.ReadAllBytesAsync(CaddyfilePath(), ct), "/etc/caddy/Caddyfile")
            // localhost ⇒ Caddy uses its internal issuer (no ACME); the upstream lives on the test host.
            .WithEnvironment("ZWARDEN_DOMAIN", "localhost")
            .WithEnvironment("ZWARDEN_UPSTREAM", $"host.docker.internal:{upstreamPort}")
            .WithExtraHost("host.docker.internal", "host-gateway")
            .WithPortBinding(80, assignRandomHostPort: true)
            .WithPortBinding(443, assignRandomHostPort: true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("serving initial configuration"))
            .Build();

        await _caddy.StartAsync(ct);
    }

    /// <summary>A minimal Kestrel app: a plain root for the redirect probe and a WebSocket echo at /agent/hub.</summary>
    private async Task<int> StartUpstreamAsync(CancellationToken ct)
    {
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        // Bind to all interfaces so the Caddy container can reach the host over host.docker.internal.
        builder.WebHost.UseUrls("http://0.0.0.0:0");
        WebApplication app = builder.Build();

        app.UseWebSockets();
        app.MapGet("/", () => "zwarden-upstream");
        app.Map("/agent/hub", async context =>
        {
            if (!context.WebSockets.IsWebSocketRequest)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            using WebSocket ws = await context.WebSockets.AcceptWebSocketAsync();
            var buffer = new byte[1024];
            WebSocketReceiveResult message = await ws.ReceiveAsync(buffer, context.RequestAborted);
            await ws.SendAsync(buffer.AsMemory(0, message.Count), WebSocketMessageType.Text, endOfMessage: true, context.RequestAborted);
            await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", context.RequestAborted);
        });

        await app.StartAsync(ct);
        _upstream = app;

        string address = app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!.Addresses.First();
        return new Uri(address).Port;
    }

    private static string CaddyfilePath()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ZWarden.slnx")))
        {
            dir = dir.Parent;
        }

        if (dir is null)
        {
            throw new InvalidOperationException("Could not locate the repository root (ZWarden.slnx) from the test output directory.");
        }

        return Path.Combine(dir.FullName, "deploy", "caddy", "Caddyfile");
    }

    public async ValueTask DisposeAsync()
    {
        if (_caddy is not null)
        {
            await _caddy.DisposeAsync();
        }

        if (_upstream is not null)
        {
            await _upstream.DisposeAsync();
        }
    }
}
