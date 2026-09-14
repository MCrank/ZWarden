using ZWarden.Application.Configuration;

namespace ZWarden.Application.Mods;

/// <summary>The outcome of a <see cref="ModListEditor"/> recompute.</summary>
public enum ModListEditStatus
{
    /// <summary>The list changed; <see cref="ModListEditResult.Edits"/> carries the config edit(s) to apply.</summary>
    Changed,

    /// <summary>The intent was a no-op against the current list; nothing to apply.</summary>
    NoChange,

    /// <summary>A reorder whose requested order is not a permutation of the current set (it would silently
    /// enable/disable a mod); nothing is applied.</summary>
    InvalidReorder,
}

/// <summary>The recomputed config edits for a mod-list change: a status and, when
/// <see cref="ModListEditStatus.Changed"/>, the F20b <see cref="ConfigApplyEdit"/>s to enqueue (WorkshopItems= before
/// Mods= when both change).</summary>
/// <param name="Status">Whether the list changed, was unchanged, or the reorder was invalid.</param>
/// <param name="Edits">The value edits to apply; empty unless <see cref="Status"/> is
/// <see cref="ModListEditStatus.Changed"/>.</param>
public sealed record ModListEditResult(ModListEditStatus Status, IReadOnlyList<ConfigApplyEdit> Edits)
{
    /// <summary>The intent was a no-op.</summary>
    public static readonly ModListEditResult NoChange = new(ModListEditStatus.NoChange, []);

    /// <summary>The requested order was not a permutation of the current set.</summary>
    public static readonly ModListEditResult InvalidReorder = new(ModListEditStatus.InvalidReorder, []);

    /// <summary>A change carrying the given edits.</summary>
    public static ModListEditResult Changed(IReadOnlyList<ConfigApplyEdit> edits) =>
        new(ModListEditStatus.Changed, edits);
}

/// <summary>
/// Pure recompute of a Server's <c>WorkshopItems=</c> / <c>Mods=</c> config list values from the observed lists (F21)
/// plus an operator intent, emitted as F20b <see cref="ConfigApplyEdit"/>s. F22's realisation of config-as-truth: a
/// mod change is a list-value config edit the F20b Agent writer applies byte-preservingly, drift-checked, and records
/// as a Configuration Revision. Ordinal (case-sensitive) comparison, order-preserving, de-duped — a pure function of
/// (current list, intent), so it never touches disk or parses config.
/// </summary>
public static class ModListEditor
{
    private const string WorkshopItemsKey = "WorkshopItems";
    private const string ModsKey = "Mods";
    private const char Separator = ';';

    /// <summary>Adds each of <paramref name="modIdsToEnable"/> not already in <paramref name="enabledModIds"/> to the
    /// end of <c>Mods=</c>, in request order.</summary>
    public static ModListEditResult EnableMods(
        IReadOnlyList<string> enabledModIds,
        IReadOnlyList<string> modIdsToEnable)
    {
        ArgumentNullException.ThrowIfNull(enabledModIds);
        ArgumentNullException.ThrowIfNull(modIdsToEnable);

        List<string> next = [.. enabledModIds];
        HashSet<string> present = new(next, StringComparer.Ordinal);
        foreach (string id in modIdsToEnable)
        {
            if (present.Add(id))
            {
                next.Add(id);
            }
        }

        return DiffMods(enabledModIds, next);
    }

    /// <summary>Removes each of <paramref name="modIdsToDisable"/> from <c>Mods=</c>, preserving the order of the
    /// rest.</summary>
    public static ModListEditResult DisableMods(
        IReadOnlyList<string> enabledModIds,
        IReadOnlyList<string> modIdsToDisable)
    {
        ArgumentNullException.ThrowIfNull(enabledModIds);
        ArgumentNullException.ThrowIfNull(modIdsToDisable);

        HashSet<string> remove = new(modIdsToDisable, StringComparer.Ordinal);
        List<string> next = [.. enabledModIds.Where(id => !remove.Contains(id))];

        return DiffMods(enabledModIds, next);
    }

    /// <summary>Rewrites <c>Mods=</c> to <paramref name="desiredOrder"/>, which must be a permutation of
    /// <paramref name="enabledModIds"/> (order-only); otherwise <see cref="ModListEditResult.InvalidReorder"/>.</summary>
    public static ModListEditResult ReorderMods(
        IReadOnlyList<string> enabledModIds,
        IReadOnlyList<string> desiredOrder)
    {
        ArgumentNullException.ThrowIfNull(enabledModIds);
        ArgumentNullException.ThrowIfNull(desiredOrder);

        if (!IsPermutation(enabledModIds, desiredOrder))
        {
            return ModListEditResult.InvalidReorder;
        }

        return DiffMods(enabledModIds, desiredOrder);
    }

    /// <summary>Appends <paramref name="workshopIdToAdd"/> to <c>WorkshopItems=</c> if not already referenced.
    /// <c>Mods=</c> is left untouched — the provided Mod ids are unknown until the item downloads (two-step
    /// install).</summary>
    public static ModListEditResult AddWorkshopItem(
        IReadOnlyList<string> configuredWorkshopIds,
        string workshopIdToAdd)
    {
        ArgumentNullException.ThrowIfNull(configuredWorkshopIds);
        ArgumentException.ThrowIfNullOrWhiteSpace(workshopIdToAdd);

        if (configuredWorkshopIds.Contains(workshopIdToAdd, StringComparer.Ordinal))
        {
            return ModListEditResult.NoChange;
        }

        List<string> next = [.. configuredWorkshopIds, workshopIdToAdd];
        return ModListEditResult.Changed([Edit(WorkshopItemsKey, next)]);
    }

    /// <summary>Removes <paramref name="workshopIdsToRemove"/> from <c>WorkshopItems=</c>, and from <c>Mods=</c> the
    /// Mod ids those items <b>exclusively</b> provide (a mod still provided by a remaining installed item stays
    /// enabled; a referenced-but-not-installed item's mods are unknown and left alone).</summary>
    public static ModListEditResult RemoveWorkshopItems(
        IReadOnlyList<string> configuredWorkshopIds,
        IReadOnlyList<string> enabledModIds,
        IReadOnlyList<InstalledWorkshopItem> installedItems,
        IReadOnlyList<string> workshopIdsToRemove)
    {
        ArgumentNullException.ThrowIfNull(configuredWorkshopIds);
        ArgumentNullException.ThrowIfNull(enabledModIds);
        ArgumentNullException.ThrowIfNull(installedItems);
        ArgumentNullException.ThrowIfNull(workshopIdsToRemove);

        HashSet<string> remove = new(workshopIdsToRemove, StringComparer.Ordinal);
        List<string> nextWorkshop = [.. configuredWorkshopIds.Where(id => !remove.Contains(id))];

        // Mod ids provided by any item that survives the removal — these must stay enabled.
        HashSet<string> survivingMods = new(
            installedItems
                .Where(item => !remove.Contains(item.WorkshopId))
                .SelectMany(item => item.Mods.Select(m => m.ModId)),
            StringComparer.Ordinal);

        // Mod ids provided by a removed, installed item and by no surviving item — safe to disable.
        HashSet<string> orphanedMods = new(
            installedItems
                .Where(item => remove.Contains(item.WorkshopId))
                .SelectMany(item => item.Mods.Select(m => m.ModId))
                .Where(modId => !survivingMods.Contains(modId)),
            StringComparer.Ordinal);

        List<string> nextMods = [.. enabledModIds.Where(id => !orphanedMods.Contains(id))];

        List<ConfigApplyEdit> edits = [];
        if (!SequenceEqual(configuredWorkshopIds, nextWorkshop))
        {
            edits.Add(Edit(WorkshopItemsKey, nextWorkshop));
        }

        if (!SequenceEqual(enabledModIds, nextMods))
        {
            edits.Add(Edit(ModsKey, nextMods));
        }

        return edits.Count == 0 ? ModListEditResult.NoChange : ModListEditResult.Changed(edits);
    }

    private static ModListEditResult DiffMods(IReadOnlyList<string> current, IReadOnlyList<string> next) =>
        SequenceEqual(current, next)
            ? ModListEditResult.NoChange
            : ModListEditResult.Changed([Edit(ModsKey, next)]);

    private static ConfigApplyEdit Edit(string key, IReadOnlyList<string> values) =>
        new(key, ConfigEditKind.Text, string.Join(Separator, values));

    private static bool SequenceEqual(IReadOnlyList<string> a, IReadOnlyList<string> b) =>
        a.SequenceEqual(b, StringComparer.Ordinal);

    private static bool IsPermutation(IReadOnlyList<string> a, IReadOnlyList<string> b)
    {
        if (a.Count != b.Count)
        {
            return false;
        }

        Dictionary<string, int> counts = new(StringComparer.Ordinal);
        foreach (string id in a)
        {
            counts[id] = counts.TryGetValue(id, out int c) ? c + 1 : 1;
        }

        foreach (string id in b)
        {
            if (!counts.TryGetValue(id, out int c))
            {
                return false;
            }

            if (c == 1)
            {
                counts.Remove(id);
            }
            else
            {
                counts[id] = c - 1;
            }
        }

        return counts.Count == 0;
    }
}
