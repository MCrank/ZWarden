namespace ZWarden.PzConfig.Revisions;

/// <summary>The outcome of a drift check (ADR 0011).</summary>
public enum PzDriftStatus
{
    /// <summary>No recorded baseline to compare against (the first write to a file). The write proceeds.</summary>
    NoBaseline,

    /// <summary>The live file still matches the recorded baseline. The write proceeds.</summary>
    InSync,

    /// <summary>The live file no longer matches the recorded baseline — a second author changed it. The write
    /// is <b>refused</b> until the operator confirms (fail closed).</summary>
    Drifted,
}

/// <summary>
/// The result of comparing a live configuration file against a recorded revision's drift baseline.
/// </summary>
/// <param name="Status">Whether the file is in sync, has drifted, or has no baseline.</param>
/// <param name="BaselineHash">The recorded baseline hash, or <see langword="null"/> when there was none.</param>
/// <param name="CurrentHash">The hash freshly derived from the live file.</param>
public sealed record PzDriftResult(PzDriftStatus Status, string? BaselineHash, string CurrentHash)
{
    /// <summary>True when the live file has drifted from the baseline.</summary>
    public bool IsDrifted => Status == PzDriftStatus.Drifted;

    /// <summary>Whether a write may proceed: it may unless the file has <see cref="PzDriftStatus.Drifted"/>.
    /// This is the fail-closed gate — an ambiguous, second-authored state refuses the privileged write
    /// rather than guessing (ADR 0011; OWASP 2025 A10).</summary>
    public bool WriteAllowed => Status != PzDriftStatus.Drifted;
}

/// <summary>
/// Value-level drift detection (F20b, ADR 0011). ZWarden is not the only author of these files — the in-game
/// admin panel, the settings editor, and the server itself all write them — so before any write the live file
/// is re-parsed and its canonical value hash compared against the last recorded revision's baseline. Because
/// the comparison is over the <see cref="PzValueSnapshot"/> hash (parsed values, order-normalized), a key
/// reorder or a regenerated comment is not drift, while a genuine value change is. The check is pure; the
/// Agent performs the live re-parse and calls it before writing (PR 3).
/// </summary>
public static class PzDriftCheck
{
    /// <summary>Compares <paramref name="current"/> against <paramref name="baselineHash"/> by re-snapshotting
    /// the document. A <see langword="null"/> or blank baseline is <see cref="PzDriftStatus.NoBaseline"/>.</summary>
    public static PzDriftResult Compare(string? baselineHash, IPzConfigDocument current)
    {
        ArgumentNullException.ThrowIfNull(current);
        return Compare(baselineHash, PzValueSnapshot.Of(current));
    }

    /// <summary>Compares an already-computed <paramref name="current"/> snapshot against
    /// <paramref name="baselineHash"/>.</summary>
    public static PzDriftResult Compare(string? baselineHash, PzValueSnapshot current)
    {
        ArgumentNullException.ThrowIfNull(current);

        string currentHash = current.Hash;
        if (string.IsNullOrWhiteSpace(baselineHash))
        {
            return new PzDriftResult(PzDriftStatus.NoBaseline, null, currentHash);
        }

        PzDriftStatus status = string.Equals(baselineHash, currentHash, StringComparison.Ordinal)
            ? PzDriftStatus.InSync
            : PzDriftStatus.Drifted;
        return new PzDriftResult(status, baselineHash, currentHash);
    }
}
