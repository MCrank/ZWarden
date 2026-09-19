namespace ZWarden.Infrastructure.Workshop;

/// <summary>
/// The stable, machine-readable audit action names for the Workshop integration key (F6; ADR 0019/0044). The
/// audit currency is a named constant, never an ad-hoc per-call-site string. The key value is never recorded
/// under either of these — only the act and the acting user.
/// </summary>
public static class WorkshopAuditActions
{
    /// <summary>An operator set (or replaced) the tenant's Workshop search API key.</summary>
    public const string KeyConfigured = "Workshop.KeyConfigured";

    /// <summary>An operator cleared the tenant's Workshop search API key, returning to keyless mode.</summary>
    public const string KeyCleared = "Workshop.KeyCleared";
}
