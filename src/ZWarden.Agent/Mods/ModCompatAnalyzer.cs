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

        // The mod.info metadata behind the last two findings belongs to a mod only when it is actually loading, so
        // resolve the first-provider metadata for each enabled mod id (#110). require=/incompatible= on an inactive
        // mod raise no noise.
        Dictionary<string, DiscoveredMod> metadataByModId = new(StringComparer.Ordinal);
        foreach (DiscoveredMod mod in installedItems.SelectMany(i => i.Mods))
        {
            metadataByModId.TryAdd(mod.ModId, mod);
        }

        // 5. An enabled mod's require= names an id no installed item provides (a missing dependency). Reported once
        //    per distinct missing id, in encounter order.
        HashSet<string> requiresSeen = new(StringComparer.Ordinal);
        foreach (string modId in enabledModIds)
        {
            if (!metadataByModId.TryGetValue(modId, out DiscoveredMod? mod))
            {
                continue;
            }

            foreach (string required in mod.Requires.Where(r => !provided.Contains(r) && requiresSeen.Add(r)))
            {
                findings.Add(new ModCompatFinding(
                    ModCompatKind.RequiresMissing, required, $"required by {modId} but no installed mod provides it"));
            }
        }

        // 6. An enabled mod's incompatible= names another enabled mod (both are loading — a real conflict). The
        //    subject is the present incompatible id; PZ's leading '\' and trailing '+'/'-' markers are normalized
        //    away for the comparison only (research §6).
        HashSet<string> incompatibleSeen = new(StringComparer.Ordinal);
        foreach (string modId in enabledModIds)
        {
            if (!metadataByModId.TryGetValue(modId, out DiscoveredMod? mod))
            {
                continue;
            }

            foreach (string raw in mod.Incompatible)
            {
                string other = NormalizeIncompatible(raw);
                if (enabled.Contains(other) && incompatibleSeen.Add(other))
                {
                    findings.Add(new ModCompatFinding(
                        ModCompatKind.IncompatiblePresent, other, $"declared incompatible by {modId} and also enabled"));
                }
            }
        }

        return findings;
    }

    // PZ writes an incompatible id with a leading '\' and an optional trailing '+'/'-' (research §6); strip them so
    // the value compares against the bare Mod id. Everything else is preserved verbatim.
    private static string NormalizeIncompatible(string value)
    {
        ReadOnlySpan<char> span = value.AsSpan().Trim();
        if (span.StartsWith("\\"))
        {
            span = span[1..];
        }

        if (span.EndsWith("+") || span.EndsWith("-"))
        {
            span = span[..^1];
        }

        return span.ToString();
    }
}
