using ZWarden.Application.Mods;
using ZWarden.Domain.Ids;
using ZWarden.Infrastructure.Mods;

namespace ZWarden.Infrastructure.Tests.Mods;

/// <summary>
/// F21: the in-memory mod-inventory cache holds the newest inventory per Server and is ownership-guarded — an
/// inventory is returned only to a caller that names the Agent that reported it (trust-boundaries.md §8), and the
/// last write per Server wins.
/// </summary>
public class ModInventoryCacheTests
{
    private static readonly DateTimeOffset At = new(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);

    private static ModInventory Inventory(ServerId server, AgentId agent, DateTimeOffset at, params string[] workshopIds) =>
        new(server, agent,
            InstalledItems: [.. workshopIds.Select(id => new InstalledWorkshopItem(id, []))],
            ConfiguredWorkshopIds: workshopIds,
            EnabledModIds: [],
            Issues: [],
            ObservedAt: at);

    [Test]
    public async Task It_returns_the_latest_inventory_to_the_owning_agent()
    {
        AgentId agent = AgentId.New();
        ServerId server = ServerId.New();
        ModInventoryCache cache = new();
        cache.Record(Inventory(server, agent, At, "111"));
        cache.Record(Inventory(server, agent, At.AddSeconds(1), "111", "222"));

        ModInventory? latest = cache.GetLatest(server, agent);

        await Assert.That(latest).IsNotNull();
        await Assert.That(latest!.InstalledItems.Count).IsEqualTo(2);
    }

    [Test]
    public async Task It_withholds_an_inventory_from_a_non_owning_agent()
    {
        AgentId owner = AgentId.New();
        ServerId server = ServerId.New();
        ModInventoryCache cache = new();
        cache.Record(Inventory(server, owner, At, "111"));

        await Assert.That(cache.GetLatest(server, AgentId.New())).IsNull();
        await Assert.That(cache.GetLatest(ServerId.New(), owner)).IsNull();
    }
}
