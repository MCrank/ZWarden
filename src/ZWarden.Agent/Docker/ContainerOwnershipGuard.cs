using ZWarden.Agent.Identity;
using ZWarden.Domain.Ids;

namespace ZWarden.Agent.Docker;

/// <summary>
/// The allowed-container enforcement point (PRD 25, trust-boundaries.md §4): the single gate every
/// target-container operation passes through before a Docker verb is issued. A container is operable only when
/// it is a canonical ZWarden container <b>and</b> its <c>io.zwarden.agent-id</c> matches <i>this</i> Agent's
/// identity. Because no socket proxy can authorize by container (ADR 0008), this check — in the Agent's own
/// code — is the actual control; the proxy only downgrades a bug here from destruction to a 403. It is
/// deliberately fail-closed: an unrecognised, mislabelled, or foreign-owned container is refused.
/// </summary>
public sealed class ContainerOwnershipGuard
{
    private readonly IAgentIdentity _identity;

    /// <summary>Creates the guard bound to this Agent's identity.</summary>
    public ContainerOwnershipGuard(IAgentIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        _identity = identity;
    }

    /// <summary>
    /// Asserts that the container identified by <paramref name="containerId"/> with the given
    /// <paramref name="labels"/> is owned by this Agent, and returns its resolved <see cref="ServerId"/>.
    /// Throws <see cref="ForeignContainerException"/> otherwise — before any mutation.
    /// </summary>
    public ServerId EnsureOwnedByThisAgent(string containerId, IReadOnlyDictionary<string, string>? labels)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(containerId);

        if (!CanonicalContainerRecognizer.TryRecognize(labels, out ServerId serverId, out AgentId ownerId))
        {
            throw new ForeignContainerException(containerId, "it is not a canonical ZWarden container");
        }

        if (ownerId != _identity.AgentId)
        {
            throw new ForeignContainerException(containerId, "it is owned by a different Agent");
        }

        return serverId;
    }

    /// <summary>
    /// The non-throwing sibling of <see cref="EnsureOwnedByThisAgent"/>, for discovery: returns <c>true</c>
    /// and the resolved <see cref="ServerId"/> only when the container is canonical <b>and</b> owned by this
    /// Agent, so a host-wide container listing is scoped down to just this Agent's Servers.
    /// </summary>
    public bool TryResolveOwned(IReadOnlyDictionary<string, string>? labels, out ServerId serverId)
    {
        serverId = default;
        if (!CanonicalContainerRecognizer.TryRecognize(labels, out ServerId resolved, out AgentId ownerId)
            || ownerId != _identity.AgentId)
        {
            return false;
        }

        serverId = resolved;
        return true;
    }
}
