namespace ZWarden.Infrastructure.Players;

/// <summary>
/// The stable, machine-readable audit action names for player management (F19; F6, ADR 0019). Server-scoped,
/// actor-attributed, carrying no secret. Each is written when the operator's action is authorized and the
/// (non-mutating) Operation is enqueued; the Operation's own <c>Operation.*</c> trail then records dispatch and
/// the terminal outcome. Enumeration (<c>players</c>) is a read, not administrative activity, so it is not audited.
/// </summary>
public static class PlayerAuditActions
{
    /// <summary>An operator kicked a player (a kick Operation was enqueued).</summary>
    public const string Kicked = "Player.Kicked";

    /// <summary>An operator banned a player (a ban Operation was enqueued; a ban registry record was written).</summary>
    public const string Banned = "Player.Banned";

    /// <summary>An operator lifted a ban (an unban Operation was enqueued; the registry record was lifted).</summary>
    public const string Unbanned = "Player.Unbanned";

    /// <summary>An operator removed a user from the whitelist (a removal Operation was enqueued).</summary>
    public const string RemovedFromWhitelist = "Player.RemovedFromWhitelist";

    /// <summary>An operator changed the whitelist mode (a set-whitelist-mode Operation was enqueued).</summary>
    public const string WhitelistModeChanged = "Player.WhitelistModeChanged";
}
