using ZWarden.Domain.Ids;

namespace ZWarden.Agent.Docker;

/// <summary>
/// Thrown when a lifecycle verb targets a Server for which this Agent owns no canonical container (F15): the
/// Server is registered but not yet provisioned, or its container was removed out of band. Distinct from
/// <see cref="ForeignContainerException"/> (a container exists but belongs to someone else) — here there is
/// simply nothing owned to act on. The command processor turns it into an actionable failed
/// <c>OperationCompleted</c> ("provision the server first"), never a crash.
/// </summary>
public sealed class ContainerNotFoundException : Exception
{
    /// <summary>The Server that has no owned container on this host.</summary>
    public ServerId ServerId { get; }

    /// <summary>Creates the exception for <paramref name="serverId"/>.</summary>
    public ContainerNotFoundException(ServerId serverId)
        : base($"No canonical container owned by this Agent was found for server {serverId}.")
    {
        ServerId = serverId;
    }
}
