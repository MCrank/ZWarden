using ZWarden.Application.Authorization;
using ZWarden.Application.Operations;
using ZWarden.Application.Servers;
using ZWarden.Domain.Authorization;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Operations;
using ZWarden.Web.Components.Pages.Servers.Sections;
using ZWarden.Web.Components.Servers;
using ZWarden.Web.Time;

namespace ZWarden.Web.Components.Pages.Servers;

/// <summary>Every per-server permission flag the Server Detail page gates on (fail-closed; each service re-checks on
/// every action, ADR 0018).</summary>
public sealed record ServerDetailPermissions(
    PlayerPermissions Players,
    ModManagePermissions Mods,
    BackupPermissions Backups,
    bool CanViewLogs,
    bool CanExecuteConsole,
    bool CanViewDiagnostics,
    bool CanExportDiagnostics,
    bool CanViewMods,
    bool CanStart,
    bool CanStop,
    bool CanRestart,
    bool CanRecreate,
    bool CanDelete,
    bool CanUpdate);

/// <summary>What the last successful game update did (#273), formatted in the operator's zone.</summary>
public sealed record ServerLastUpdate(string Summary, string At);

/// <summary>The live part of the header: the status badge and buttons (#249), the last failure (#266), the last game
/// update (#273), and the Server as last reported.</summary>
public sealed record ServerDetailHeader(
    ServerSummary Server, ServerStatusView Status, ServerFailureView? LastFailure, ServerLastUpdate? LastUpdate);

/// <summary>The page's first load (#299). It is a plain, JSON-serializable record, so the prerender hands it to the
/// circuit through <c>[PersistentState]</c> instead of the circuit loading it again.</summary>
public sealed record ServerDetailState(ServerDetailPermissions Permissions, ServerDetailHeader Header);

/// <summary>
/// Loads the Server Detail page (#299). An interactive page calls it through <c>ActionScopeRunner</c>, so each load and
/// each header poll gets a scope (and a <c>DbContext</c>) of its own (ADR 0046 Q7). The Server load is the
/// tenant-filtered, fail-closed Server.View in <see cref="IServerInventory.GetVisibleAsync"/>; a Server the operator
/// can't see is <see langword="null"/>, never a hint that it exists.
/// </summary>
public sealed class ServerDetailQuery
{
    private readonly IServerInventory _inventory;
    private readonly IOperationStore _operations;
    private readonly IPermissionChecker _permissions;

    public ServerDetailQuery(IServerInventory inventory, IOperationStore operations, IPermissionChecker permissions)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(operations);
        ArgumentNullException.ThrowIfNull(permissions);
        _inventory = inventory;
        _operations = operations;
        _permissions = permissions;
    }

    /// <summary>The Server, every permission flag and the header, or <see langword="null"/> when the operator can't
    /// view the Server.</summary>
    public async Task<ServerDetailState?> LoadAsync(UserId user, ServerId serverId, TimeZoneInfo zone, CancellationToken ct)
    {
        ServerDetailHeader? header = await ReadHeaderAsync(user, serverId, zone, ct).ConfigureAwait(false);
        if (header is null)
        {
            return null;
        }

        async Task<bool> Can(PermissionDefinition permission) =>
            (await _permissions.EvaluateAsync(user, permission, serverId, ct).ConfigureAwait(false)).IsAllowed;

        // Diagnostics.View / .Export are tenant-wide (not server-scoped), so they evaluate without a Server.
        async Task<bool> CanTenantWide(PermissionDefinition permission) =>
            (await _permissions.EvaluateAsync(user, permission, cancellationToken: ct).ConfigureAwait(false)).IsAllowed;

        bool canRestart = await Can(Permissions.ServerRestart).ConfigureAwait(false);
        var permissions = new ServerDetailPermissions(
            Players: new PlayerPermissions(
                CanView: await Can(Permissions.PlayerView).ConfigureAwait(false),
                CanKick: await Can(Permissions.PlayerKick).ConfigureAwait(false),
                CanBan: await Can(Permissions.PlayerBan).ConfigureAwait(false),
                CanUnban: await Can(Permissions.PlayerUnban).ConfigureAwait(false),
                CanConfigEdit: await Can(Permissions.ServerConfigurationEdit).ConfigureAwait(false)),
            Mods: new ModManagePermissions(
                CanInstall: await Can(Permissions.ModInstall).ConfigureAwait(false),
                CanRemove: await Can(Permissions.ModRemove).ConfigureAwait(false),
                CanUpdate: await Can(Permissions.ModUpdate).ConfigureAwait(false),
                CanRestart: canRestart),
            Backups: new BackupPermissions(
                CanView: await Can(Permissions.BackupView).ConfigureAwait(false),
                CanCreate: await Can(Permissions.BackupCreate).ConfigureAwait(false),
                CanRestore: await Can(Permissions.BackupRestore).ConfigureAwait(false),
                CanDelete: await Can(Permissions.BackupDelete).ConfigureAwait(false)),
            CanViewLogs: await Can(Permissions.ConsoleView).ConfigureAwait(false),
            CanExecuteConsole: await Can(Permissions.ConsoleExecute).ConfigureAwait(false),
            CanViewDiagnostics: await CanTenantWide(Permissions.DiagnosticsView).ConfigureAwait(false),
            CanExportDiagnostics: await CanTenantWide(Permissions.DiagnosticsExport).ConfigureAwait(false),
            CanViewMods: await Can(Permissions.ModView).ConfigureAwait(false),
            CanStart: await Can(Permissions.ServerStart).ConfigureAwait(false),
            CanStop: await Can(Permissions.ServerStop).ConfigureAwait(false),
            CanRestart: canRestart,
            CanRecreate: await Can(Permissions.ServerRecreate).ConfigureAwait(false),
            CanDelete: await Can(Permissions.ServerDelete).ConfigureAwait(false),
            CanUpdate: await Can(Permissions.ServerUpdate).ConfigureAwait(false));

        return new ServerDetailState(permissions, header);
    }

    /// <summary>The header as it is now (#249/#266/#273), or <see langword="null"/> when the Server is gone or no longer
    /// viewable. The page polls this in its circuit, replacing <c>live-status.js</c> there.</summary>
    public async Task<ServerDetailHeader?> ReadHeaderAsync(UserId user, ServerId serverId, TimeZoneInfo zone, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(zone);
        ServerSummary? server = await _inventory.GetVisibleAsync(user, serverId, ct).ConfigureAwait(false);
        if (server is null)
        {
            return null;
        }

        Operation? active = await _operations.FindActiveForServerAsync(serverId, ct).ConfigureAwait(false);
        ServerStatusView status = ServerLiveStatus.Resolve(server.LastRunState, active?.Kind, active?.StatusLine);

        // #266: the last action's failure, unless a new action is already in flight (it supersedes the old one).
        ServerFailureView? failure = active is null
            ? ServerFailureView.From(await _operations.FindUnresolvedFailureForServerAsync(serverId, ct).ConfigureAwait(false), zone)
            : null;

        // #273: the last game update's result line (Agent-observed builds, set by the hub on completion).
        Operation? updated = await _operations
            .FindLatestSucceededForServerAsync(serverId, OperationKind.UpdateServer, ct).ConfigureAwait(false);
        ServerLastUpdate? lastUpdate = updated is { StatusLine: { } summary } && !string.IsNullOrWhiteSpace(summary)
            ? new ServerLastUpdate(summary, OperatorTimeZone.Format(updated.CompletedAt ?? updated.EnqueuedAt, zone))
            : null;

        return new ServerDetailHeader(server, status, failure, lastUpdate);
    }
}
