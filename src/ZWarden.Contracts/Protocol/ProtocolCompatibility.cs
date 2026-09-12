namespace ZWarden.Contracts.Protocol;

/// <summary>Why a protocol negotiation was accepted or rejected (ADR 0020).</summary>
public enum ProtocolCompatibilityStatus
{
    /// <summary>The Agent's version is within the accepted range.</summary>
    Compatible = 0,

    /// <summary>The Agent is older than <see cref="ProtocolVersionRange.MinSupported"/>; it must update.</summary>
    BelowMinimum = 1,

    /// <summary>The Agent is newer than this build; ZWarden.Web must update.</summary>
    AboveCurrent = 2,
}

/// <summary>
/// The outcome of checking an Agent's protocol version against a
/// <see cref="ProtocolVersionRange"/>. Carries enough to log an actionable rejection — the version
/// seen, the range required, and which bound failed — so F10 never reports a bare "mismatch".
/// </summary>
/// <param name="Status">Whether the versions are compatible, and if not, which way.</param>
/// <param name="AgentVersion">The protocol version the Agent presented.</param>
/// <param name="Range">The range ZWarden.Web accepts.</param>
public sealed record ProtocolNegotiationResult(
    ProtocolCompatibilityStatus Status,
    int AgentVersion,
    ProtocolVersionRange Range)
{
    /// <summary>True when the connection may proceed.</summary>
    public bool IsCompatible => Status == ProtocolCompatibilityStatus.Compatible;

    /// <summary>An operator-facing reason for a rejection, or <c>null</c> when compatible.</summary>
    public string? RejectionReason => Status switch
    {
        ProtocolCompatibilityStatus.Compatible => null,
        ProtocolCompatibilityStatus.BelowMinimum =>
            $"Agent protocol {AgentVersion} is below the minimum supported {Range.MinSupported}; the Agent must be updated.",
        ProtocolCompatibilityStatus.AboveCurrent =>
            $"Agent protocol {AgentVersion} is newer than this build's {Range.Current}; ZWarden.Web must be updated.",
        _ => $"Agent protocol {AgentVersion} is not compatible with the accepted range [{Range.MinSupported}, {Range.Current}].",
    };
}

/// <summary>
/// The pure protocol-version compatibility rule (ADR 0020, decision 1). An Agent is compatible iff
/// its version falls within the Web-declared range, inclusive. No transport, no I/O — F10 calls
/// this at connect time and acts on the result.
/// </summary>
public static class ProtocolCompatibility
{
    /// <summary>True iff <paramref name="agentVersion"/> is within <paramref name="range"/>, inclusive.</summary>
    public static bool IsCompatible(int agentVersion, ProtocolVersionRange range)
        => agentVersion >= range.MinSupported && agentVersion <= range.Current;

    /// <summary>
    /// Classifies <paramref name="agentVersion"/> against <paramref name="range"/>, returning an
    /// actionable <see cref="ProtocolNegotiationResult"/>.
    /// </summary>
    public static ProtocolNegotiationResult Negotiate(int agentVersion, ProtocolVersionRange range)
    {
        ProtocolCompatibilityStatus status =
            agentVersion < range.MinSupported ? ProtocolCompatibilityStatus.BelowMinimum
            : agentVersion > range.Current ? ProtocolCompatibilityStatus.AboveCurrent
            : ProtocolCompatibilityStatus.Compatible;

        return new ProtocolNegotiationResult(status, agentVersion, range);
    }
}
