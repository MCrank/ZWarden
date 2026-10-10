using ZWarden.Application.Audit;
using ZWarden.Application.Authorization;
using ZWarden.Application.Configuration;
using ZWarden.Application.Mods;
using ZWarden.Application.Operations;
using ZWarden.Application.Servers;
using ZWarden.Domain.Audit;
using ZWarden.Domain.Authorization;
using ZWarden.Domain.Configuration;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Mods;
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
/// <see cref="UpdateModsAsync"/> instead enqueues a safe restart, which is what pulls Workshop updates (#273). Fail-closed (ADR 0018):
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
    private readonly ServerWorkshopItemRepository _items;
    private readonly ServerModStateRepository _states;

    public ServerModManager(
        ServerRepository servers,
        IPermissionChecker permissions,
        IModInventoryCache inventory,
        IOperationCoordinator operations,
        ConfigurationRevisionRepository revisions,
        IAuditWriter audit,
        ServerWorkshopItemRepository items,
        ServerModStateRepository states)
    {
        ArgumentNullException.ThrowIfNull(servers);
        ArgumentNullException.ThrowIfNull(permissions);
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(operations);
        ArgumentNullException.ThrowIfNull(revisions);
        ArgumentNullException.ThrowIfNull(audit);
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(states);
        _servers = servers;
        _permissions = permissions;
        _inventory = inventory;
        _operations = operations;
        _audit = audit;
        _enqueuer = new ConfigApplyEnqueuer(operations, revisions, audit);
        _items = items;
        _states = states;
    }

    /// <inheritdoc />
    public Task<ModManagementResult> EnableModsAsync(
        UserId user, ServerId server, IReadOnlyList<string> modIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(modIds);
        return ApplyListEditAsync(
            user, server, Permissions.ModInstall, ModAuditActions.Enabled, $"enable {modIds.Count} mod(s)",
            inventory => ValidateModIds(modIds, out PzModId[] ids) ?? Ok(ModListEditor.EnableMods(inventory.EnabledModIds, ids)),
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<ModManagementResult> DisableModsAsync(
        UserId user, ServerId server, IReadOnlyList<string> modIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(modIds);
        return ApplyListEditAsync(
            user, server, Permissions.ModRemove, ModAuditActions.Disabled, $"disable {modIds.Count} mod(s)",
            inventory => ValidateModIds(modIds, out PzModId[] ids) ?? Ok(ModListEditor.DisableMods(inventory.EnabledModIds, ids)),
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<ModManagementResult> ReorderModsAsync(
        UserId user, ServerId server, IReadOnlyList<string> orderedModIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(orderedModIds);
        return ApplyListEditAsync(
            user, server, Permissions.ModInstall, ModAuditActions.Reordered, "reorder mods",
            inventory => ValidateModIds(orderedModIds, out PzModId[] ids) ?? Ok(ModListEditor.ReorderMods(inventory.EnabledModIds, ids)),
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
    public Task<ModManagementResult> InstallWorkshopItemsAsync(
        UserId user,
        ServerId server,
        IReadOnlyList<string> workshopIds,
        IReadOnlyList<string> modIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workshopIds);
        ArgumentNullException.ThrowIfNull(modIds);
        string subject = workshopIds.Count == 0
            ? "install workshop item"
            : $"install workshop item {workshopIds[0]}"
              + (workshopIds.Count > 1 ? $" (+{workshopIds.Count - 1} required)" : string.Empty)
              + $" with {modIds.Count} mod(s)";
        return ApplyListEditAsync(
            user, server, Permissions.ModInstall, ModAuditActions.Installed, subject,
            inventory =>
            {
                if (ValidateWorkshopIds(workshopIds) is { } badWorkshopId)
                {
                    return badWorkshopId;
                }

                // No ids is valid: the description listed none, so only WorkshopItems= is written (Pick parts later).
                PzModId[] ids = [];
                if (modIds.Count > 0 && ValidateModIds(modIds, out ids) is { } badModId)
                {
                    return badModId;
                }

                return Ok(ModListEditor.InstallWorkshopItems(
                    inventory.ConfiguredWorkshopIds, inventory.EnabledModIds, workshopIds, ids));
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ModManagementResult> RemoveWorkshopItemsAsync(
        UserId user, ServerId server, IReadOnlyList<string> workshopIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workshopIds);
        // Guessed ids (#291) stand in for mod.info of an item not yet on disk — Install enabled them before the
        // download, so Remove must be able to take them back out. Read only after authorization (inside the pipeline).
        return await ApplyListEditAsync(
            user, server, Permissions.ModRemove, ModAuditActions.Removed, $"remove {workshopIds.Count} workshop item(s)",
            async inventory => ValidateWorkshopIds(workshopIds) ?? Ok(ModListEditor.RemoveWorkshopItems(
                inventory.ConfiguredWorkshopIds,
                inventory.EnabledModIds,
                await KnownProvidersAsync(server, inventory, cancellationToken).ConfigureAwait(false),
                workshopIds)),
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<ModManagementResult> DeleteDownloadsAsync(
        UserId user, ServerId server, IReadOnlyList<string> workshopIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workshopIds);
        if (ValidateWorkshopIds(workshopIds) is { } invalid)
        {
            return ModManagementResult.Denied(invalid.Failure!.Value, invalid.Message);
        }

        Server? resolved = await ResolveAndAuthorizeAsync(user, server, Permissions.ModRemove, cancellationToken).ConfigureAwait(false);
        if (resolved is null)
        {
            return await DenyResolveAsync(user, server, Permissions.ModRemove, cancellationToken).ConfigureAwait(false);
        }

        // Only unused downloads (#290 Leftover: on disk, neither configured nor loaded at the last boot). An item
        // removed since the boot may still be loaded, so it waits for the restart. The Agent re-checks WorkshopItems=.
        ServerModOverview overview = ModChangeSet.Derive(
            server,
            await _states.FindAsync(server, cancellationToken).ConfigureAwait(false),
            await _items.ListForServerAsync(server, cancellationToken).ConfigureAwait(false));
        HashSet<string> leftovers = new(
            overview.Items.Where(i => i.Status is ModChangeStatus.Leftover).Select(i => i.WorkshopId), StringComparer.Ordinal);
        string subject = $"delete {workshopIds.Count} unused download(s)";
        if (workshopIds.FirstOrDefault(id => !leftovers.Contains(id)) is { } inUse)
        {
            await _audit.WriteAsync(
                new AuditEntry(ModAuditActions.DownloadsDeleted, AuditOutcome.Failed, user, resolved.Id, $"{subject}: {inUse} is not unused"),
                cancellationToken).ConfigureAwait(false);
            return ModManagementResult.Denied(
                ModManagementFailure.InvalidInput,
                $"Workshop item {inUse} is still used by the server or waits for a restart, so its files can't be deleted.");
        }

        try
        {
            Operation operation = await _operations.EnqueueAsync(
                new EnqueueOperationRequest(
                    resolved.AgentId, OperationKind.DeleteWorkshopContent, IsMutating: true, Guid.NewGuid().ToString("N"),
                    ServerId: resolved.Id, CommandPayload: new WorkshopContentCommandPayload([.. workshopIds]).ToJson()),
                user,
                cancellationToken).ConfigureAwait(false);

            await _audit.WriteAsync(
                new AuditEntry(
                    ModAuditActions.DownloadsDeleted, AuditOutcome.Succeeded, user, resolved.Id,
                    $"{subject} ({string.Join(", ", workshopIds)}), operation {operation.Id}"),
                cancellationToken).ConfigureAwait(false);

            return ModManagementResult.Success(operation.Id);
        }
        catch (ServerBusyException)
        {
            await _audit.WriteAsync(
                new AuditEntry(ModAuditActions.DownloadsDeleted, AuditOutcome.Failed, user, resolved.Id, $"{subject}: server busy"),
                cancellationToken).ConfigureAwait(false);
            return ModManagementResult.Denied(ModManagementFailure.ServerBusy);
        }
        catch (HostOfflineException)
        {
            await _audit.WriteAsync(
                new AuditEntry(ModAuditActions.DownloadsDeleted, AuditOutcome.Failed, user, resolved.Id, $"{subject}: host offline"),
                cancellationToken).ConfigureAwait(false);
            return ModManagementResult.Denied(ModManagementFailure.HostOffline);
        }
    }

    /// <inheritdoc />
    public async Task<ModManagementResult> SetItemPartsAsync(
        UserId user,
        ServerId server,
        string workshopId,
        IReadOnlyList<string> modIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(modIds);
        if (ValidateWorkshopId(workshopId) is { } invalid)
        {
            return ModManagementResult.Denied(invalid.Failure!.Value, invalid.Message);
        }

        // As for Undo: these tenant-filtered reads only pick the permission; the pipeline authorizes before anything
        // is computed or returned. Turning any part on is an install; only turning parts off is a removal.
        Server? owner = await _servers.FindByIdAsync(server, cancellationToken).ConfigureAwait(false);
        ModInventory? current = owner is null ? null : _inventory.GetLatest(server, owner.AgentId);
        bool turnsOn = current is null || modIds.Any(id => !current.EnabledModIds.Contains(id, StringComparer.Ordinal));
        IReadOnlyList<ServerWorkshopItem> tracked = await _items.ListForServerAsync(server, cancellationToken).ConfigureAwait(false);

        return await ApplyListEditAsync(
            user, server, turnsOn ? Permissions.ModInstall : Permissions.ModRemove, ModAuditActions.PartsSet,
            $"choose {modIds.Count} part(s) of workshop item {workshopId}",
            inventory =>
            {
                PzModId[] chosen = [];
                if (modIds.Count > 0 && ValidateModIds(modIds, out chosen) is { } badModId)
                {
                    return badModId;
                }

                List<PzModId> parts = ItemParts(workshopId, inventory, tracked);
                if (chosen.Any(id => !parts.Contains(id)))
                {
                    return new ListEditOutcome(
                        null, ModManagementFailure.InvalidInput, "Choose only parts this Workshop item provides.");
                }

                return Ok(ModListEditor.SetItemParts(inventory.EnabledModIds, parts, chosen));
            },
            cancellationToken).ConfigureAwait(false);
    }

    // One item's mod ids: mod.info on disk (the truth) plus its description's guesses, validated as PzModIds.
    private static List<PzModId> ItemParts(
        string workshopId, ModInventory inventory, IReadOnlyList<ServerWorkshopItem> tracked)
    {
        IEnumerable<string> onDisk = inventory.InstalledItems
            .Where(i => string.Equals(i.WorkshopId, workshopId, StringComparison.Ordinal))
            .SelectMany(i => i.Mods.Select(m => m.ModId));
        IEnumerable<string> guessed = tracked
            .Where(i => string.Equals(i.WorkshopId, workshopId, StringComparison.Ordinal))
            .SelectMany(i => i.GuessedModIds);

        List<PzModId> parts = [];
        foreach (string id in onDisk.Concat(guessed).Distinct(StringComparer.Ordinal))
        {
            if (PzModId.TryCreate(id, out PzModId valid))
            {
                parts.Add(valid);
            }
        }

        return parts;
    }

    /// <inheritdoc />
    public async Task<ModManagementResult> UndoPendingAsync(
        UserId user, ServerId server, string workshopId, CancellationToken cancellationToken = default)
    {
        if (ValidateWorkshopId(workshopId) is { } invalid)
        {
            return ModManagementResult.Denied(invalid.Failure!.Value, invalid.Message);
        }

        // These tenant-filtered reads only pick which permission to check; nothing is returned or written before the
        // pipeline authorizes it. The item's ids are what it provides on disk plus what its description guessed.
        // Before the first recorded boot there's nothing to undo; that answer is gated on Mod.Install.
        ServerModState? state = await _states.FindAsync(server, cancellationToken).ConfigureAwait(false);
        ServerWorkshopItem? item = (await _items.ListForServerAsync(server, cancellationToken).ConfigureAwait(false))
            .FirstOrDefault(i => string.Equals(i.WorkshopId, workshopId, StringComparison.Ordinal));
        // Stored ids were validated on the way in (#290); they re-enter Mods= only as PzModIds.
        List<PzModId> itemModIds = [];
        foreach (string id in item is null ? [] : item.ObservedModIds.Union(item.GuessedModIds, StringComparer.Ordinal))
        {
            if (PzModId.TryCreate(id, out PzModId valid))
            {
                itemModIds.Add(valid);
            }
        }
        Server? owner = await _servers.FindByIdAsync(server, cancellationToken).ConfigureAwait(false);
        ModInventory? current = owner is null ? null : _inventory.GetLatest(server, owner.AgentId);
        PermissionDefinition permission =
            state is not { HasBootSnapshot: true } || current is null || ReAddsEntries(state, current, workshopId, itemModIds)
                ? Permissions.ModInstall
                : Permissions.ModRemove;

        return await ApplyListEditAsync(
            user, server, permission, ModAuditActions.Undone, $"undo pending change to workshop item {workshopId}",
            inventory => state is { HasBootSnapshot: true }
                ? Ok(ModListEditor.UndoItem(
                    inventory.ConfiguredWorkshopIds, inventory.EnabledModIds,
                    state.BootedWorkshopIds, state.BootedModIds, workshopId, itemModIds))
                : new ListEditOutcome(
                    null, ModManagementFailure.InvalidInput,
                    "There is nothing to undo until the server has booted once with ZWarden watching."),
            cancellationToken).ConfigureAwait(false);
    }

    // Undo re-adds an entry when the item or one of its ids loaded at the last boot but is no longer configured.
    private static bool ReAddsEntries(
        ServerModState state, ModInventory current, string workshopId, IReadOnlyList<PzModId> itemModIds) =>
        (state.BootedWorkshopIds.Contains(workshopId, StringComparer.Ordinal)
            && !current.ConfiguredWorkshopIds.Contains(workshopId, StringComparer.Ordinal))
        || itemModIds.Any(id => state.BootedModIds.Contains(id.Value, StringComparer.Ordinal)
            && !current.EnabledModIds.Contains(id.Value, StringComparer.Ordinal));

    // What each Workshop item is known to provide: mod.info on disk (the truth), else the description's guesses.
    private async Task<IReadOnlyList<InstalledWorkshopItem>> KnownProvidersAsync(
        ServerId server, ModInventory inventory, CancellationToken cancellationToken)
    {
        IReadOnlyList<ServerWorkshopItem> tracked = await _items.ListForServerAsync(server, cancellationToken).ConfigureAwait(false);
        HashSet<string> onDisk = new(inventory.InstalledItems.Select(i => i.WorkshopId), StringComparer.Ordinal);
        return
        [
            .. inventory.InstalledItems,
            .. tracked
                .Where(item => !onDisk.Contains(item.WorkshopId) && item.GuessedModIds.Count > 0)
                .Select(item => new InstalledWorkshopItem(
                    item.WorkshopId, [.. item.GuessedModIds.Select(id => new InstalledMod(id, null))])),
        ];
    }

    /// <inheritdoc />
    public async Task<ModManagementResult> UpdateModsAsync(
        UserId user, ServerId server, GracefulRestartPayload? plan = null, CancellationToken cancellationToken = default)
    {
        // Mod.Update, or Server.Restart: the one Mods-section button is the restart that applies mod changes too.
        Server? resolved = await ResolveAndAuthorizeAsync(user, server, Permissions.ModUpdate, cancellationToken).ConfigureAwait(false)
            ?? await ResolveAndAuthorizeAsync(user, server, Permissions.ServerRestart, cancellationToken).ConfigureAwait(false);
        if (resolved is null)
        {
            // Distinguish not-found from not-authorized for the caller.
            return await DenyResolveAsync(user, server, Permissions.ModUpdate, cancellationToken).ConfigureAwait(false);
        }

        // #292: the bar's countdown is checked here as well as on the Agent, so a bad schedule is never enqueued.
        if (plan is not null
            && (GracefulRestartRules.ValidateSchedule(plan.WarningLeadSeconds)
                ?? GracefulRestartRules.ValidateReason(plan.Reason)) is { } invalid)
        {
            return ModManagementResult.Denied(ModManagementFailure.InvalidInput, invalid);
        }

        try
        {
            // A safe restart (#273): PZ re-fetches WorkshopItems= at boot, so a restart is what pulls a newer Workshop
            // version (confirmed in the field). Not the F17 UpdateServer, which would also install any new game build.
            // No plan ⇒ the Agent warns players on its default schedule (#114) before the save→quit stop. It backs the world
            // up first (#379): a mod update can break a world, and this is the restart that applies one.
            Operation operation = await _operations.EnqueueAsync(
                new EnqueueOperationRequest(
                    resolved.AgentId, OperationKind.RestartServer, IsMutating: true, Guid.NewGuid().ToString("N"),
                    ServerId: resolved.Id,
                    CommandPayload: ((plan ?? GracefulRestartPayload.DefaultSchedule) with { BackupFirst = true }).ToJson()),
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
        catch (HostOfflineException)
        {
            await _audit.WriteAsync(
                new AuditEntry(ModAuditActions.Updated, AuditOutcome.Failed, user, resolved.Id, "host offline"),
                cancellationToken).ConfigureAwait(false);
            return ModManagementResult.Denied(ModManagementFailure.HostOffline);
        }
    }

    // The shared fail-closed pipeline for the list-editing verbs: resolve + authorize, load the ownership-guarded
    // inventory, compute the edits, map a no-op / invalid reorder / validation failure, else enqueue a config-apply.
    private Task<ModManagementResult> ApplyListEditAsync(
        UserId user,
        ServerId server,
        PermissionDefinition permission,
        string auditAction,
        string auditSubject,
        Func<ModInventory, ListEditOutcome> compute,
        CancellationToken cancellationToken) =>
        ApplyListEditAsync(
            user, server, permission, auditAction, auditSubject,
            inventory => Task.FromResult(compute(inventory)), cancellationToken);

    private async Task<ModManagementResult> ApplyListEditAsync(
        UserId user,
        ServerId server,
        PermissionDefinition permission,
        string auditAction,
        string auditSubject,
        Func<ModInventory, Task<ListEditOutcome>> compute,
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

        ListEditOutcome outcome = await compute(inventory).ConfigureAwait(false);
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
            ServerConfigurationFailure.AgentOffline => ModManagementResult.Denied(ModManagementFailure.HostOffline),
            _ => ModManagementResult.Denied(ModManagementFailure.InvalidInput, result.Message),
        };
    }

    private static ListEditOutcome Ok(ModListEditResult result) => new(result, null, null);

    // Every operator-supplied mod id passes PzModId (#290 D3) before ModListEditor sees it, so a separator or
    // control character can never split or corrupt Mods=.
    private static ListEditOutcome? ValidateModIds(IReadOnlyList<string> modIds, out PzModId[] ids)
    {
        ids = [];
        if (modIds.Count == 0)
        {
            return new ListEditOutcome(null, ModManagementFailure.InvalidInput, "No mods were supplied.");
        }

        PzModId[] valid = new PzModId[modIds.Count];
        for (int i = 0; i < modIds.Count; i++)
        {
            if (!PzModId.TryCreate(modIds[i], out valid[i]))
            {
                return new ListEditOutcome(
                    null, ModManagementFailure.InvalidInput,
                    $"A mod id must be 1–{PzModId.MaxLength} characters with no separators (; , =), slashes, quotes or control characters.");
            }
        }

        ids = valid;
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
