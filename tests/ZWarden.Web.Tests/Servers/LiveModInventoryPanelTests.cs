using Bunit;
using Microsoft.Extensions.DependencyInjection;
using ZWarden.Application.Mods;
using ZWarden.Domain.Ids;
using ZWarden.Infrastructure.Mods;
using ZWarden.Web.Components.Pages.Servers;

namespace ZWarden.Web.Tests.Servers;

/// <summary>
/// F21: the interactive live mod-inventory island reads the ownership-guarded inventory cache (no tenant context)
/// and renders the installed Workshop items, the mods each provides, and the compatibility issues. With nothing
/// cached it shows the "awaiting" state. All ids/names are untrusted PZ/Workshop output, rendered as data — never
/// interpreted (trust-boundaries.md §8).
/// </summary>
public class LiveModInventoryPanelTests
{
    private static readonly DateTimeOffset At = new(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);

    private static ModInventory Inventory(ServerId server, AgentId agent) =>
        new(
            server,
            agent,
            InstalledItems:
            [
                new InstalledWorkshopItem("2392709985", [new InstalledMod("Brita_2", "Brita's Weapon Pack")]),
                new InstalledWorkshopItem("2335368829", [new InstalledMod("ModA", null)]),
            ],
            ConfiguredWorkshopIds: ["2392709985", "2335368829"],
            EnabledModIds: ["Brita_2"],
            Issues: [new ModCompatIssue(ModCompatIssueKind.InstalledButInactive, "ModA", "installed but not enabled in Mods=")],
            ObservedAt: At);

    [Test]
    public async Task It_renders_the_inventory_from_the_cache()
    {
        AgentId agent = AgentId.New();
        ServerId server = ServerId.New();
        ModInventoryCache cache = new();
        cache.Record(Inventory(server, agent));

        using BunitContext ctx = new();
        ctx.Services.AddSingleton<IModInventoryCache>(cache);

        var cut = ctx.Render<LiveModInventoryPanel>(p => p
            .Add(c => c.ServerId, server.ToString())
            .Add(c => c.AgentId, agent.ToString()));

        string markup = cut.Markup;
        await Assert.That(markup).Contains("2392709985");
        await Assert.That(markup).Contains("Brita_2");
        await Assert.That(markup).Contains("Brita's Weapon Pack");
        await Assert.That(markup).Contains("ModA");
    }

    [Test]
    public async Task It_renders_the_compatibility_issues()
    {
        AgentId agent = AgentId.New();
        ServerId server = ServerId.New();
        ModInventoryCache cache = new();
        cache.Record(Inventory(server, agent));

        using BunitContext ctx = new();
        ctx.Services.AddSingleton<IModInventoryCache>(cache);

        var cut = ctx.Render<LiveModInventoryPanel>(p => p
            .Add(c => c.ServerId, server.ToString())
            .Add(c => c.AgentId, agent.ToString()));

        string markup = cut.Markup;
        await Assert.That(markup).Contains("data-issue-kind=\"InstalledButInactive\"");
        await Assert.That(markup).Contains("Installed, inactive");
    }

    [Test]
    public async Task It_shows_the_awaiting_state_when_no_inventory_is_cached()
    {
        using BunitContext ctx = new();
        ctx.Services.AddSingleton<IModInventoryCache>(new ModInventoryCache());

        var cut = ctx.Render<LiveModInventoryPanel>(p => p
            .Add(c => c.ServerId, ServerId.New().ToString())
            .Add(c => c.AgentId, AgentId.New().ToString()));

        await Assert.That(cut.Markup).Contains("data-mods-awaiting");
    }

    [Test]
    public async Task A_hostile_mod_name_is_rendered_as_escaped_data()
    {
        AgentId agent = AgentId.New();
        ServerId server = ServerId.New();
        ModInventoryCache cache = new();
        cache.Record(new ModInventory(
            server, agent,
            [new InstalledWorkshopItem("111", [new InstalledMod("Evil", "<script>alert(1)</script>")])],
            ["111"], [], [], At));

        using BunitContext ctx = new();
        ctx.Services.AddSingleton<IModInventoryCache>(cache);

        var cut = ctx.Render<LiveModInventoryPanel>(p => p
            .Add(c => c.ServerId, server.ToString())
            .Add(c => c.AgentId, agent.ToString()));

        string markup = cut.Markup;
        await Assert.That(markup).Contains("&lt;script&gt;");
        await Assert.That(markup).DoesNotContain("<script>alert(1)");
    }
}
