namespace ZWarden.Infrastructure.Settings;

/// <summary>The audit actions of the operator-edited control-plane settings (#345, ADR 0048).</summary>
public static class SettingsAuditActions
{
    /// <summary>An operator renamed the instance, or cleared the name back to config; the detail is old → new.</summary>
    public const string InstanceNameChanged = "Settings.InstanceNameChanged";

    /// <summary>The Owner changed the operator session idle timeout (#346); the detail is old → new.</summary>
    public const string SessionTimeoutChanged = "Settings.SessionTimeoutChanged";

    /// <summary>An operator set or cleared the host Deploy server pre-selects (#347); the detail is old → new.</summary>
    public const string DefaultDeployHostChanged = "Settings.DefaultDeployHostChanged";
}
