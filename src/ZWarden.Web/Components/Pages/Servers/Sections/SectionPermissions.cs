namespace ZWarden.Web.Components.Pages.Servers.Sections;

// The per-server permission flags the Server Detail page evaluates (fail-closed) and hands to a section (#298). A
// control shows only where the caller holds the matching grant; the service re-checks server-side on submit.

/// <summary>The Players section's Player.* grants, plus Server.Configuration.Edit (which also gates Config).</summary>
public readonly record struct PlayerPermissions(bool CanView, bool CanKick, bool CanBan, bool CanUnban, bool CanConfigEdit)
{
    public bool CanAny => CanView || CanKick || CanBan || CanUnban || CanConfigEdit;
}

/// <summary>The Mods / Mod Browser sections' Mod.* grants, plus Server.Restart for "restart to load".</summary>
public readonly record struct ModManagePermissions(bool CanInstall, bool CanRemove, bool CanUpdate, bool CanRestart)
{
    public bool CanAny => CanInstall || CanRemove || CanUpdate || CanRestart;
}

/// <summary>The Backups section's Backup.* grants.</summary>
public readonly record struct BackupPermissions(bool CanView, bool CanCreate, bool CanRestore, bool CanDelete);
