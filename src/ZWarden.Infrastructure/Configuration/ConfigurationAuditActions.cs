namespace ZWarden.Infrastructure.Configuration;

/// <summary>
/// The stable, machine-readable audit action names for configuration changes (F20b; F6, ADR 0019).
/// Server-scoped, actor-attributed, and carrying no secret — the values themselves live in the Configuration
/// Revision, not the audit detail.
/// </summary>
public static class ConfigurationAuditActions
{
    /// <summary>An operator requested a configuration apply (a mutating config Operation was enqueued — F20b).</summary>
    public const string Applied = "Server.ConfigurationApplied";
}
