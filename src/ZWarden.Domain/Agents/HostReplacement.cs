using ZWarden.Domain.Ids;
using ZWarden.Domain.Tenancy;

namespace ZWarden.Domain.Agents;

/// <summary>
/// A <b>host replacement</b> (<c>hr-</c>, #368, ADR 0049): the Owner replaced a Host whose machine was wiped (the
/// predecessor) with the Host that machine enrolled as next (the successor). The predecessor's Servers and backups
/// moved to the successor and the predecessor was removed; its PZ containers still carry its id, so the successor's
/// Agent owns that id from now on. ZWarden holds the list and sends it to the Agent on every connect; the Agent never
/// stores it.
/// <para>
/// When the successor is replaced in turn, each record against it passes to the new successor (<see cref="PassTo"/>),
/// so the whole chain of ids follows the machine. It is <see cref="ITenantOwned"/> (ADR 0016).
/// </para>
/// </summary>
public sealed class HostReplacement : ITenantOwned
{
    /// <summary>EF / factory use.</summary>
    public HostReplacement()
    {
    }

    /// <summary>The replacement identifier (<c>hr-&lt;uuid&gt;</c>).</summary>
    public HostReplacementId Id { get; init; } = HostReplacementId.New();

    /// <inheritdoc />
    public TenantId TenantId { get; init; }

    /// <summary>The Host that now owns <see cref="PredecessorId"/>'s containers.</summary>
    public AgentId SuccessorId { get; private set; }

    /// <summary>The removed Host whose id the successor's Agent owns.</summary>
    public AgentId PredecessorId { get; init; }

    /// <summary>The Owner who confirmed the replacement.</summary>
    public UserId ReplacedBy { get; init; }

    /// <summary>When the replacement was made.</summary>
    public DateTimeOffset ReplacedAt { get; init; }

    /// <summary>Records that <paramref name="successor"/> replaced <paramref name="predecessor"/>. The tenant is left
    /// unset for the ownership interceptor to stamp (ADR 0016).</summary>
    public static HostReplacement Record(AgentId successor, AgentId predecessor, UserId replacedBy, DateTimeOffset now)
    {
        if (successor.IsEmpty || predecessor.IsEmpty)
        {
            throw new ArgumentException("Both hosts are required.", nameof(successor));
        }

        if (successor == predecessor)
        {
            throw new ArgumentException("A host cannot replace itself.", nameof(predecessor));
        }

        return new HostReplacement
        {
            Id = HostReplacementId.New(),
            SuccessorId = successor,
            PredecessorId = predecessor,
            ReplacedBy = replacedBy,
            ReplacedAt = now,
        };
    }

    /// <summary>The successor was itself replaced: <paramref name="nextSuccessor"/> now owns the predecessor's id.</summary>
    public void PassTo(AgentId nextSuccessor)
    {
        if (nextSuccessor.IsEmpty || nextSuccessor == PredecessorId)
        {
            throw new ArgumentException("The next successor must be another host.", nameof(nextSuccessor));
        }

        SuccessorId = nextSuccessor;
    }
}
