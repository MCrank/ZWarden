using ZWarden.PzConfig.Model;

namespace ZWarden.PzConfig.Revisions;

/// <summary>
/// Plans and applies a value-level revision restore (ADR 0011). Restoring re-applies a target
/// snapshot's scalar values to the current file as surgical edits — never a byte restore, which the
/// ADR rejects because the server rewrites these files. A key that exists in only one side is a
/// structural obstacle the values-only writer reports rather than guesses at.
/// </summary>
public static class PzRestore
{
    /// <summary>Plans the restore of <paramref name="current"/> to the values in <paramref name="target"/>.</summary>
    public static PzRestorePlan PlanTo(PzValueSnapshot target, IPzConfigDocument current)
    {
        ArgumentNullException.ThrowIfNull(current);
        return PlanTo(target, PzValueSnapshot.Of(current));
    }

    /// <summary>Plans the restore from the current snapshot to the target snapshot.</summary>
    public static PzRestorePlan PlanTo(PzValueSnapshot target, PzValueSnapshot current)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(current);

        Dictionary<string, PzValue> currentByPath = ByPath(current);
        Dictionary<string, PzValue> targetByPath = ByPath(target);

        var edits = new List<PzScalarEntry>();
        var obstacles = new List<PzRestoreObstacle>();

        foreach (PzScalarEntry wanted in target.Scalars)
        {
            if (!currentByPath.TryGetValue(wanted.Path, out PzValue? have))
            {
                // The target has a scalar the file lacks (absent, or now a table): the writer adds no keys.
                obstacles.Add(new PzRestoreObstacle(wanted.Path, PzRestoreObstacleKind.AddNotSupported));
            }
            else if (!PzScalar.AreEqual(have, wanted.Value))
            {
                edits.Add(wanted);
            }
        }

        foreach (PzScalarEntry present in current.Scalars)
        {
            if (!targetByPath.ContainsKey(present.Path))
            {
                obstacles.Add(new PzRestoreObstacle(present.Path, PzRestoreObstacleKind.RemoveNotSupported));
            }
        }

        edits.Sort(static (x, y) => string.CompareOrdinal(x.Path, y.Path));
        obstacles.Sort(static (x, y) => string.CompareOrdinal(x.Path, y.Path));
        return new PzRestorePlan(edits, obstacles);
    }

    /// <summary>
    /// Applies a plan's value edits to a document in path order and returns each edit's result. A
    /// caller checks the results (and the plan's obstacles) before treating the restore as complete.
    /// </summary>
    public static IReadOnlyList<PzConfigEditResult> Apply(IPzConfigDocument document, PzRestorePlan plan)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(plan);

        var results = new List<PzConfigEditResult>(plan.Edits.Count);
        foreach (PzScalarEntry edit in plan.Edits)
        {
            results.Add(document.TrySetValue(edit.Path, edit.Value));
        }

        return results;
    }

    private static Dictionary<string, PzValue> ByPath(PzValueSnapshot snapshot)
    {
        var map = new Dictionary<string, PzValue>(StringComparer.Ordinal);
        foreach (PzScalarEntry entry in snapshot.Scalars)
        {
            map[entry.Path] = entry.Value;
        }

        return map;
    }
}
