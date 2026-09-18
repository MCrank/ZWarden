using ZWarden.Application.Audit;
using ZWarden.Application.Authorization;
using ZWarden.Application.Configuration;
using ZWarden.Application.Mods;
using ZWarden.Application.Operations;
using ZWarden.Domain.Audit;
using ZWarden.Domain.Authorization;
using ZWarden.Domain.Configuration;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Operations;
using ZWarden.Domain.Servers;
using ZWarden.Infrastructure.Configuration;
using ZWarden.Infrastructure.Servers;

namespace ZWarden.Infrastructure.Mods;

/// <summary>
/// The tenant-scoped mod-management service (F22). A mod's desired state <b>is</b> the Server's
/// <c>WorkshopItems=</c>/<c>Mods=</c> config, so enable/disable/reorder/add/remove recompute those list values from
/// the last observed Mod Inventory (F21, <see cref="IModInventoryCache"/>) via the pure <see cref="ModListEditor"/>
/// and enqueue an F20b config-apply Operation through the shared <see cref="ConfigApplyEnqueuer"/> — drift-checked,
/// byte-preserving, recorded as a Configuration Revision, and audited under a <c>Mod.*</c> action.
/// <see cref="UpdateModsAsync"/> instead enqueues the F17 <c>UpdateServer</c> Operation. Fail-closed (ADR 0018):
/// every verb resolves the Server through the tenant filter and authorizes its mapped server-scoped permission, and
/// reads the inventory ownership-guarded by the Server's true owning Agent.
/// </summary>
public sealed class ServerModManager : IServerModManager
{
    private readonly ServerRepository _servers;
    private readonly IPermissionChecker _permissions;
    private readonly IModInventoryCache _inventory;
    private readonly IOperationCoordinator _operations;
    private readonly IAuditWriter _audit;
    private readonly ConfigApplyEnqueuer _enqueuer;

    public ServerModManager(
        ServerRepository servers,
        IPermissionChecker permissions,
        IModInventoryCache inventory,
        IOperationCoordinator operations,
        ConfigurationRevisionRepository revisions,
        IAuditWriter audit)
    {
        ArgumentNullException.ThrowIfNull(servers);
        ArgumentNullException.ThrowIfNull(permissions);
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(operations);
        ArgumentNullException.ThrowIfNull(revisions);
        ArgumentNullException.ThrowIfNull(audit);
        _servers = servers;
        _permissions = permissions;
        _inventory = inventory;
        _operations = operations;
        _audit = audit;
        _enqueuer = new ConfigApplyEnqueuer(operations, revisions, audit);
    }

    /// <inheritdoc />
    public Task<ModManagementResult> EnableModsAsync(
        UserId user, ServerId server, IReadOnlyList<string> modIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(modIds);
        return ApplyListEditAsync(
            user, server, Permissions.ModInstall, ModAuditActions.Enabled, $"enable {modIds.Count} mod(s)",
            inventory => ValidateModIds(modIds) ?? Ok(ModListEditor.EnableMods(inventory.EnabledModIds, modIds)),
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<ModManagementResult> DisableModsAsync(
        UserId user, ServerId server, IReadOnlyList<string> modIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(modIds);
        return ApplyListEditAsync(
            user, server, Permissions.ModRemove, ModAuditActions.Disabled, $"disable {modIds.Count} mod(s)",
            inventory => ValidateModIds(modIds) ?? Ok(ModListEditor.DisableMods(inventory.EnabledModIds, modIds)),
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<ModManagementResult> ReorderModsAsync(
        UserId user, ServerId server, IReadOnlyList<string> orderedModIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(orderedModIds);
        return ApplyListEditAsync(
            user, server, Permissions.ModInstall, ModAuditActions.Reordered, "reorder mods",
            inventory => ValidateModIds(orderedModIds) ?? Ok(ModListEditor.ReorderMods(inventory.EnabledModIds, orderedModIds)),
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<ModManagementResult> AddWorkshopItemAsync(
        UserId user, ServerId server, string workshopId, CancellationToken cancellationToken = default)
    {
        return ApplyListEditAsync(
            user, server, Permissions.ModInstall, ModAuditActions.Installed, $"add workshop item {workshopId}",
            inventory => ValidateWorkshopId(workshopId) ?? Ok(ModListEditor.AddWorkshopItem(inventory.ConfiguredWorkshopIds, workshopId)),
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<ModManagementResult> RemoveWorkshopItemsAsync(
        UserId user, ServerId server, IReadOnlyList<string> workshopIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workshopIds);
        return ApplyListEditAsync(
            user, server, Permissions.ModRemove, ModAuditActions.Removed, $"remove {workshopIds.Count} workshop item(s)",
            inventory => ValidateWorkshopIds(workshopIds) ?? Ok(ModListEditor.RemoveWorkshopItems(
                inventory.ConfiguredWorkshopIds, inventory.EnabledModIds, inventory.InstalledItems, workshopIds)),
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ModManagementResult> UpdateModsAsync(
        UserId user, ServerId server, CancellationToken cancellationToken = default)
    {
        Server? resolved = await ResolveAndAuthorizeAsync(user, server, Permissions.ModUpdate, cancellationToken).ConfigureAwait(false);
        if (resolved is null)
        {
            // Distinguish not-found from not-authorized for the caller.
            return await DenyResolveAsync(user, server, Permissions.ModUpdate, cancellationToken).ConfigureAwait(false);
        }

        try
        {
            // The same F17 UpdateServer Operation the lifecycle exposes, gated here by Mod.Update so a mod-manager
            // role can refresh Workshop content without a full server-update grant (ADR 0025).
            Operation operation = await _operations.EnqueueAsync(
                new EnqueueOperationRequest(
                    resolved.AgentId, OperationKind.UpdateServer, IsMutating: true, Guid.NewGuid().ToString("N"),
                    ServerId: resolved.Id),
                user,
                cancellationToken).ConfigureAwait(false);

            await _audit.WriteAsync(
                new AuditEntry(ModAuditActions.Updated, AuditOutcome.Succeeded, user, resolved.Id, $"operation {operation.Id}"),
                cancellationToken).ConfigureAwait(false);

            return ModManagementResult.Success(operation.Id);
        }
        catch (ServerBusyException)
        {
            await _audit.WriteAsync(
                new AuditEntry(ModAuditActions.Updated, AuditOutcome.Failed, user, resolved.Id, "server busy"),
                cancellationToken).ConfigureAwait(false);
            return ModManagementResult.Denied(ModManagementFailure.ServerBusy);
        }
    }

    // The shared fail-closed pipeline for the list-editing verbs: resolve + authorize, load the ownership-guarded
    // inventory, compute the edits, map a no-op / invalid reorder / validation failure, else enqueue a config-apply.
    private async Task<ModManagementResult> ApplyListEditAsync(
        UserId user,
        ServerId server,
        PermissionDefinition permission,
        string auditAction,
        string auditSubject,
        Func<ModInventory, ListEditOutcome> compute,
        CancellationToken cancellationToken)
    {
        Server? resolved = await ResolveAndAuthorizeAsync(user, server, permission, cancellationToken).ConfigureAwait(false);
        if (resolved is null)
        {
            return await DenyResolveAsync(user, server, permission, cancellationToken).ConfigureAwait(false);
        }

        // Ownership-guarded (trust-boundaries §8): the inventory is returned only when we name the Server's true
        // owning Agent. No observed inventory ⇒ the operator must refresh discovery before mutating.
        ModInventory? inventory = _inventory.GetLatest(server, resolved.AgentId);
        if (inventory is null)
        {
            return ModManagementResult.Denied(
                ModManagementFailure.SnapshotUnavailable, "Refresh mod discovery for this server before making changes.");
        }

        ListEditOutcome outcome = compute(inventory);
        if (outcome.Failure is { } failure)
        {
            return ModManagementResult.Denied(failure, outcome.Message);
        }

        ModListEditResult edit = outcome.Result!;
        switch (edit.Status)
        {
            case ModListEditStatus.NoChange:
                return ModManagementResult.Denied(ModManagementFailure.NoChange, "The request did not change the mod list.");
            case ModListEditStatus.InvalidReorder:
                return ModManagementResult.Denied(
                    ModManagementFailure.InvalidReorder, "A reorder must contain exactly the currently enabled mods.");
        }

        // A mod-list change drift-checks against the last recorded revision (no interactive live read), so it
        // supplies no live-read baseline (F20c, ADR 0042) — the enqueuer falls back to the recorded revision.
        ServerConfigurationResult enqueued = await _enqueuer
            .EnqueueAsync(
                user, resolved, PzConfigFile.Ini, edit.Edits, auditAction, auditSubject,
                expectedBaselineHash: null, cancellationToken)
            .ConfigureAwait(false);
        return MapEnqueued(enqueued);
    }

    // Resolve the Server through the tenant filter and authorize the server-scoped permission. Returns the Server
    // only when both pass; null otherwise (the caller distinguishes the two reasons via DenyResolveAsync).
    private async Task<Server?> ResolveAndAuthorizeAsync(
        UserId user, ServerId server, PermissionDefinition permission, CancellationToken cancellationToken)
    {
        Server? resolved = await _servers.FindByIdAsync(server, cancellationToken).ConfigureAwait(false);
        if (resolved is null)
        {
            return null;
        }

        AuthorizationDecision decision = await _permissions
            .EvaluateAsync(user, permission, server: server, cancellationToken).ConfigureAwait(false);
        return decision.IsAllowed ? resolved : null;
    }

    // Recompute which reason a null resolve was: an unknown/foreign Server is ServerNotFound, otherwise the
    // permission was denied. (A second resolve is cheap and keeps the happy path a single method.)
    private async Task<ModManagementResult> DenyResolveAsync(
        UserId user, ServerId server, PermissionDefinition permission, CancellationToken cancellationToken)
    {
        Server? resolved = await _servers.FindByIdAsync(server, cancellationToken).ConfigureAwait(false);
        if (resolved is null)
        {
            return ModManagementResult.Denied(ModManagementFailure.ServerNotFound);
        }

        return ModManagementResult.Denied(ModManagementFailure.NotAuthorized);
    }

    private static ModManagementResult MapEnqueued(ServerConfigurationResult result)
    {
        if (result.Succeeded)
        {
            return ModManagementResult.Success(result.Operation!.Value);
        }

        return result.Failure switch
        {
            ServerConfigurationFailure.ServerBusy => ModManagementResult.Denied(ModManagementFailure.ServerBusy),
            _ => ModManagementResult.Denied(ModManagementFailure.InvalidInput, result.Message),
        };
    }

    private static ListEditOutcome Ok(ModListEditResult result) => new(result, null, null);

    private static ListEditOutcome? ValidateModIds(IReadOnlyList<string> modIds)
    {
        if (modIds.Count == 0)
        {
            return new ListEditOutcome(null, ModManagementFailure.InvalidInput, "No mods were supplied.");
        }

        if (modIds.Any(id => string.IsNullOrWhiteSpace(id) || id.Length > MaxIdLength))
        {
            return new ListEditOutcome(null, ModManagementFailure.InvalidInput, "A mod id is empty or too long.");
        }

        return null;
    }

    private static ListEditOutcome? ValidateWorkshopIds(IReadOnlyList<string> workshopIds)
    {
        if (workshopIds.Count == 0)
        {
            return new ListEditOutcome(null, ModManagementFailure.InvalidInput, "No workshop items were supplied.");
        }

        return workshopIds.Select(ValidateWorkshopId).FirstOrDefault(o => o is not null);
    }

    private static ListEditOutcome? ValidateWorkshopId(string workshopId)
    {
        if (string.IsNullOrWhiteSpace(workshopId) || workshopId.Length > MaxIdLength || !workshopId.All(char.IsAsciiDigit))
        {
            return new ListEditOutcome(null, ModManagementFailure.InvalidInput, "A workshop item id must be numeric.");
        }

        return null;
    }

    private const int MaxIdLength = 256;

    // A computed list edit, or a validation failure — the discriminated result of the per-verb compute step.
    private sealed record ListEditOutcome(ModListEditResult? Result, ModManagementFailure? Failure, string? Message);
}
