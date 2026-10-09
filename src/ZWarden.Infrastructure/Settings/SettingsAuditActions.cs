namespace ZWarden.Infrastructure.Settings;

/// <summary>The audit actions of the operator-edited control-plane settings (#345, ADR 0048).</summary>
public static class SettingsAuditActions
{
    /// <summary>An operator renamed the instance, or cleared the name back to config; the detail is old → new.</summary>
    public const string InstanceNameChanged = "Settings.InstanceNameChanged";
}
