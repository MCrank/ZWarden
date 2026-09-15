using Bunit;
using Microsoft.Extensions.DependencyInjection;
using ZWarden.Application.Players;
using ZWarden.Domain.Ids;
using ZWarden.Infrastructure.Players;
using ZWarden.Web.Components.Pages.Servers;

namespace ZWarden.Web.Tests.Servers;

/// <summary>
/// F19: the interactive live roster island reads the ownership-guarded roster cache (no tenant context) and
/// renders the connected players. With nothing cached it shows the "awaiting" state. Usernames are untrusted PZ
/// output and are rendered as data — never interpreted (trust-boundaries.md §8).
/// </summary>
public class LivePlayerRosterPanelTests
{
    private static readonly DateTimeOffset At = new(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task It_renders_the_roster_from_the_cache()
    {
        AgentId agent = AgentId.New();
        ServerId server = ServerId.New();
        PlayerRosterCache cache = new();
        cache.Record(new PlayerRoster(server, agent, 2, ["Bob", "Alice"], At));

        using BunitContext ctx = new();
        // The roster now renders BlazorBlueprint BbItem controls, which call JSInterop in OnAfterRender (ui-components.md).
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<IPlayerRosterCache>(cache);

        var cut = ctx.Render<LivePlayerRosterPanel>(p => p
            .Add(c => c.ServerId, server.ToString())
            .Add(c => c.AgentId, agent.ToString()));

        string markup = cut.Markup;
        await Assert.That(markup).Contains("2 connected");
        await Assert.That(markup).Contains("Bob");
        await Assert.That(markup).Contains("Alice");
    }

    [Test]
    public async Task It_renders_the_roster_as_blueprint_item_lists()
    {
        AgentId agent = AgentId.New();
        ServerId server = ServerId.New();
        PlayerRosterCache cache = new();
        cache.Record(new PlayerRoster(server, agent, 1, ["Bob"], At));

        using BunitContext ctx = new();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<IPlayerRosterCache>(cache);

        var cut = ctx.Render<LivePlayerRosterPanel>(p => p
            .Add(c => c.ServerId, server.ToString())
            .Add(c => c.AgentId, agent.ToString()));

        string markup = cut.Markup;
        // BbItemGroup renders role="list"; each player keeps its data-roster-* hooks (data-roster-player on the BbItem).
        await Assert.That(markup).Contains("role=\"list\"");
        await Assert.That(markup).Contains("data-roster-list");
        await Assert.That(markup).Contains("data-roster-player");
    }

    [Test]
    public async Task It_shows_the_awaiting_state_when_no_roster_is_cached()
    {
        using BunitContext ctx = new();
        // The roster now renders BlazorBlueprint BbItem controls, which call JSInterop in OnAfterRender (ui-components.md).
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<IPlayerRosterCache>(new PlayerRosterCache());

        var cut = ctx.Render<LivePlayerRosterPanel>(p => p
            .Add(c => c.ServerId, ServerId.New().ToString())
            .Add(c => c.AgentId, AgentId.New().ToString()));

        await Assert.That(cut.Markup).Contains("data-roster-awaiting");
    }

    [Test]
    public async Task A_hostile_username_is_rendered_as_escaped_data()
    {
        AgentId agent = AgentId.New();
        ServerId server = ServerId.New();
        PlayerRosterCache cache = new();
        cache.Record(new PlayerRoster(server, agent, 1, ["<script>alert(1)</script>"], At));

        using BunitContext ctx = new();
        // The roster now renders BlazorBlueprint BbItem controls, which call JSInterop in OnAfterRender (ui-components.md).
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<IPlayerRosterCache>(cache);

        var cut = ctx.Render<LivePlayerRosterPanel>(p => p
            .Add(c => c.ServerId, server.ToString())
            .Add(c => c.AgentId, agent.ToString()));

        string markup = cut.Markup;
        // The username is present as escaped text, never as a live <script> element.
        await Assert.That(markup).Contains("&lt;script&gt;");
        await Assert.That(markup).DoesNotContain("<script>alert(1)");
    }
}
