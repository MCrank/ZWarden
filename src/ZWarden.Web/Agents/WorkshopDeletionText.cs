using ZWarden.Contracts.Protocol.Messages;

namespace ZWarden.Web.Agents;

/// <summary>
/// The operator-facing line for a completed Workshop delete (#293): how many unused downloads went, and each id the
/// Agent kept and why. An already-absent folder counts as deleted — it's gone either way.
/// </summary>
public static class WorkshopDeletionText
{
    /// <summary>The result line for a delete.</summary>
    public static string Describe(WorkshopContentDeletionResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        int deleted = result.Items.Count(i => i.Outcome is WorkshopContentDeletionOutcome.Deleted
            or WorkshopContentDeletionOutcome.AlreadyAbsent);
        string line = deleted == 0
            ? "Deleted nothing."
            : $"Deleted {deleted} unused download{(deleted == 1 ? "" : "s")}.";

        IEnumerable<string> kept = result.Items
            .Where(i => i.Outcome is WorkshopContentDeletionOutcome.RefusedReferenced or WorkshopContentDeletionOutcome.RefusedUnsafe)
            .Select(i => i.Outcome is WorkshopContentDeletionOutcome.RefusedReferenced
                ? $"Kept {i.WorkshopId} (still in WorkshopItems=)."
                : $"Kept {i.WorkshopId} (its folder is a link).");
        return string.Join(' ', [line, .. kept]);
    }
}
