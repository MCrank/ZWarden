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
}
