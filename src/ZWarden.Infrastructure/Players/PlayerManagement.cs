using ZWarden.Application.Audit;
using ZWarden.Application.Authorization;
using ZWarden.Application.Operations;
using ZWarden.Application.Players;
using ZWarden.Domain.Audit;
using ZWarden.Domain.Authorization;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Operations;
using ZWarden.Domain.Players;
using ZWarden.Domain.Servers;
using ZWarden.Infrastructure.Persistence;
using ZWarden.Infrastructure.Servers;

namespace ZWarden.Infrastructure.Players;

/// <summary>
/// The tenant-scoped player-management service (F19): kick/ban/unban/remove-from-whitelist and the whitelist-mode
/// toggle as durable, authorized, audited, <b>non-mutating</b> Operations — a sibling of
/// <see cref="ServerDiagnostics"/>. Each action is <b>fail-closed</b> (ADR 0018): it resolves the Server through
/// the tenant filter (a foreign/unknown Server is <see cref="PlayerManagementFailure.ServerNotFound"/>),
/// authorizes the matching server-scoped permission against that specific Server, and validates the operator's
/// username/reason (F19 D-3) before enqueuing. Being non-mutating, an action never claims the per-server lock
/// (ADR 0022), so there is no <c>ServerBusy</c> case. The target parameters ride the Operation's command payload
/// for the Agent to quote (ADR 0026). A ban writes an advisory registry record and an unban lifts the matching
/// one (ADR 0027) — recorded from the operator's intent at enqueue, with the Operation/audit trail authoritative
/// for whether the RCON command actually took effect.
/// </summary>
public sealed class PlayerManagement : IPlayerManagement
{
    private readonly ServerRepository _servers;
    private readonly IPermissionChecker _permissions;
    private readonly IOperationCoordinator _operations;
    private readonly IAuditWriter _audit;
    private readonly BanRecordRepository _bans;
    private readonly ZWardenDbContext _context;
    private readonly TimeProvider _clock;

    public PlayerManagement(
        ServerRepository servers,
        IPermissionChecker permissions,
        IOperationCoordinator operations,
        IAuditWriter audit,
        BanRecordRepository bans,
        ZWardenDbContext context,
        TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(servers);
        ArgumentNullException.ThrowIfNull(permissions);
        ArgumentNullException.ThrowIfNull(operations);
        ArgumentNullException.ThrowIfNull(audit);
        ArgumentNullException.ThrowIfNull(bans);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(clock);
        _servers = servers;
        _permissions = permissions;
        _operations = operations;
        _audit = audit;
        _bans = bans;
        _context = context;
        _clock = clock;
    }

    /// <inheritdoc />
    public async Task<PlayerManagementResult> ListPlayersAsync(UserId user, ServerId server, CancellationToken cancellationToken = default)
    {
        Server? resolved = await _servers.FindByIdAsync(server, cancellationToken).ConfigureAwait(false);
        if (resolved is null)
        {
            return PlayerManagementResult.Denied(PlayerManagementFailure.ServerNotFound);
        }

        AuthorizationDecision decision = await _permissions
            .EvaluateAsync(user, Permissions.PlayerView, server: server, cancellationToken).ConfigureAwait(false);
        if (!decision.IsAllowed)
        {
            return PlayerManagementResult.Denied(PlayerManagementFailure.NotAuthorized);
        }

        // A read: enqueue the non-mutating enumeration, but do not audit it (criterion 11 is administrative
        // activity). The observed roster returns on the completion into the in-memory roster cache.
        Operation operation = await _operations.EnqueueAsync(
            new EnqueueOperationRequest(
                resolved.AgentId, OperationKind.ListPlayers, IsMutating: false, Guid.NewGuid().ToString("N"),
                ServerId: server),
            user,
            cancellationToken).ConfigureAwait(false);

        return PlayerManagementResult.Success(operation.Id);
    }

    /// <inheritdoc />
    public Task<PlayerManagementResult> KickAsync(UserId user, ServerId server, string username, string? reason, CancellationToken cancellationToken = default)
        => ActAsync(user, server, Permissions.PlayerKick, OperationKind.KickPlayer, PlayerAuditActions.Kicked,
            new PlayerCommandPayload(Username: username, Reason: reason), username, reason, subject: username, onEnqueued: null, cancellationToken);

    /// <inheritdoc />
    public Task<PlayerManagementResult> BanAsync(UserId user, ServerId server, string username, string? reason, CancellationToken cancellationToken = default)
        => ActAsync(user, server, Permissions.PlayerBan, OperationKind.BanPlayer, PlayerAuditActions.Banned,
            new PlayerCommandPayload(Username: username, Reason: reason), username, reason, subject: username,
            onEnqueued: (now, ct) => RecordBanAsync(server, username, reason, user, now, ct), cancellationToken);

    /// <inheritdoc />
    public Task<PlayerManagementResult> UnbanAsync(UserId user, ServerId server, string username, CancellationToken cancellationToken = default)
        => ActAsync(user, server, Permissions.PlayerUnban, OperationKind.UnbanPlayer, PlayerAuditActions.Unbanned,
            new PlayerCommandPayload(Username: username), username, reason: null, subject: username,
            onEnqueued: (now, ct) => LiftBanAsync(server, username, user, now, ct), cancellationToken);

    /// <inheritdoc />
    public Task<PlayerManagementResult> RemoveFromWhitelistAsync(UserId user, ServerId server, string username, CancellationToken cancellationToken = default)
        => ActAsync(user, server, Permissions.PlayerBan, OperationKind.RemoveFromWhitelist, PlayerAuditActions.RemovedFromWhitelist,
            new PlayerCommandPayload(Username: username), username, reason: null, subject: username, onEnqueued: null, cancellationToken);

    /// <inheritdoc />
    public Task<PlayerManagementResult> SetWhitelistModeAsync(UserId user, ServerId server, bool open, CancellationToken cancellationToken = default)
        => ActAsync(user, server, Permissions.ServerConfigurationEdit, OperationKind.SetWhitelistMode, PlayerAuditActions.WhitelistModeChanged,
            new PlayerCommandPayload(Open: open), username: null, reason: null, subject: $"Open={(open ? "true" : "false")}", onEnqueued: null, cancellationToken);

    private async Task<PlayerManagementResult> ActAsync(
        UserId user,
        ServerId server,
        PermissionDefinition permission,
        OperationKind kind,
        string auditAction,
        PlayerCommandPayload payload,
        string? username,
        string? reason,
        string? subject,
        Func<DateTimeOffset, CancellationToken, Task>? onEnqueued,
        CancellationToken cancellationToken)
    {
        // Resolve first, through the tenant filter: an unknown or foreign-tenant Server is ServerNotFound, and
        // gives the server-scoped authorization a concrete resource to check.
        Server? resolved = await _servers.FindByIdAsync(server, cancellationToken).ConfigureAwait(false);
        if (resolved is null)
        {
            return PlayerManagementResult.Denied(PlayerManagementFailure.ServerNotFound);
        }

        AuthorizationDecision decision = await _permissions
            .EvaluateAsync(user, permission, server: server, cancellationToken).ConfigureAwait(false);
        if (!decision.IsAllowed)
        {
            return PlayerManagementResult.Denied(PlayerManagementFailure.NotAuthorized);
        }

        // Validate the operator's arguments at the edge (D-3) — the same rules the Agent re-checks before it
        // quotes the command, so an injection attempt is refused before an Operation is even enqueued.
        if (username is not null && PlayerCommandRules.ValidateUsername(username) is { } usernameError)
        {
            return PlayerManagementResult.Denied(PlayerManagementFailure.InvalidInput, usernameError);
        }

        if (PlayerCommandRules.ValidateReason(reason) is { } reasonError)
        {
            return PlayerManagementResult.Denied(PlayerManagementFailure.InvalidInput, reasonError);
        }

        // A non-mutating, server-scoped Operation (ADR 0022): it names the Server but does not take the per-server
        // lock, so it never contends and never throws ServerBusyException. The parameters ride the command payload.
        Operation operation = await _operations.EnqueueAsync(
            new EnqueueOperationRequest(
                resolved.AgentId, kind, IsMutating: false, Guid.NewGuid().ToString("N"),
                ServerId: server, CommandPayload: payload.ToJson()),
            user,
            cancellationToken).ConfigureAwait(false);

        if (onEnqueued is not null)
        {
            await onEnqueued(_clock.GetUtcNow(), cancellationToken).ConfigureAwait(false);
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        string detail = subject is null ? $"operation {operation.Id}" : $"{subject} — operation {operation.Id}";
        await _audit.WriteAsync(
            new AuditEntry(auditAction, AuditOutcome.Succeeded, user, server, detail),
            cancellationToken).ConfigureAwait(false);

        return PlayerManagementResult.Success(operation.Id);
    }

    private async Task RecordBanAsync(ServerId server, string username, string? reason, UserId user, DateTimeOffset now, CancellationToken cancellationToken)
    {
        // Advisory registry (ADR 0027): record the ban ZWarden issued. If one is already active, keep it — the
        // active-ban uniqueness index would reject a duplicate anyway.
        BanRecord? active = await _bans.FindActiveAsync(server, username, cancellationToken).ConfigureAwait(false);
        if (active is null)
        {
            _bans.Add(BanRecord.Issue(server, username, reason, user, now));
        }
    }

    private async Task LiftBanAsync(ServerId server, string username, UserId user, DateTimeOffset now, CancellationToken cancellationToken)
    {
        BanRecord? active = await _bans.FindActiveAsync(server, username, cancellationToken).ConfigureAwait(false);
        active?.Lift(user, now);
    }
}
