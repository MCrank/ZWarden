using ZWarden.Application.Mods;
using ZWarden.Application.Servers;

namespace ZWarden.Web.Components.Pages.Servers.Sections;

/// <summary>
/// The last observed mod inventory for a Server and the sets the Mods / Mod Browser sections derive from it (#298).
/// The inventory is ownership-guarded by the Server's owning Agent; the mod manager re-reads it on submit, so a
/// stale snapshot here only affects which controls are offered.
/// </summary>
internal sealed record ModInventoryView(
    ModInventory? Inventory,
    IReadOnlyList<string> EnableCandidates,
    HashSet<string> InstalledWorkshopIds)
{
    public static ModInventoryView Load(IModInventoryCache cache, ServerSummary server)
    {
        ModInventory? inventory = cache.GetLatest(server.Id, server.AgentId);
        if (inventory is null)
        {
            return new ModInventoryView(null, [], []);
        }

        HashSet<string> installed = new(inventory.InstalledItems.Select(i => i.WorkshopId), StringComparer.Ordinal);
        HashSet<string> enabled = new(inventory.EnabledModIds, StringComparer.Ordinal);
        // Installed mods not already enabled — the candidates the operator can enable in one click.
        List<string> candidates = inventory.InstalledItems
            .SelectMany(i => i.Mods.Select(m => m.ModId))
            .Where(id => !enabled.Contains(id))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        return new ModInventoryView(inventory, candidates, installed);
    }
}
