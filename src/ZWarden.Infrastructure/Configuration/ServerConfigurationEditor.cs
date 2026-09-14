using ZWarden.Application.Audit;
using ZWarden.Application.Authorization;
using ZWarden.Application.Configuration;
using ZWarden.Application.Operations;
using ZWarden.Domain.Audit;
using ZWarden.Domain.Authorization;
using ZWarden.Domain.Configuration;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Operations;
using ZWarden.Domain.Servers;
using ZWarden.Infrastructure.Servers;

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
    private readonly IOperationCoordinator _operations;
    private readonly ConfigurationRevisionRepository _revisions;
    private readonly IAuditWriter _audit;

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
        _operations = operations;
        _revisions = revisions;
        _audit = audit;
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

        // The drift baseline: the last recorded revision's hash for this file, or null when none exists (the
        // Agent then treats it as the first write — no baseline to drift from, ADR 0011).
        ConfigurationRevision? baseline = await _revisions.FindLatestAsync(server, file, cancellationToken).ConfigureAwait(false);
        string payload = new ConfigApplyPayload(file, baseline?.SnapshotHash, edits).ToJson();
        if (payload.Length > Operation.MaxCommandPayloadLength)
        {
            return ServerConfigurationResult.Denied(
                ServerConfigurationFailure.InvalidInput, "Too many edits to apply in one operation; apply fewer at a time.");
        }

        try
        {
            // A mutating, server-scoped Operation on the Server's Agent (ADR 0022). Each apply is a fresh intent,
            // so the idempotency key is fresh; the per-server lock refuses a second in-flight mutation.
            Operation operation = await _operations.EnqueueAsync(
                new EnqueueOperationRequest(
                    resolved.AgentId, OperationKind.ConfigApply, IsMutating: true, Guid.NewGuid().ToString("N"),
                    ServerId: server, CommandPayload: payload),
                user,
                cancellationToken).ConfigureAwait(false);

            await _audit.WriteAsync(
                new AuditEntry(ConfigurationAuditActions.Applied, AuditOutcome.Succeeded, user, server,
                    $"{file}, {edits.Count} edit(s) — operation {operation.Id}"),
                cancellationToken).ConfigureAwait(false);

            return ServerConfigurationResult.Success(operation.Id);
        }
        catch (ServerBusyException)
        {
            // The per-server lock (ADR 0022) refused: another mutating Operation is already in flight.
            await _audit.WriteAsync(
                new AuditEntry(ConfigurationAuditActions.Applied, AuditOutcome.Failed, user, server, "server busy"),
                cancellationToken).ConfigureAwait(false);
            return ServerConfigurationResult.Denied(ServerConfigurationFailure.ServerBusy);
        }
    }
}
