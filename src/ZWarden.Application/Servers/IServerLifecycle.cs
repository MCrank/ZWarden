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

    /// <summary>Restarts the Server safely, authorized by the server-scoped <c>Server.Restart</c>. When
    /// <paramref name="plan"/> is supplied the restart is <b>graceful</b> (#114): the Agent broadcasts a
    /// <c>servermsg</c> countdown to connected players before the stop (an empty schedule restarts immediately). A
    /// <c>null</c> plan restarts with the Agent's default warning schedule.</summary>
    Task<ServerLifecycleResult> RestartAsync(
        UserId user, ServerId server, GracefulRestartPayload? plan = null, CancellationToken cancellationToken = default);

    /// <summary>Updates (install/validate) the Server's Project Zomboid install via anonymous SteamCMD (F17),
    /// authorized by the server-scoped <c>Server.Update</c>. Install/update/validate are one SteamCMD verb, so a
    /// repair is this same operation run again.</summary>
    Task<ServerLifecycleResult> UpdateAsync(UserId user, ServerId server, CancellationToken cancellationToken = default);

    /// <summary>Recreates the Server's container preserving its data (#229, ADR 0045), authorized by the server-scoped
    /// <c>Server.Recreate</c>: optionally on a new host <paramref name="gamePort"/> pair (<c>null</c> keeps the current
    /// pair), warning players with <paramref name="plan"/> before the safe stop if it is running. A port outside
    /// <c>HostPortRules</c> is <see cref="ServerLifecycleFailure.InvalidPort"/>; one overlapping another Server's pair on
    /// the same host is <see cref="ServerLifecycleFailure.PortInUse"/>. An optional <paramref name="heapSizeBytes"/> (#230)
    /// rebuilds it with a new heap (<c>null</c> keeps the current one); outside <c>ServerMemoryRules</c> it is
    /// <see cref="ServerLifecycleFailure.InvalidHeap"/>.</summary>
    Task<ServerLifecycleResult> RecreateAsync(
        UserId user,
        ServerId server,
        int? gamePort,
        GracefulRestartPayload? plan,
        long? heapSizeBytes = null,
        CancellationToken cancellationToken = default);
}
