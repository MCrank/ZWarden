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
