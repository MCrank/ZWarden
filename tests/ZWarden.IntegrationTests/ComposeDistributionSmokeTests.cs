using System.Net;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Images;

namespace ZWarden.IntegrationTests;

/// <summary>
/// F34 tier-2: builds the real <c>ZWarden.Web</c> image from its Dockerfile (<c>src/ZWarden.Web/Dockerfile</c>)
/// and boots it in SQLite mode — the base Compose mode (ADR 0037) — proving the shipped image actually starts,
/// applies its migrations, runs <b>non-root</b>, and serves the ungated <c>/healthz</c> liveness endpoint the
/// health-ordered Compose startup depends on (D-5). The structural wiring is guarded offline by
/// <c>ComposeDistributionTests</c>; this is the networked backstop that the image is genuinely runnable.
/// </summary>
[ParallelLimiter<ContainerParallelLimit>]
public sealed class ComposeDistributionSmokeTests : IAsyncDisposable
{
    private IFutureDockerImage? _image;
    private IContainer? _web;

    [Test]
    [Category("Networked")]
    [Timeout(900_000)]
    public async Task The_web_image_boots_in_sqlite_mode_runs_non_root_and_serves_healthz(CancellationToken ct)
    {
        IContainer web = await StartWebImageAsync(ct);

        // The liveness endpoint answers 200 over plain HTTP (the in-stack scheme behind Caddy).
        int port = web.GetMappedPublicPort(8080);
        using var client = new HttpClient();
        HttpResponseMessage response = await client.GetAsync($"http://localhost:{port}/healthz", ct);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        // Non-root runtime (PRD 24): the Dockerfile's dedicated uid 10001, not root.
        ExecResult whoami = await web.ExecAsync(["id", "-u"], ct);
        await Assert.That(whoami.Stdout.Trim()).IsEqualTo("10001");
    }

    /// <summary>Builds the Web image from its Dockerfile (repo-root context, as the Compose file uses) and boots
    /// it in SQLite mode, waiting until <c>/healthz</c> answers.</summary>
    private async Task<IContainer> StartWebImageAsync(CancellationToken ct)
    {
        _image = new ImageFromDockerfileBuilder()
            .WithName($"zwarden-web-smoke:{Guid.NewGuid():N}")
            .WithDockerfileDirectory(RepoRoot())
            .WithDockerfile("src/ZWarden.Web/Dockerfile")
            .WithCleanUp(true)
            .Build();
        await _image.CreateAsync(ct);

        _web = new ContainerBuilder(_image.FullName)
            // The base SQLite mode wiring (ADR 0037): provider, a writable on-volume DB path, and the
            // fail-closed key ring the security foundation (ADR 0015) refuses to start without.
            .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Production")
            .WithEnvironment("ZWarden__Database__Provider", "sqlite")
            .WithEnvironment("ConnectionStrings__ZWarden", "Data Source=/data/zwarden.db")
            .WithEnvironment("ZW_SECRET_KEYS", $"k1:{Convert.ToBase64String(new byte[32])}")
            .WithEnvironment("ZW_SECRET_ACTIVE_KEY_ID", "k1")
            .WithPortBinding(8080, assignRandomHostPort: true)
            .WithWaitStrategy(Wait.ForUnixContainer()
                .UntilHttpRequestIsSucceeded(r => r.ForPath("/healthz").ForPort(8080)))
            .Build();

        await _web.StartAsync(ct);
        return _web;
    }

    private static string RepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ZWarden.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Could not locate the repository root (ZWarden.slnx).");
    }

    public async ValueTask DisposeAsync()
    {
        if (_web is not null)
        {
            await _web.DisposeAsync();
        }

        if (_image is not null)
        {
            await _image.DisposeAsync();
        }
    }
}
