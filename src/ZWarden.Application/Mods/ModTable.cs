namespace ZWarden.Application.Mods;

/// <summary>Which way a Mods-table row moves in the load order (#292).</summary>
public enum ModMove
{
    /// <summary>Earlier in the load order.</summary>
    Up,

    /// <summary>Later in the load order.</summary>
    Down,
}

/// <summary>One row of the Mods table (#292 D1): a Workshop item, or — when <see cref="Item"/> is <c>null</c> — an
/// enabled mod id no tracked item provides ("other mod").</summary>
/// <param name="Key">Stable row key: <c>item:{workshopId}</c> or <c>mod:{modId}</c>.</param>
/// <param name="Item">The Workshop item, or <c>null</c> for an other-mod row.</param>
/// <param name="Mods">The row's mod ids: enabled ones in load order, then ones removed since the last boot.</param>
/// <param name="Status">The item's status, or the mod id's for an other-mod row.</param>
/// <param name="CanMoveUp">A row with an enabled id sits above it.</param>
/// <param name="CanMoveDown">A row with an enabled id sits below it.</param>
public sealed record ModTableRow(
    string Key,
    ModItemView? Item,
    IReadOnlyList<ModIdView> Mods,
    ModChangeStatus Status,
    bool CanMoveUp,
    bool CanMoveDown)
{
    /// <summary>The row's ids enabled in <c>Mods=</c> now, in load order.</summary>
    public IEnumerable<string> EnabledModIds =>
        Mods.Where(m => m.Status != ModChangeStatus.RemovedOnRestart).Select(m => m.ModId);

    /// <summary>Something about this row changes on the next restart: the item (or other mod) itself installs or is
    /// removed, or one of its parts is turned on or off.</summary>
    public bool HasPendingChange =>
        IsPending(Status) || Mods.Any(m => IsPending(m.Status));

    private static bool IsPending(ModChangeStatus status) =>
        status is ModChangeStatus.InstallsOnRestart or ModChangeStatus.RemovedOnRestart;
}

/// <summary>The Mods table (#292): its rows, and the leftover downloads shown apart in the footer.</summary>
/// <param name="Rows">Rows with an enabled id in load order, then items with nothing enabled in config order, then
/// removals.</param>
/// <param name="Leftovers">Items on disk that are neither configured nor booted with.</param>
public sealed record ModTableView(IReadOnlyList<ModTableRow> Rows, IReadOnlyList<ModItemView> Leftovers)
{
    /// <summary>The rows with a change waiting for a restart — what the pending-changes bar counts and lists.</summary>
    public IEnumerable<ModTableRow> PendingRows => Rows.Where(r => r.HasPendingChange);

    /// <summary>The rows a restart would update from the Workshop (#275), listed apart from <see cref="PendingRows"/>;
    /// a row that already has a pending change restarts anyway, so it's only counted there.</summary>
    public IEnumerable<ModTableRow> UpdateRows => Rows.Where(r => !r.HasPendingChange && r.Item is { UpdateReady: true });

    /// <summary>The whole new <c>Mods=</c> order that moves row <paramref name="key"/> one place
    /// <paramref name="direction"/>: its id block swaps with the neighbouring movable row's block, and every block
    /// closes up (a scattered item's parts end up adjacent). <c>null</c> when the row is unknown, has no enabled id, or
    /// is already at that end. The result is a permutation of the enabled ids, for <c>ReorderModsAsync</c>.</summary>
    public IReadOnlyList<string>? MoveOrder(string key, ModMove direction)
    {
        List<ModTableRow> movable = [.. Rows.Where(r => r.EnabledModIds.Any())];
        int index = movable.FindIndex(r => string.Equals(r.Key, key, StringComparison.Ordinal));
        int target = direction == ModMove.Up ? index - 1 : index + 1;
        if (index < 0 || target < 0 || target >= movable.Count)
        {
            return null;
        }

        (movable[index], movable[target]) = (movable[target], movable[index]);
        return [.. movable.SelectMany(r => r.EnabledModIds)];
    }
}

/// <summary>
/// Builds the Mods table from a <see cref="ServerModOverview"/> (#292 D1). Pure. A row is a Workshop item that is
/// configured or removed since the last boot; it owns the mod ids its files provide (<c>mod.info</c>), or — for ids no
/// file provides yet — the ids its description listed. Files win over another item's guess; between equals, the first
/// item in config order wins. Each enabled id belongs to at most one row, and an id no item claims is its own
/// other-mod row, so the rows' enabled ids are exactly <c>Mods=</c>. Ordinal comparison (PZ ids are case-sensitive).
/// </summary>
public static class ModTable
{
    /// <summary>Builds the table.</summary>
    public static ModTableView Build(ServerModOverview overview)
    {
        ArgumentNullException.ThrowIfNull(overview);

        List<ModItemView> items =
            [.. overview.Items.Where(i => i.Status is not ModChangeStatus.Leftover)];
        Dictionary<string, string> owner = Owners(items);

        // Per row: its ids in overview order (enabled in load order, then removals) and its first enabled position.
        Dictionary<string, List<ModIdView>> idsByRow = new(StringComparer.Ordinal);
        Dictionary<string, int> firstEnabled = new(StringComparer.Ordinal);
        List<(string Key, ModIdView Id)> others = [];
        for (int i = 0; i < overview.Mods.Count; i++)
        {
            ModIdView mod = overview.Mods[i];
            string key = owner.TryGetValue(mod.ModId, out string? workshopId) ? ItemKey(workshopId) : ModKey(mod.ModId);
            if (!idsByRow.TryGetValue(key, out List<ModIdView>? ids))
            {
                idsByRow[key] = ids = [];
                if (workshopId is null)
                {
                    others.Add((key, mod));
                }
            }

            ids.Add(mod);
            if (mod.Status != ModChangeStatus.RemovedOnRestart)
            {
                firstEnabled.TryAdd(key, i);
            }
        }

        // Ordering groups: 0 = has an enabled id (by load position); 1 = configured item with nothing enabled; 2 = removed.
        List<(int Group, int Order, string Key, ModItemView? Item, ModChangeStatus Status)> ordered = [];
        for (int i = 0; i < items.Count; i++)
        {
            ModItemView item = items[i];
            string key = ItemKey(item.WorkshopId);
            ordered.Add(Place(key, item, item.Status, i));
        }

        foreach ((string key, ModIdView mod) in others)
        {
            ordered.Add(Place(key, null, mod.Status, items.Count));
        }

        List<(string Key, ModItemView? Item, ModChangeStatus Status)> sorted =
        [
            .. ordered
                .OrderBy(r => r.Group)
                .ThenBy(r => r.Order)
                .Select(r => (r.Key, r.Item, r.Status)),
        ];

        int movableCount = sorted.Count(r => firstEnabled.ContainsKey(r.Key));
        int seenMovable = 0;
        List<ModTableRow> rows = [];
        foreach ((string key, ModItemView? item, ModChangeStatus status) in sorted)
        {
            bool movable = firstEnabled.ContainsKey(key);
            rows.Add(new ModTableRow(
                key,
                item,
                idsByRow.TryGetValue(key, out List<ModIdView>? ids) ? ids : [],
                status,
                CanMoveUp: movable && seenMovable > 0,
                CanMoveDown: movable && seenMovable < movableCount - 1));
            if (movable)
            {
                seenMovable++;
            }
        }

        return new ModTableView(rows, [.. overview.Items.Where(i => i.Status is ModChangeStatus.Leftover)]);

        (int, int, string, ModItemView?, ModChangeStatus) Place(
            string key, ModItemView? item, ModChangeStatus status, int configOrder) =>
            firstEnabled.TryGetValue(key, out int position)
                ? (0, position, key, item, status)
                : status == ModChangeStatus.RemovedOnRestart
                    ? (2, configOrder, key, item, status)
                    : (1, configOrder, key, item, status);
    }

    // Which item owns each mod id: files on disk first, then description guesses; first item in config order wins.
    private static Dictionary<string, string> Owners(IReadOnlyList<ModItemView> items)
    {
        Dictionary<string, string> owner = new(StringComparer.Ordinal);
        foreach (ModItemView item in items)
        {
            foreach (string id in item.ObservedModIds)
            {
                owner.TryAdd(id, item.WorkshopId);
            }
        }

        foreach (ModItemView item in items)
        {
            foreach (string id in item.GuessedModIds)
            {
                owner.TryAdd(id, item.WorkshopId);
            }
        }

        return owner;
    }

    private static string ItemKey(string workshopId) => $"item:{workshopId}";

    private static string ModKey(string modId) => $"mod:{modId}";
}
