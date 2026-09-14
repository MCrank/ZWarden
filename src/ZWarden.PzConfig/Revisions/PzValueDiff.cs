using ZWarden.PzConfig.Model;

namespace ZWarden.PzConfig.Revisions;

/// <summary>
/// Compares two configuration documents as parsed values (ADR 0011). Named keys are matched by name,
/// so a reorder — which the server performs on every start — produces no change; positional (spawn)
/// entries are matched by index. Numbers compare by magnitude <em>and</em> integer-ness (so <c>1</c>
/// and <c>1.0</c> differ), strings by ordinal, booleans by value. The walk is iterative, never
/// recursing over tree depth (trust-boundaries §8); the model it walks is already depth-bounded.
/// </summary>
public static class PzValueDiff
{
    /// <summary>The value-level differences from <paramref name="before"/> to <paramref name="after"/>.</summary>
    public static IReadOnlyList<PzConfigChange> Compare(IPzConfigDocument before, IPzConfigDocument after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        return Compare(before.Root, after.Root);
    }

    /// <summary>
    /// The value-level differences between two canonical snapshots (F20b PR-4). Snapshots are already flat,
    /// path-keyed and order-normalized, so this is a direct per-path comparison — the form the revision-history
    /// diff needs when the control plane holds two persisted revisions and no live document.
    /// </summary>
    public static IReadOnlyList<PzConfigChange> Compare(PzValueSnapshot before, PzValueSnapshot after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        Dictionary<string, PzValue> beforeByPath = before.Scalars.ToDictionary(s => s.Path, s => s.Value, StringComparer.Ordinal);
        Dictionary<string, PzValue> afterByPath = after.Scalars.ToDictionary(s => s.Path, s => s.Value, StringComparer.Ordinal);

        var changes = new List<PzConfigChange>();
        foreach (string path in beforeByPath.Keys.Union(afterByPath.Keys, StringComparer.Ordinal))
        {
            bool hasBefore = beforeByPath.TryGetValue(path, out PzValue? b);
            bool hasAfter = afterByPath.TryGetValue(path, out PzValue? a);

            if (!hasBefore)
            {
                changes.Add(new PzConfigChange(path, PzConfigChangeKind.Added, null, a));
            }
            else if (!hasAfter)
            {
                changes.Add(new PzConfigChange(path, PzConfigChangeKind.Removed, b, null));
            }
            else if (!PzScalar.AreEqual(b!, a!))
            {
                changes.Add(new PzConfigChange(path, PzConfigChangeKind.Changed, b, a));
            }
        }

        changes.Sort(static (x, y) => string.CompareOrdinal(x.Path, y.Path));
        return changes;
    }

    /// <summary>The value-level differences between two value trees.</summary>
    public static IReadOnlyList<PzConfigChange> Compare(PzTable before, PzTable after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        var changes = new List<PzConfigChange>();
        var stack = new Stack<Frame>();
        stack.Push(new Frame(string.Empty, before, after));

        while (stack.Count > 0)
        {
            Frame frame = stack.Pop();
            CompareNamed(frame, changes, stack);
            ComparePositional(frame, changes, stack);
        }

        changes.Sort(static (x, y) => string.CompareOrdinal(x.Path, y.Path));
        return changes;
    }

    private static void CompareNamed(Frame frame, List<PzConfigChange> changes, Stack<Frame> stack)
    {
        Dictionary<string, PzValue> before = NamedMap(frame.Before);
        Dictionary<string, PzValue> after = NamedMap(frame.After);

        foreach (string key in before.Keys.Union(after.Keys, StringComparer.Ordinal))
        {
            string path = frame.Prefix.Length == 0 ? key : $"{frame.Prefix}.{key}";
            bool hasBefore = before.TryGetValue(key, out PzValue? b);
            bool hasAfter = after.TryGetValue(key, out PzValue? a);

            Emit(path, hasBefore ? b : null, hasAfter ? a : null, changes, stack);
        }
    }

    private static void ComparePositional(Frame frame, List<PzConfigChange> changes, Stack<Frame> stack)
    {
        List<PzTableEntry> before = [.. frame.Before.PositionalEntries];
        List<PzTableEntry> after = [.. frame.After.PositionalEntries];
        int count = Math.Max(before.Count, after.Count);

        for (int i = 0; i < count; i++)
        {
            string path = $"{frame.Prefix}[{i}]";
            PzValue? b = i < before.Count ? before[i].Value : null;
            PzValue? a = i < after.Count ? after[i].Value : null;
            Emit(path, b, a, changes, stack);
        }
    }

    // Records a difference for one path, or pushes a child frame when both sides are tables so the
    // comparison descends into them.
    private static void Emit(string path, PzValue? before, PzValue? after, List<PzConfigChange> changes, Stack<Frame> stack)
    {
        if (before is null)
        {
            changes.Add(new PzConfigChange(path, PzConfigChangeKind.Added, null, after));
            return;
        }

        if (after is null)
        {
            changes.Add(new PzConfigChange(path, PzConfigChangeKind.Removed, before, null));
            return;
        }

        if (before is PzTable beforeTable && after is PzTable afterTable)
        {
            stack.Push(new Frame(path, beforeTable, afterTable));
            return;
        }

        // One side is a table and the other a scalar, or two unequal scalars.
        if (before is PzTable || after is PzTable || !PzScalar.AreEqual(before, after))
        {
            changes.Add(new PzConfigChange(path, PzConfigChangeKind.Changed, before, after));
        }
    }

    // First occurrence wins, matching PzTable.TryGet ("PZ never repeats a key").
    private static Dictionary<string, PzValue> NamedMap(PzTable table)
    {
        var map = new Dictionary<string, PzValue>(StringComparer.Ordinal);
        foreach (PzTableEntry entry in table.NamedEntries)
        {
            map.TryAdd(entry.Key!.Name, entry.Value);
        }

        return map;
    }

    private readonly record struct Frame(string Prefix, PzTable Before, PzTable After);
}
