using ZWarden.Contracts.Protocol.Messages;
using ZWarden.Domain.Ids;

namespace ZWarden.Agent.LogStreaming;

/// <summary>
/// The sink a live-log subscription forwards its sanitized batches to (F27). It exists to break the cycle between
/// the subscription service (which the control-plane connection routes <c>Start</c>/<c>Stop</c> into) and the
/// connection itself: the connection hands the service a per-connection emitter over the live
/// <c>HubConnection</c> at subscribe time — exactly the <c>HubOperationProgressReporter</c> pattern — so the
/// service never takes a connection dependency and no DI cycle forms.
/// </summary>
public interface IServerLogEmitter
{
    /// <summary>Sends one <see cref="ServerLogBatch"/> upward for <paramref name="serverId"/>. Best-effort: a send
    /// while disconnected is dropped rather than thrown, since the stream is transient and re-subscribed on
    /// reconnect.</summary>
    Task EmitAsync(ServerId serverId, IReadOnlyList<ServerLogLine> lines, bool dropped, CancellationToken cancellationToken);
}
