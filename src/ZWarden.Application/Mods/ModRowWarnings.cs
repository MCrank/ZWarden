namespace ZWarden.Application.Mods;

/// <summary>
/// The plain-English warnings shown under a Mods-table row (#292 D2), replacing the separate F21 issues list. Derived
/// from the inventory's <c>mod.info</c> metadata for each enabled part of the row: a required mod that isn't on (and
/// whether it is even downloaded), a declared conflict with another enabled mod, or an id more than one download
/// provides. Pure; ids are untrusted text, escaped at render. Ordinal comparison (PZ ids are case-sensitive).
/// </summary>
public static class ModRowWarnings
{
    /// <summary>The warnings per row key; rows without any are absent.</summary>
    /// <param name="table">The Mods table.</param>
    /// <param name="inventory">The last observed inventory, or <c>null</c> before the first discovery.</param>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> For(ModTableView table, ModInventory? inventory)
    {
        ArgumentNullException.ThrowIfNull(table);
        Dictionary<string, IReadOnlyList<string>> warnings = new(StringComparer.Ordinal);
        if (inventory is null)
        {
            return warnings;
        }

        HashSet<string> enabled = new(inventory.EnabledModIds, StringComparer.Ordinal);
        ILookup<string, InstalledMod> installed = inventory.InstalledItems
            .SelectMany(i => i.Mods)
            .ToLookup(m => m.ModId, StringComparer.Ordinal);
        Dictionary<string, int> providers = inventory.InstalledItems
            .SelectMany(i => i.Mods.Select(m => m.ModId).Distinct(StringComparer.Ordinal))
            .GroupBy(id => id, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

        foreach (ModTableRow row in table.Rows)
        {
            List<string> lines = [];
            foreach (string id in row.EnabledModIds)
            {
                InstalledMod? mod = installed[id].FirstOrDefault();
                foreach (string required in mod?.Requires.Where(r => !enabled.Contains(r)) ?? [])
                {
                    lines.Add(installed.Contains(required)
                        ? $"{id} needs {required}, which is downloaded but not turned on."
                        : $"{id} needs {required}, which isn't installed.");
                }

                foreach (string other in mod?.Incompatible.Where(enabled.Contains) ?? [])
                {
                    lines.Add($"{id} conflicts with {other}, which is also on.");
                }

                if (providers.GetValueOrDefault(id) > 1)
                {
                    lines.Add($"{id} is also provided by another download.");
                }
            }

            if (lines.Count > 0)
            {
                warnings[row.Key] = lines;
            }
        }

        return warnings;
    }
}
