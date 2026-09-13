using Bunit;
using Microsoft.Extensions.DependencyInjection;
using ZWarden.Application.Servers;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Servers;
using ZWarden.Infrastructure.Servers;
using ZWarden.Web.Components.Pages.Servers;

namespace ZWarden.Web.Tests.Servers;

/// <summary>
/// F16 PR-C: the interactive live panel reads the ownership-guarded metrics + health caches (no tenant context)
/// and renders the health rollup, its reason, and MeterBars for the resource samples. With nothing cached it
/// shows the "awaiting" state.
/// </summary>
public class LiveServerPanelTests
{
    private static readonly DateTimeOffset At = new(2026, 9, 13, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task It_renders_health_reason_and_meters_from_the_caches()
    {
        AgentId agent = AgentId.New();
        ServerId server = ServerId.New();
        ServerMetricsCache metrics = new();
        metrics.Record([new ServerMetrics(agent, server, 42, 2_000_000_000, 4_000_000_000, 10_000_000_000, 50_000_000_000, null, At)]);
        ServerHealthCache health = new();
        health.Record(new ServerLiveHealth(agent, server, ServerHealth.Degraded, "a port is unreachable", At));

        using BunitContext ctx = new();
        ctx.Services.AddSingleton<IServerMetricsCache>(metrics);
        ctx.Services.AddSingleton<IServerHealthCache>(health);

        var cut = ctx.Render<LiveServerPanel>(p => p
            .Add(c => c.ServerId, server.ToString())
            .Add(c => c.AgentId, agent.ToString()));

        string markup = cut.Markup;
        await Assert.That(markup).Contains("Degraded");
        await Assert.That(markup).Contains("a port is unreachable");
        // 42% CPU and 50% memory both land in the nominal band; the meter track + fill render.
        await Assert.That(markup).Contains("bg-meter-track");
        await Assert.That(markup).Contains("bg-meter-nominal");
    }

    [Test]
    public async Task It_shows_the_awaiting_state_when_no_sample_is_cached()
    {
        using BunitContext ctx = new();
        ctx.Services.AddSingleton<IServerMetricsCache>(new ServerMetricsCache());
        ctx.Services.AddSingleton<IServerHealthCache>(new ServerHealthCache());

        var cut = ctx.Render<LiveServerPanel>(p => p
            .Add(c => c.ServerId, ServerId.New().ToString())
            .Add(c => c.AgentId, AgentId.New().ToString())
            .Add(c => c.InitialHealth, "Stopped"));

        string markup = cut.Markup;
        await Assert.That(markup).Contains("data-live-awaiting");
        // Falls back to the last-reported health until a live sample arrives.
        await Assert.That(markup).Contains("Stopped");
    }
}
