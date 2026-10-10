using ZWarden.Domain.Ids;

namespace ZWarden.Application.Operations;

/// <summary>
/// A mutating, server-scoped Operation was refused because the Server's host isn't connected (#383). Nothing was
/// enqueued and the per-server lock was not taken: an Operation left Pending for an offline Agent would hold the
/// lock until it was reaped, and running it whenever the host comes back would surprise the operator. Services map
/// it to their own "host offline" failure, next to <see cref="ServerBusyException"/>.
/// </summary>
public sealed class HostOfflineException : Exception
{
    /// <summary>The Server whose host is offline.</summary>
    public ServerId ServerId { get; }

    /// <summary>Creates the exception for <paramref name="serverId"/>.</summary>
    public HostOfflineException(ServerId serverId)
        : base($"The host of server {serverId} is not connected.")
    {
        ServerId = serverId;
    }
}
