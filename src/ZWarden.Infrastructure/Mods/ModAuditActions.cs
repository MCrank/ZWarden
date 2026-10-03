namespace ZWarden.Infrastructure.Mods;

/// <summary>
/// The stable, machine-readable audit action names for mod-management changes (F22; F6, ADR 0019), mirroring
/// <see cref="Configuration.ConfigurationAuditActions"/>. A mod change is applied as a config edit, so it leaves a
/// dual trail: an audit entry named here (the operator intent) and the underlying Configuration Revision (the exact
/// <c>WorkshopItems=</c>/<c>Mods=</c> bytes).
/// </summary>
public static class ModAuditActions
{
    /// <summary>A Workshop item was added to <c>WorkshopItems=</c>.</summary>
    public const string Installed = "Mod.Installed";

    /// <summary>One or more Workshop items were removed from <c>WorkshopItems=</c> (and their exclusive mods from
    /// <c>Mods=</c>).</summary>
    public const string Removed = "Mod.Removed";

    /// <summary>Mods were added to <c>Mods=</c>.</summary>
    public const string Enabled = "Mod.Enabled";

    /// <summary>Mods were removed from <c>Mods=</c>.</summary>
    public const string Disabled = "Mod.Disabled";

    /// <summary>The <c>Mods=</c> load order was rewritten.</summary>
    public const string Reordered = "Mod.Reordered";

    /// <summary>Which of one Workshop item's mods are on was chosen (Pick parts, #291).</summary>
    public const string PartsSet = "Mod.PartsSet";

    /// <summary>A pending change to one Workshop item was undone: its <c>WorkshopItems=</c> entry and mod ids put back
    /// the way the server last booted (#291).</summary>
    public const string Undone = "Mod.Undone";

    /// <summary>A Workshop-content refresh was triggered: a safe restart, since PZ re-fetches <c>WorkshopItems=</c> at boot
    /// (#273; F22 originally ran the F17 game update).</summary>
    public const string Updated = "Mod.Updated";

    /// <summary>Unused Workshop downloads were deleted from the install volume (#293). Not a config edit, so there is
    /// no Configuration Revision; the Operation's result says what the Agent did with each id.</summary>
    public const string DownloadsDeleted = "Mod.DownloadsDeleted";
}
