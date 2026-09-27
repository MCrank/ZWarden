using ZWarden.Domain.Ids;

namespace ZWarden.Application.Servers;

/// <summary>
/// Removes a deleted Server from the fleet (#271) once its Agent reports the delete Operation succeeded — the container
/// is gone from the host. The Operation's kind and target are read from the persisted Operation, never the wire, and
/// only the Agent that owns the Server can trigger it (trust-boundaries §3). The Server row and its server-scoped role
/// assignments are deleted (D1); Operations and audit entries stay as history. The discovery cache forgets it so it is
/// not offered for import.
/// </summary>
public interface IServerRemoval
{
    /// <summary>Removes the Server targeted by <paramref name="operationId"/> if it is a delete Operation and
    /// <paramref name="reportingAgent"/> owns the Server. Returns whether a Server was removed; anything else (another
    /// kind, a foreign Agent, an already-removed Server) is a no-op.</summary>
    Task<bool> RecordDeletedAsync(OperationId operationId, AgentId reportingAgent, CancellationToken cancellationToken = default);
}
