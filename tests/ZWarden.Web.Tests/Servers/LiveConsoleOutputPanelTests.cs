using Bunit;
using Microsoft.Extensions.DependencyInjection;
using ZWarden.Application.Console;
using ZWarden.Domain.Ids;
using ZWarden.Infrastructure.Console;
using ZWarden.Web.Components.Pages.Servers;

namespace ZWarden.Web.Tests.Servers;

/// <summary>
/// F28: the interactive live console-output island reads the ownership-guarded output cache (no tenant context)
/// and renders each command's reply. With nothing cached it shows the "no output yet" state. Replies are untrusted
/// PZ output and are rendered as data — never interpreted (trust-boundaries.md §8).
/// </summary>
public class LiveConsoleOutputPanelTests
{
    private static readonly DateTimeOffset At = new(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task It_renders_the_output_from_the_cache()
    {
        AgentId agent = AgentId.New();
        ServerId server = ServerId.New();
        ConsoleOutputCache cache = new();
        cache.Record(server, agent, OperationId.New(), "Players connected (0): ", truncated: false, At);

        using BunitContext ctx = new();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<IConsoleOutputCache>(cache);

        var cut = ctx.Render<LiveConsoleOutputPanel>(p => p
            .Add(c => c.ServerId, server.ToString())
            .Add(c => c.AgentId, agent.ToString()));

        string markup = cut.Markup;
        await Assert.That(markup).Contains("data-console-entry");
        await Assert.That(markup).Contains("Players connected (0): ");
    }

    [Test]
    public async Task It_shows_the_empty_state_when_no_output_is_cached()
    {
        using BunitContext ctx = new();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<IConsoleOutputCache>(new ConsoleOutputCache());

        var cut = ctx.Render<LiveConsoleOutputPanel>(p => p
            .Add(c => c.ServerId, ServerId.New().ToString())
            .Add(c => c.AgentId, AgentId.New().ToString()));

        await Assert.That(cut.Markup).Contains("data-console-empty");
    }

    [Test]
    public async Task A_foreign_agents_output_is_not_shown()
    {
        AgentId owner = AgentId.New();
        ServerId server = ServerId.New();
        ConsoleOutputCache cache = new();
        cache.Record(server, owner, OperationId.New(), "secret", truncated: false, At);

        using BunitContext ctx = new();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<IConsoleOutputCache>(cache);

        // The panel names a different Agent than the one that reported the output — the ownership guard hides it.
        var cut = ctx.Render<LiveConsoleOutputPanel>(p => p
            .Add(c => c.ServerId, server.ToString())
            .Add(c => c.AgentId, AgentId.New().ToString()));

        string markup = cut.Markup;
        await Assert.That(markup).Contains("data-console-empty");
        await Assert.That(markup).DoesNotContain("secret");
    }

    [Test]
    public async Task Hostile_output_is_rendered_as_escaped_data()
    {
        AgentId agent = AgentId.New();
        ServerId server = ServerId.New();
        ConsoleOutputCache cache = new();
        cache.Record(server, agent, OperationId.New(), "<script>alert(1)</script>", truncated: false, At);

        using BunitContext ctx = new();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<IConsoleOutputCache>(cache);

        var cut = ctx.Render<LiveConsoleOutputPanel>(p => p
            .Add(c => c.ServerId, server.ToString())
            .Add(c => c.AgentId, agent.ToString()));

        string markup = cut.Markup;
        await Assert.That(markup).Contains("&lt;script&gt;");
        await Assert.That(markup).DoesNotContain("<script>alert(1)");
    }

    private static IRenderedComponent<LiveConsoleOutputPanel> RenderPanel(
        BunitContext ctx, ConsoleOutputCache cache, ServerId server, AgentId agent,
        string? operationId = null, string? command = null, string? timeZoneId = null)
    {
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<IConsoleOutputCache>(cache);
        return ctx.Render<LiveConsoleOutputPanel>(p => p
            .Add(c => c.ServerId, server.ToString())
            .Add(c => c.AgentId, agent.ToString())
            .Add(c => c.OperationId, operationId)
            .Add(c => c.Command, command)
            .Add(c => c.TimeZoneId, timeZoneId));
    }

    [Test]
    public async Task Only_the_latest_reply_is_shown_not_every_earlier_one()
    {
        // #242: the pane used to append every cached reply, so the one just asked for was buried at the bottom.
        AgentId agent = AgentId.New();
        ServerId server = ServerId.New();
        ConsoleOutputCache cache = new();
        cache.Record(server, agent, OperationId.New(), "first reply", truncated: false, At);
        cache.Record(server, agent, OperationId.New(), "second reply", truncated: false, At);

        using BunitContext ctx = new();
        var cut = RenderPanel(ctx, cache, server, agent);

        await Assert.That(cut.Markup).Contains("second reply");
        await Assert.That(cut.Markup).DoesNotContain("first reply");
    }

    [Test]
    public async Task The_command_just_run_waits_for_its_own_reply_instead_of_showing_an_older_one()
    {
        AgentId agent = AgentId.New();
        ServerId server = ServerId.New();
        OperationId justRun = OperationId.New();
        ConsoleOutputCache cache = new();
        cache.Record(server, agent, OperationId.New(), "older reply", truncated: false, At);

        using BunitContext ctx = new();
        var cut = RenderPanel(ctx, cache, server, agent, justRun.ToString(), "showoptions");

        await Assert.That(cut.Markup).Contains("data-console-waiting");
        await Assert.That(cut.Markup).Contains("showoptions");
        await Assert.That(cut.Markup).DoesNotContain("older reply");

        // The reply arrives; the next poll swaps the waiting state for it.
        cache.Record(server, agent, justRun, "Options: MaxPlayers=16", truncated: false, At);
        cut.WaitForState(() => cut.Markup.Contains("Options: MaxPlayers=16"), TimeSpan.FromSeconds(5));
        await Assert.That(cut.Markup).DoesNotContain("data-console-waiting");
        await Assert.That(cut.Markup).Contains("data-console-command");
    }

    [Test]
    public async Task A_later_reply_replaces_the_one_shown()
    {
        AgentId agent = AgentId.New();
        ServerId server = ServerId.New();
        ConsoleOutputCache cache = new();
        cache.Record(server, agent, OperationId.New(), "first reply", truncated: false, At);

        using BunitContext ctx = new();
        var cut = RenderPanel(ctx, cache, server, agent);
        cache.Record(server, agent, OperationId.New(), "second reply", truncated: false, At);

        cut.WaitForState(() => cut.Markup.Contains("second reply"), TimeSpan.FromSeconds(5));
        await Assert.That(cut.Markup).DoesNotContain("first reply");
    }

    [Test]
    public async Task The_reply_time_uses_the_operators_time_zone()
    {
        AgentId agent = AgentId.New();
        ServerId server = ServerId.New();
        ConsoleOutputCache cache = new();
        cache.Record(server, agent, OperationId.New(), "reply", truncated: false, At);

        using BunitContext ctx = new();
        var cut = RenderPanel(ctx, cache, server, agent, timeZoneId: "America/New_York");

        TimeZoneInfo zone = ZWarden.Web.Time.OperatorTimeZone.Resolve("America/New_York");
        await Assert.That(cut.Markup).Contains(ZWarden.Web.Time.OperatorTimeZone.Format(At, zone, "HH:mm:ss"));
    }

    [Test]
    public async Task A_truncated_reply_is_flagged()
    {
        AgentId agent = AgentId.New();
        ServerId server = ServerId.New();
        ConsoleOutputCache cache = new();
        cache.Record(server, agent, OperationId.New(), "partial", truncated: true, At);

        using BunitContext ctx = new();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<IConsoleOutputCache>(cache);

        var cut = ctx.Render<LiveConsoleOutputPanel>(p => p
            .Add(c => c.ServerId, server.ToString())
            .Add(c => c.AgentId, agent.ToString()));

        await Assert.That(cut.Markup).Contains("data-console-truncated");
    }
}
