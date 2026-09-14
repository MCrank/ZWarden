using ZWarden.Application.Audit;
using ZWarden.Application.Authorization;
using ZWarden.Application.Configuration;
using ZWarden.Application.Operations;
using ZWarden.Domain.Authorization;
using ZWarden.Domain.Configuration;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Servers;
using ZWarden.Infrastructure.Servers;
using ZWarden.PzConfig.Model;
using ZWarden.PzConfig.Revisions;

namespace ZWarden.Infrastructure.Configuration;

/// <summary>
/// The tenant-scoped configuration-apply service (F20b PR-3): applies surgical value edits to a Server's config
/// file as a durable, authorized, audited, <b>mutating</b> Operation — a sibling of <see cref="ServerLifecycle"/>.
/// Fail-closed (ADR 0018): it resolves the Server through the tenant filter (a foreign/unknown Server is
/// <see cref="ServerConfigurationFailure.ServerNotFound"/>), authorizes <c>ServerConfigurationEdit</c> against
/// that specific Server, and validates the edits before enqueuing. The edits and the last recorded revision's
/// hash (the drift baseline, ADR 0011) ride the Operation's command payload. Being mutating and server-scoped, it
/// claims the per-server lock (ADR 0022), so a config write never runs while a lifecycle Operation is in flight —
/// a contended request surfaces as <see cref="ServerConfigurationFailure.ServerBusy"/>. The Agent, not this
/// service, re-parses the live file, fails the write closed on a drift, and writes byte-preservingly.
/// </summary>
public sealed class ServerConfigurationEditor : IServerConfigurationEditor
{
    private readonly ServerRepository _servers;
    private readonly IPermissionChecker _permissions;
    private readonly ConfigurationRevisionRepository _revisions;
    private readonly ConfigApplyEnqueuer _enqueuer;

    public ServerConfigurationEditor(
        ServerRepository servers,
        IPermissionChecker permissions,
        IOperationCoordinator operations,
        ConfigurationRevisionRepository revisions,
        IAuditWriter audit)
    {
        ArgumentNullException.ThrowIfNull(servers);
        ArgumentNullException.ThrowIfNull(permissions);
        ArgumentNullException.ThrowIfNull(operations);
        ArgumentNullException.ThrowIfNull(revisions);
        ArgumentNullException.ThrowIfNull(audit);
        _servers = servers;
        _permissions = permissions;
        _revisions = revisions;
        _enqueuer = new ConfigApplyEnqueuer(operations, revisions, audit);
    }

    /// <inheritdoc />
    public async Task<ServerConfigurationResult> ApplyAsync(
        UserId user,
        ServerId server,
        PzConfigFile file,
        IReadOnlyList<ConfigApplyEdit> edits,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(edits);

        // Resolve first, through the tenant filter: an unknown or foreign-tenant Server is ServerNotFound, and
        // gives the server-scoped authorization a concrete resource to check.
        Server? resolved = await _servers.FindByIdAsync(server, cancellationToken).ConfigureAwait(false);
        if (resolved is null)
        {
            return ServerConfigurationResult.Denied(ServerConfigurationFailure.ServerNotFound);
        }

        AuthorizationDecision decision = await _permissions
            .EvaluateAsync(user, Permissions.ServerConfigurationEdit, server: server, cancellationToken).ConfigureAwait(false);
        if (!decision.IsAllowed)
        {
            return ServerConfigurationResult.Denied(ServerConfigurationFailure.NotAuthorized);
        }

        if (edits.Count == 0)
        {
            return ServerConfigurationResult.Denied(ServerConfigurationFailure.InvalidInput, "No configuration edits were supplied.");
        }

        if (edits.Any(e => string.IsNullOrWhiteSpace(e.Path)))
        {
            return ServerConfigurationResult.Denied(ServerConfigurationFailure.InvalidInput, "An edit has an empty configuration path.");
        }

        return await EnqueueAsync(user, resolved, file, edits, ConfigurationAuditActions.Applied, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<ServerConfigurationResult> RestoreAsync(
        UserId user,
        ServerId server,
        ConfigurationRevisionId revision,
        CancellationToken cancellationToken = default)
    {
        Server? resolved = await _servers.FindByIdAsync(server, cancellationToken).ConfigureAwait(false);
        if (resolved is null)
        {
            return ServerConfigurationResult.Denied(ServerConfigurationFailure.ServerNotFound);
        }

        AuthorizationDecision decision = await _permissions
            .EvaluateAsync(user, Permissions.ServerConfigurationEdit, server: server, cancellationToken).ConfigureAwait(false);
        if (!decision.IsAllowed)
        {
            return ServerConfigurationResult.Denied(ServerConfigurationFailure.NotAuthorized);
        }

        // Resolve the target revision through the tenant filter, and confirm it belongs to this Server — a
        // revision id for another Server (or tenant) is not a restore target here.
        ConfigurationRevision? target = await _revisions.FindByIdAsync(revision, cancellationToken).ConfigureAwait(false);
        if (target is null || target.ServerId != server)
        {
            return ServerConfigurationResult.Denied(
                ServerConfigurationFailure.ServerNotFound, "That revision does not exist for this server.");
        }

        PzConfigFile file = target.File;

        // The current recorded state of this file is both what we restore *from* and the drift baseline the
        // Agent re-checks. Restoring the current revision itself is a no-op.
        ConfigurationRevision? current = await _revisions.FindLatestAsync(server, file, cancellationToken).ConfigureAwait(false);
        PzValueSnapshot targetSnapshot = PzValueSnapshot.Parse(target.CanonicalSnapshot);
        PzValueSnapshot currentSnapshot = current is null
            ? targetSnapshot
            : PzValueSnapshot.Parse(current.CanonicalSnapshot);

        // Only the differing scalars become edits — a whole-file resend would blow the payload cap, and PZ owns
        // the key set, so a structural add/remove is reported rather than applied (values-only writer, ADR 0010).
        PzRestorePlan plan = PzRestore.PlanTo(targetSnapshot, currentSnapshot);
        if (plan.Edits.Count == 0)
        {
            string reason = plan.Obstacles.Count > 0
                ? "That revision differs from the current configuration only by keys that cannot be restored surgically."
                : "That revision already matches the current configuration.";
            return ServerConfigurationResult.Denied(ServerConfigurationFailure.InvalidInput, reason);
        }

        List<ConfigApplyEdit> edits = [.. plan.Edits.Select(e => new ConfigApplyEdit(e.Path, KindOf(e.Value), WireValueOf(e.Value)))];
        return await EnqueueAsync(user, resolved, file, edits, ConfigurationAuditActions.Restored, cancellationToken)
            .ConfigureAwait(false);
    }

    // The shared enqueue half of apply and restore: capture the drift baseline, size-check the payload, enqueue a
    // mutating server-scoped Operation, and audit. Delegates to the enqueuer F22's mod manager also reuses; the
    // audit subject preserves the "{file}, {n} edit(s)" detail. The caller has already resolved and authorized.
    private Task<ServerConfigurationResult> EnqueueAsync(
        UserId user,
        Server resolved,
        PzConfigFile file,
        IReadOnlyList<ConfigApplyEdit> edits,
        string auditAction,
        CancellationToken cancellationToken)
        => _enqueuer.EnqueueAsync(
            user, resolved, file, edits, auditAction, $"{file}, {edits.Count} edit(s)", cancellationToken);

    private static ConfigEditKind KindOf(PzValue value) => value switch
    {
        PzBoolean => ConfigEditKind.Bool,
        PzNumber => ConfigEditKind.Number,
        PzString => ConfigEditKind.Text,
        _ => throw new ArgumentException($"A {value.GetType().Name} is not a scalar edit value.", nameof(value)),
    };

    private static string WireValueOf(PzValue value) => value switch
    {
        PzBoolean b => b.Value ? "true" : "false",
        PzNumber n => n.Lexeme,
        PzString s => s.Value,
        _ => throw new ArgumentException($"A {value.GetType().Name} is not a scalar edit value.", nameof(value)),
    };
}
