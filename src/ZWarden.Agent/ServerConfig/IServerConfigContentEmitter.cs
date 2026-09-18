using ZWarden.Contracts.Protocol.Messages;

namespace ZWarden.Agent.ServerConfig;

/// <summary>
/// Sends a live configuration read reply upward as sequenced <see cref="ServerConfigContent"/> chunks (F20c, ADR
/// 0041), tagged with the request's correlation id. Built over the live hub connection (like the log emitter and the
/// operation-progress reporter), never a DI singleton, so the read handler never takes a connection dependency.
/// </summary>
public interface IServerConfigContentEmitter
{
    /// <summary>Chunks <paramref name="payload"/> and sends each chunk, each carrying <paramref name="correlationId"/>.
    /// A no-op when the connection is not currently connected.</summary>
    Task EmitAsync(string correlationId, ConfigReadPayload payload, CancellationToken cancellationToken);
}
