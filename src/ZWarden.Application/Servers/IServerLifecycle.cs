using ZWarden.Domain.Ids;

namespace ZWarden.Application.Servers;

/// <summary>
/// The operator-facing Server lifecycle service (F15): start, stop and restart a registered Server as durable,
/// authorized, audited Operations. Each verb authorizes its own <b>server-scoped</b> permission
/// (<c>Server.Start</c>/<c>Server.Stop</c>/<c>Server.Restart</c>) against the specific Server (ADR 0018,
/// fail-closed), resolves the Server through the tenant filter (so a foreign Server is
/// <see cref="ServerLifecycleFailure.ServerNotFound"/>), records an audit event, and enqueues a
/// <b>mutating, server-scoped</b> Operation on the Server's Agent (per-server lock, ADR 0022). A conflicting
/// in-flight Operation surfaces as <see cref="ServerLifecycleFailure.ServerBusy"/>. It does not itself run any
/// Docker verb — that is the Agent, which re-authorizes ownership locally (F13, trust-boundaries §4).
/// </summary>
public interface IServerLifecycle
{
    /// <summary>Starts the Server, authorized by the server-scoped <c>Server.Start</c>.</summary>
    Task<ServerLifecycleResult> StartAsync(UserId user, ServerId server, CancellationToken cancellationToken = default);

    /// <summary>Stops the Server safely, authorized by the server-scoped <c>Server.Stop</c>.</summary>
    Task<ServerLifecycleResult> StopAsync(UserId user, ServerId server, CancellationToken cancellationToken = default);

    /// <summary>Restarts the Server safely, authorized by the server-scoped <c>Server.Restart</c>.</summary>
    Task<ServerLifecycleResult> RestartAsync(UserId user, ServerId server, CancellationToken cancellationToken = default);
}
