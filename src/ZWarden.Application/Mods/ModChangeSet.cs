using ZWarden.Domain.Ids;
using ZWarden.Domain.Mods;

namespace ZWarden.Application.Mods;

/// <summary>Where a Workshop item or mod id stands relative to the server's last boot (#290).</summary>
public enum ModChangeStatus
{
    /// <summary>Configured and loaded at the last boot.</summary>
    Active,

    /// <summary>Configured since the last boot: it loads on the next restart.</summary>
    InstallsOnRestart,

    /// <summary>Loaded at the last boot but no longer configured: it unloads on the next restart.</summary>
    RemovedOnRestart,

    /// <summary>On disk, but neither configured nor loaded: unused files.</summary>
    Leftover,
}

/// <summary>One Workshop item on a Server, with its status and the details known about it. Steam text is untrusted
/// display data, escaped at render. <see cref="NeedsParts"/> (#291) is the after-boot check of the description's guess
/// against <c>mod.info</c>: a configured, downloaded item either enables an id its files don't provide
/// (<see cref="MissingModIds"/> — "the Workshop page listed X, the download provides Y") or enables none of its
/// mods (installed without ids). Surfaced only; config is never rewritten for it.</summary>
public sealed record ModItemView(
    string WorkshopId,
    ModChangeStatus Status,
    string? Title,
    string? PreviewUrl,
    long? SizeBytes,
    DateTimeOffset? SteamUpdatedAt,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> GuessedModIds,
    IReadOnlyList<string> ObservedModIds,
    bool OnDisk,
    bool NeedsParts = false,
    IReadOnlyList<string>? MissingModIds = null)
{
    /// <summary>Enabled ids guessed for this item that its downloaded files don't provide; empty when none.</summary>
    public IReadOnlyList<string> MissingModIds { get; init; } = MissingModIds ?? [];
}

/// <summary>One mod id in <c>Mods=</c> (now or at the last boot), with its status.</summary>
public sealed record ModIdView(string ModId, ModChangeStatus Status);

/// <summary>A Server's mods as the Mods page shows them (#290, for #292): items in config order, then removals, then
/// leftovers; mod ids in load order, then removals; and how many changes wait for a restart.</summary>
public sealed record ServerModOverview(
    ServerId ServerId,
    bool HasBootSnapshot,
    DateTimeOffset? ConfigObservedAt,
    IReadOnlyList<ModItemView> Items,
    IReadOnlyList<ModIdView> Mods)
{
    /// <summary>How many items and mod ids install or are removed on the next restart.</summary>
    public int PendingChanges =>
        Items.Count(i => i.Status is ModChangeStatus.InstallsOnRestart or ModChangeStatus.RemovedOnRestart)
        + Mods.Count(m => m.Status is ModChangeStatus.InstallsOnRestart or ModChangeStatus.RemovedOnRestart);
}

/// <summary>
/// Derives each Workshop item's and mod id's <see cref="ModChangeStatus"/> from a Server's
/// <see cref="ServerModState"/> and its <see cref="ServerWorkshopItem"/>s (#290). Pure. Before the first boot snapshot,
/// everything configured counts as <see cref="ModChangeStatus.Active"/>, so a fresh install doesn't show every row as
/// pending. Ordinal comparison throughout (PZ ids are case-sensitive).
/// </summary>
public static class ModChangeSet
{
    /// <summary>Derives the overview.</summary>
    /// <param name="server">The Server.</param>
    /// <param name="state">Its mod lists, or <c>null</c> before the first discovery.</param>
    /// <param name="items">Its tracked Workshop items.</param>
    public static ServerModOverview Derive(ServerId server, ServerModState? state, IReadOnlyList<ServerWorkshopItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (state is null)
        {
            return new ServerModOverview(server, false, null, [], []);
        }

        bool snapshot = state.HasBootSnapshot;
        IReadOnlyList<string> bootedItems = snapshot ? state.BootedWorkshopIds : state.ConfiguredWorkshopIds;
        IReadOnlyList<string> bootedMods = snapshot ? state.BootedModIds : state.ConfiguredModIds;
        Dictionary<string, ServerWorkshopItem> byId = items
            .GroupBy(i => i.WorkshopId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        List<ModItemView> itemViews = [];
        HashSet<string> seen = new(StringComparer.Ordinal);
        HashSet<string> configured = new(state.ConfiguredWorkshopIds, StringComparer.Ordinal);
        HashSet<string> booted = new(bootedItems, StringComparer.Ordinal);

        HashSet<string> configuredMods = new(state.ConfiguredModIds, StringComparer.Ordinal);
        foreach (string id in state.ConfiguredWorkshopIds.Where(seen.Add))
        {
            ModItemView view = View(id, booted.Contains(id) ? ModChangeStatus.Active : ModChangeStatus.InstallsOnRestart, byId);
            itemViews.Add(CheckParts(view, configuredMods, items));
        }

        foreach (string id in bootedItems.Where(id => !configured.Contains(id) && seen.Add(id)))
        {
            itemViews.Add(View(id, ModChangeStatus.RemovedOnRestart, byId));
        }

        foreach (ServerWorkshopItem item in items.Where(i => i.OnDisk).OrderBy(i => i.WorkshopId, StringComparer.Ordinal))
        {
            if (seen.Add(item.WorkshopId))
            {
                itemViews.Add(View(item.WorkshopId, ModChangeStatus.Leftover, byId));
            }
        }

        return new ServerModOverview(
            server, snapshot, state.ConfigObservedAt, itemViews, Mods(state.ConfiguredModIds, bootedMods));
    }

    private static List<ModIdView> Mods(IReadOnlyList<string> configured, IReadOnlyList<string> booted)
    {
        HashSet<string> configuredSet = new(configured, StringComparer.Ordinal);
        HashSet<string> bootedSet = new(booted, StringComparer.Ordinal);
        HashSet<string> seen = new(StringComparer.Ordinal);
        List<ModIdView> views = [];

        foreach (string id in configured.Where(seen.Add))
        {
            views.Add(new ModIdView(id, bootedSet.Contains(id) ? ModChangeStatus.Active : ModChangeStatus.InstallsOnRestart));
        }

        foreach (string id in booted.Where(id => !configuredSet.Contains(id) && seen.Add(id)))
        {
            views.Add(new ModIdView(id, ModChangeStatus.RemovedOnRestart));
        }

        return views;
    }

    // #291: once a configured item's files are on disk, mod.info is the truth. Flag a guessed id that is enabled but
    // provided by neither this item nor any other, or an item none of whose mods are enabled (installed without ids).
    private static ModItemView CheckParts(
        ModItemView view, HashSet<string> configuredMods, IReadOnlyList<ServerWorkshopItem> items)
    {
        if (!view.OnDisk || view.ObservedModIds.Count == 0)
        {
            return view;
        }

        HashSet<string> provided = new(
            items.Where(i => i.OnDisk).SelectMany(i => i.ObservedModIds), StringComparer.Ordinal);
        List<string> missing =
        [
            .. view.GuessedModIds
                .Where(id => configuredMods.Contains(id) && !provided.Contains(id))
                .Distinct(StringComparer.Ordinal),
        ];
        bool noneEnabled = !view.ObservedModIds.Any(configuredMods.Contains);

        return view with { NeedsParts = missing.Count > 0 || noneEnabled, MissingModIds = missing };
    }

    private static ModItemView View(string workshopId, ModChangeStatus status, Dictionary<string, ServerWorkshopItem> byId) =>
        byId.TryGetValue(workshopId, out ServerWorkshopItem? item)
            ? new ModItemView(
                workshopId, status, item.Title, item.PreviewUrl, item.SizeBytes, item.SteamUpdatedAt, item.Tags,
                item.GuessedModIds, item.ObservedModIds, item.OnDisk)
            : new ModItemView(workshopId, status, null, null, null, null, [], [], [], OnDisk: false);
}
