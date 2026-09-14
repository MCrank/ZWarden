namespace ZWarden.Infrastructure.Servers;

/// <summary>
/// The stable, machine-readable audit action names for Server registration, inventory and lifecycle (F14/F15;
/// F6, ADR 0019). Server-scoped, actor-attributed, and carrying no secret.
/// </summary>
public static class ServerAuditActions
{
    /// <summary>An operator imported a discovered container as a Server (adopted an existing container).</summary>
    public const string Imported = "Server.Imported";

    /// <summary>An operator registered a new Server (record created; provisioning dispatched — F14 PR-B).</summary>
    public const string Registered = "Server.Registered";

    /// <summary>An operator requested a Server start (a start Operation was enqueued — F15).</summary>
    public const string Started = "Server.Started";

    /// <summary>An operator requested a safe Server stop (a stop Operation was enqueued — F15).</summary>
    public const string Stopped = "Server.Stopped";

    /// <summary>An operator requested a safe Server restart (a restart Operation was enqueued — F15).</summary>
    public const string Restarted = "Server.Restarted";

    /// <summary>An operator requested a SteamCMD update/validate (an update Operation was enqueued — F17).</summary>
    public const string Updated = "Server.Updated";

    /// <summary>An operator ran an RCON health check (a diagnostics probe Operation was enqueued — F18).</summary>
    public const string RconChecked = "Server.RconChecked";

    /// <summary>A backup of a Server was requested (a backup Operation was enqueued — F24).</summary>
    public const string BackedUp = "Server.BackedUp";

    /// <summary>Deletion of a Server's backup was requested (a deletion Operation was enqueued — F24).</summary>
    public const string BackupDeleted = "Server.BackupDeleted";

    /// <summary>A restore of a Server from a backup was requested (a restore Operation was enqueued — F25).</summary>
    public const string Restored = "Server.Restored";
}
