namespace ZWarden.Infrastructure.Backups;

/// <summary>Backup policy the control plane enforces (#379), bound from <c>ZWarden:Backups</c>.</summary>
public sealed class BackupOptions
{
    /// <summary>The configuration section these options bind from.</summary>
    public const string SectionName = "ZWarden:Backups";

    /// <summary>How many automatic (<c>PreOperation</c>) backups each server keeps; older ones are deleted. Manual
    /// backups are never deleted automatically. Default 5, minimum 1.</summary>
    public int KeepAutomatic { get; set; } = 5;
}
