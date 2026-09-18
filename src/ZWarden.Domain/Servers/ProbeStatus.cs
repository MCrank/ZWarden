namespace ZWarden.Domain.Servers;

/// <summary>
/// One probe's verdict within the breakdown behind a <see cref="ServerHealth"/> rollup (F16, #93) — observed,
/// never inferred (trust-boundaries.md §3). This is the Domain's own vocabulary: the wire
/// <c>ZWarden.Contracts.Protocol.ProbeStatus</c> cannot be referenced here (the Domain references no Contracts),
/// so ZWarden.Web maps wire → this, the probe-level sibling of the <see cref="ServerHealth"/> rollup map. Unlike
/// the rollup it is never persisted — it lives only in the transient live-health cache — but it is Domain
/// vocabulary all the same. Stored/serialized by name, so a later addition is additive.
/// </summary>
public enum ProbeStatus
{
    /// <summary>The probe passed.</summary>
    Pass = 0,

    /// <summary>The probe is non-fatally impaired — degrades health but does not fail it.</summary>
    Warn = 1,

    /// <summary>The probe failed fatally.</summary>
    Fail = 2,

    /// <summary>The probe did not apply in this state (e.g. network probing a stopped container).</summary>
    Skipped = 3,
}
