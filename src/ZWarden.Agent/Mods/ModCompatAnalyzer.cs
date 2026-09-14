using ZWarden.Contracts.Protocol.Messages;

namespace ZWarden.Agent.Mods;

/// <summary>
/// Reconciles what a Server has on disk against what its config references and enables, into the four F21
/// compatibility findings. Pure and static: it takes the installed items (with the mods each provides), the
/// <c>WorkshopItems=</c> ids, and the <c>Mods=</c> ids, and returns findings — no filesystem, no config parsing —
/// so the whole truth table is unit-tested directly. Ids are compared with the ordinal comparer: PZ Mod ids are
/// case-sensitive tokens and Workshop ids are exact numeric strings, so a case difference is a real mismatch.
/// </summary>
public static class ModCompatAnalyzer
{
    /// <summary>Computes the findings, grouped by kind in <see cref="ModCompatKind"/> order, each id reported once.</summary>
    public static IReadOnlyList<ModCompatFinding> Analyze(
        IReadOnlyList<DiscoveredWorkshopItem> installedItems,
        IReadOnlyList<string> configuredWorkshopIds,
        IReadOnlyList<string> enabledModIds)
    {
        ArgumentNullException.ThrowIfNull(installedItems);
        ArgumentNullException.ThrowIfNull(configuredWorkshopIds);
        ArgumentNullException.ThrowIfNull(enabledModIds);

        HashSet<string> installedWorkshopIds = new(installedItems.Select(i => i.WorkshopId), StringComparer.Ordinal);
        HashSet<string> enabled = new(enabledModIds, StringComparer.Ordinal);

        // How many installed items provide each mod id (dedup within an item so a repeated id in one item is not
        // mistaken for a conflict), and the distinct set of provided ids in first-seen order.
        Dictionary<string, int> providerCount = new(StringComparer.Ordinal);
        List<string> providedInOrder = [];
        foreach (DiscoveredWorkshopItem item in installedItems)
        {
            foreach (string modId in item.Mods.Select(m => m.ModId).Distinct(StringComparer.Ordinal))
            {
                if (providerCount.TryGetValue(modId, out int count))
                {
                    providerCount[modId] = count + 1;
                }
                else
                {
                    providerCount[modId] = 1;
                    providedInOrder.Add(modId);
                }
            }
        }

        HashSet<string> provided = new(providedInOrder, StringComparer.Ordinal);
        List<ModCompatFinding> findings = [];

        // 1. Referenced (WorkshopItems=) but not present under content/108600/.
        foreach (string workshopId in configuredWorkshopIds.Where(id => !installedWorkshopIds.Contains(id)))
        {
            findings.Add(new ModCompatFinding(
                ModCompatKind.ReferencedNotInstalled, workshopId,
                "referenced by WorkshopItems= but not present on disk"));
        }

        // 2. Enabled (Mods=) but no installed mod.info provides it.
        foreach (string modId in enabledModIds.Where(id => !provided.Contains(id)))
        {
            findings.Add(new ModCompatFinding(
                ModCompatKind.EnabledButMissing, modId, "no installed mod provides this id"));
        }

        // 3. Installed but not listed in Mods= (informational).
        foreach (string modId in providedInOrder.Where(id => !enabled.Contains(id)))
        {
            findings.Add(new ModCompatFinding(
                ModCompatKind.InstalledButInactive, modId, "installed but not enabled in Mods="));
        }

        // 4. The same mod id provided by more than one installed Workshop item.
        foreach (string modId in providedInOrder.Where(id => providerCount[id] > 1))
        {
            findings.Add(new ModCompatFinding(
                ModCompatKind.DuplicateModId, modId, "provided by multiple installed Workshop items"));
        }

        return findings;
    }
}
