using ZWarden.Domain.Ids;

namespace ZWarden.Application.Players;

/// <summary>
/// The operator-facing player-management service (F19): kick, ban, unban, remove-from-whitelist, and the
/// whitelist-mode toggle, each run as a durable, authorized, audited, <b>non-mutating</b> Operation on the
/// Server's Agent. It is <b>fail-closed</b> (ADR 0018): every action resolves the Server through the tenant
/// filter (a foreign or unknown Server is <see cref="PlayerManagementFailure.ServerNotFound"/>), authorizes the
/// matching server-scoped permission against that specific Server (<c>Player.Kick</c>/<c>Player.Ban</c>/
/// <c>Player.Unban</c>, and <c>Server.Configuration.Edit</c> for the whitelist-mode toggle), and validates the
/// operator's username/reason before an Operation is enqueued (F19 D-3). Being non-mutating, an action never
/// claims the per-server lock (ADR 0022). The Agent, not this service, builds and quotes the RCON command and
/// reports the outcome; the caller polls the returned Operation. A ban is also recorded in the advisory ban
/// registry, and an unban lifts the matching record (ADR 0027).
/// </summary>
public interface IPlayerManagement
{
    /// <summary>Enqueues a roster enumeration (PZ's <c>players</c>). Authorized by <c>Player.View</c>. The
    /// observed roster lands in the in-memory <see cref="IPlayerRosterCache"/> for the live UI; a read, so it is
    /// not audited.</summary>
    Task<PlayerManagementResult> ListPlayersAsync(UserId user, ServerId server, CancellationToken cancellationToken = default);

    /// <summary>Kicks a connected player by account username. Authorized by <c>Player.Kick</c>.</summary>
    Task<PlayerManagementResult> KickAsync(UserId user, ServerId server, string username, string? reason, CancellationToken cancellationToken = default);

    /// <summary>Bans a player by account username and records it in the ban registry. Authorized by
    /// <c>Player.Ban</c>.</summary>
    Task<PlayerManagementResult> BanAsync(UserId user, ServerId server, string username, string? reason, CancellationToken cancellationToken = default);

    /// <summary>Lifts a ban by account username and lifts the matching registry record. Authorized by
    /// <c>Player.Unban</c>.</summary>
    Task<PlayerManagementResult> UnbanAsync(UserId user, ServerId server, string username, CancellationToken cancellationToken = default);

    /// <summary>Removes a user from the Server's whitelist by account username (removal only — ADR 0012).
    /// Authorized by <c>Player.Ban</c> (persistent access denial).</summary>
    Task<PlayerManagementResult> RemoveFromWhitelistAsync(UserId user, ServerId server, string username, CancellationToken cancellationToken = default);

    /// <summary>Toggles the Server's whitelist mode (the <c>Open</c> option). Authorized by
    /// <c>Server.Configuration.Edit</c>.</summary>
    Task<PlayerManagementResult> SetWhitelistModeAsync(UserId user, ServerId server, bool open, CancellationToken cancellationToken = default);
}
