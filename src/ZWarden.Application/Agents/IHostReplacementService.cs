using ZWarden.Domain.Ids;

namespace ZWarden.Application.Agents;

/// <summary>
/// #368 (ADR 0049): replaces a wiped Host with the Host its machine enrolled as next — the one path across the
/// Agent's container ownership guard. The Owner confirms it; it is never automatic.
/// </summary>
public interface IHostReplacementService
{
    /// <summary>
    /// Replaces <paramref name="predecessor"/> with <paramref name="successor"/>. Requires
    /// <c>Tenant.Enrollment.Manage</c> (else <c>AuthorizationDeniedException</c>, as is a Host outside the tenant),
    /// the predecessor offline, and the successor's latest snapshot reporting containers stamped with the predecessor's
    /// id. On success the predecessor's Servers and backups move to the successor, the predecessor is revoked and
    /// removed, and the successor inherits its id.
    /// </summary>
    Task<HostReplacementResult> ReplaceAsync(
        UserId actor, AgentId successor, AgentId predecessor, CancellationToken cancellationToken = default);

    /// <summary>The Agent ids <paramref name="successor"/> inherited, for its Agent on connect. Tenant-scoped.</summary>
    Task<IReadOnlyList<AgentId>> InheritedIdsAsync(AgentId successor, CancellationToken cancellationToken = default);
}

/// <summary>How a Replace host ended.</summary>
public enum HostReplacementOutcome
{
    /// <summary>The Host was replaced.</summary>
    Replaced,

    /// <summary>Refused: the old Host is connected, so it may still be managing its containers.</summary>
    PredecessorOnline,

    /// <summary>Refused: the new Host has not reported containers stamped with the old Host's id.</summary>
    NotReported,

    /// <summary>Refused: a Host can't replace itself.</summary>
    SameHost,

    /// <summary>Refused: the new Host is disabled or revoked.</summary>
    SuccessorUntrusted,
}

/// <summary>The outcome of a Replace host, with what moved when it succeeded.</summary>
/// <param name="Outcome">How it ended.</param>
/// <param name="ServerCount">Servers moved; 0 when refused.</param>
/// <param name="BackupCount">Backups moved; 0 when refused.</param>
public sealed record HostReplacementResult(HostReplacementOutcome Outcome, int ServerCount = 0, int BackupCount = 0)
{
    /// <summary>A refusal with nothing changed.</summary>
    public static HostReplacementResult Refused(HostReplacementOutcome outcome) => new(outcome);
}
