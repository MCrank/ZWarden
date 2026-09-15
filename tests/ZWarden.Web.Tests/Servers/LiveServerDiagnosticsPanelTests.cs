using Bunit;
using Microsoft.Extensions.DependencyInjection;
using ZWarden.Application.Diagnostics;
using ZWarden.Domain.Ids;
using ZWarden.Infrastructure.Diagnostics;
using ZWarden.Web.Components.Pages.Servers;

namespace ZWarden.Web.Tests.Servers;

/// <summary>
/// F29 PR-C: the interactive live per-server diagnostics island reads the ownership-guarded gather cache (no tenant
/// context) and renders each domain's check. With nothing cached it shows the empty state; a foreign Agent's bundle
/// is hidden by the ownership guard; and untrusted check detail is rendered as escaped data (trust-boundaries §8).
/// </summary>
public class LiveServerDiagnosticsPanelTests
{
    private static readonly DateTimeOffset At = new(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);

    private static BunitContext Context(DiagnosticsResultCache cache)
    {
        BunitContext ctx = new();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<IDiagnosticsResultCache>(cache);
        return ctx;
    }

    [Test]
    public async Task It_renders_the_checks_from_the_cache()
    {
        AgentId agent = AgentId.New();
        ServerId server = ServerId.New();
        DiagnosticsResultCache cache = new();
        cache.RecordServer(server, agent, new DiagnosticBundle(
            [new DiagnosticCheck(DiagnosticDomain.Rcon, DiagnosticStatus.Fail, "RCON is not usable.", "connection refused")], At));

        using BunitContext ctx = Context(cache);
        var cut = ctx.Render<LiveServerDiagnosticsPanel>(p => p
            .Add(c => c.ServerId, server.ToString())
            .Add(c => c.AgentId, agent.ToString()));

        string markup = cut.Markup;
        await Assert.That(markup).Contains("data-diagnostics-check");
        await Assert.That(markup).Contains("RCON is not usable.");
        await Assert.That(markup).Contains("FAIL");
    }

    [Test]
    public async Task It_shows_the_empty_state_when_nothing_is_cached()
    {
        using BunitContext ctx = Context(new DiagnosticsResultCache());
        var cut = ctx.Render<LiveServerDiagnosticsPanel>(p => p
            .Add(c => c.ServerId, ServerId.New().ToString())
            .Add(c => c.AgentId, AgentId.New().ToString()));

        await Assert.That(cut.Markup).Contains("data-diagnostics-empty");
    }

    [Test]
    public async Task A_foreign_agents_bundle_is_not_shown()
    {
        AgentId owner = AgentId.New();
        ServerId server = ServerId.New();
        DiagnosticsResultCache cache = new();
        cache.RecordServer(server, owner, new DiagnosticBundle(
            [new DiagnosticCheck(DiagnosticDomain.Config, DiagnosticStatus.Fail, "leak")], At));

        using BunitContext ctx = Context(cache);
        // The panel names a different Agent than the one that reported the bundle — the ownership guard hides it.
        var cut = ctx.Render<LiveServerDiagnosticsPanel>(p => p
            .Add(c => c.ServerId, server.ToString())
            .Add(c => c.AgentId, AgentId.New().ToString()));

        string markup = cut.Markup;
        await Assert.That(markup).Contains("data-diagnostics-empty");
        await Assert.That(markup).DoesNotContain("leak");
    }

    [Test]
    public async Task Hostile_detail_is_rendered_as_escaped_data()
    {
        AgentId agent = AgentId.New();
        ServerId server = ServerId.New();
        DiagnosticsResultCache cache = new();
        cache.RecordServer(server, agent, new DiagnosticBundle(
            [new DiagnosticCheck(DiagnosticDomain.Mod, DiagnosticStatus.Warn, "A mod is odd.", "<script>alert(1)</script>")], At));

        using BunitContext ctx = Context(cache);
        var cut = ctx.Render<LiveServerDiagnosticsPanel>(p => p
            .Add(c => c.ServerId, server.ToString())
            .Add(c => c.AgentId, agent.ToString()));

        string markup = cut.Markup;
        await Assert.That(markup).Contains("&lt;script&gt;");
        await Assert.That(markup).DoesNotContain("<script>alert(1)");
    }
}
