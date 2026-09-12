using ZWarden.Domain.Ids;

namespace ZWarden.Application.Operations;

/// <summary>
/// Thrown when a mutating Operation cannot be enqueued because a conflicting mutating Operation is already
/// in flight against the same Server (PRD 21). This is the typed translation of the per-server lock
/// conflict — the partial unique index refusing a second active mutating row (ADR 0005/0022) — surfaced so
/// a caller sees "the server is busy", not a raw persistence error.
/// </summary>
public sealed class ServerBusyException : Exception
{
    /// <summary>The Server that already has a conflicting mutating Operation in flight.</summary>
    public ServerId ServerId { get; }

    /// <summary>Creates the exception for <paramref name="serverId"/>.</summary>
    public ServerBusyException(ServerId serverId)
        : base($"A conflicting mutating operation is already in flight against server {serverId}.")
    {
        ServerId = serverId;
    }
}
