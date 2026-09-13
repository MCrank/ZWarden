using ZWarden.Domain.Ids;

namespace ZWarden.Application.Servers;

/// <summary>
/// The operator-facing Server diagnostics service (F18): run an on-demand RCON health check as a durable,
/// authorized, audited Operation. It is <b>fail-closed</b> (ADR 0018) — it authorizes the server-scoped
/// <c>Server.Diagnostics</c> permission against the specific Server and resolves the Server through the tenant
/// filter (so a foreign or unknown Server is <see cref="ServerLifecycleFailure.ServerNotFound"/>), records an
/// audit event, and enqueues a <b>non-mutating, server-scoped</b> Operation on the Server's Agent. Being
/// non-mutating, the probe never claims the per-server lock (ADR 0022), so it never contends with an in-flight
/// lifecycle Operation. The Agent, not this service, opens the RCON connection and reports the result.
/// </summary>
public interface IServerDiagnostics
{
    /// <summary>Enqueues an RCON health check for the Server, authorized by the server-scoped
    /// <c>Server.Diagnostics</c>. The caller polls the returned Operation for the reachable/authenticated result.</summary>
    Task<ServerLifecycleResult> CheckRconHealthAsync(UserId user, ServerId server, CancellationToken cancellationToken = default);
}
