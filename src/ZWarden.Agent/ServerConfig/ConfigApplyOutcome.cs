namespace ZWarden.Agent.ServerConfig;

/// <summary>
/// The result of an Agent-side configuration write (F20b PR-3). A write either applied — carrying the recorded
/// revision's canonical snapshot, its hash, and how many edits were applied — or it did not: refused closed
/// because the live file drifted from the recorded baseline (ADR 0011), or failed for an actionable reason (the
/// file is absent, did not parse, an edit targets no scalar, or a disk error). Every non-applied outcome carries
/// a non-secret, operator-facing <see cref="FailureReason"/>; none throws for an expected condition.
/// </summary>
public sealed record ConfigApplyOutcome
{
    private ConfigApplyOutcome(
        bool succeeded, bool drifted, string? failureReason, string? canonicalSnapshot, string? snapshotHash, int changedCount)
    {
        Succeeded = succeeded;
        Drifted = drifted;
        FailureReason = failureReason;
        CanonicalSnapshot = canonicalSnapshot;
        SnapshotHash = snapshotHash;
        ChangedCount = changedCount;
    }

    /// <summary><see langword="true"/> when the file was written and a revision snapshot produced.</summary>
    public bool Succeeded { get; }

    /// <summary><see langword="true"/> when the write was refused because the live file drifted from the recorded
    /// baseline (fail closed, ADR 0011). Implies <see cref="Succeeded"/> is <see langword="false"/>.</summary>
    public bool Drifted { get; }

    /// <summary>The non-secret, operator-facing reason a write did not apply, or <see langword="null"/> on
    /// success.</summary>
    public string? FailureReason { get; }

    /// <summary>The order-normalized snapshot of the file's values after the write (from
    /// <c>PzValueSnapshot</c>), or <see langword="null"/> when the write did not apply.</summary>
    public string? CanonicalSnapshot { get; }

    /// <summary>The SHA-256 hash of <see cref="CanonicalSnapshot"/> — the next write's drift baseline — or
    /// <see langword="null"/> when the write did not apply.</summary>
    public string? SnapshotHash { get; }

    /// <summary>How many edits were applied (0 unless <see cref="Succeeded"/>).</summary>
    public int ChangedCount { get; }

    /// <summary>A write that applied <paramref name="changedCount"/> edits and produced the given snapshot.</summary>
    public static ConfigApplyOutcome Applied(string canonicalSnapshot, string snapshotHash, int changedCount) =>
        new(succeeded: true, drifted: false, failureReason: null, canonicalSnapshot, snapshotHash, changedCount);

    /// <summary>A write refused closed because the live file drifted from the baseline (ADR 0011).</summary>
    public static ConfigApplyOutcome DriftRefused(string reason) =>
        new(succeeded: false, drifted: true, reason, canonicalSnapshot: null, snapshotHash: null, changedCount: 0);

    /// <summary>A write that failed for an actionable reason (absent file, parse error, bad edit, disk error).</summary>
    public static ConfigApplyOutcome Failed(string reason) =>
        new(succeeded: false, drifted: false, reason, canonicalSnapshot: null, snapshotHash: null, changedCount: 0);
}
