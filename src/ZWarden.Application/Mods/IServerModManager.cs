using ZWarden.Application.Servers;
using ZWarden.Domain.Ids;

namespace ZWarden.Application.Mods;

/// <summary>Why a mod-management action was refused, or that it was a no-op. Fail-closed (ADR 0018): the manager
/// resolves the Server through the tenant filter and authorizes the mapped server-scoped <c>Mod.*</c> permission
/// before computing or enqueuing anything.</summary>
public enum ModManagementFailure
{
    /// <summary>The caller lacks the mapped <c>Mod.*</c> permission on this Server.</summary>
    NotAuthorized,

    /// <summary>No such Server in the current tenant (or not visible to this caller's tenant).</summary>
    ServerNotFound,

    /// <summary>A conflicting mutating Operation is already in flight against this Server (per-server lock,
    /// ADR 0022).</summary>
    ServerBusy,

    /// <summary>The request is not valid to enqueue — an empty request, a malformed Workshop id, or too many
    /// edits to fit one Operation's command payload.</summary>
    InvalidInput,

    /// <summary>No current Mod Inventory has been observed for this Server, so the manager cannot compute the new
    /// list. The operator should refresh discovery (F21) first.</summary>
    SnapshotUnavailable,

    /// <summary>The requested change is a no-op against the currently observed lists (already enabled/disabled, or
    /// an unreferenced Workshop id) — nothing was enqueued.</summary>
    NoChange,

    /// <summary>A reorder whose requested order is not a permutation of the currently enabled set (it would
    /// silently enable or disable a mod).</summary>
    InvalidReorder,
}

/// <summary>The outcome of a mod-management action (F22): on success, the enqueued mutating Operation whose state
/// the caller polls (a config-apply the Agent drift-checks and writes, recording a Configuration Revision; or an
/// update); otherwise a typed failure with an optional operator-facing message.</summary>
/// <param name="Succeeded">Whether an Operation was enqueued.</param>
/// <param name="Operation">The enqueued Operation on success; otherwise <c>null</c>.</param>
/// <param name="Failure">The typed failure or no-op reason when not successful.</param>
/// <param name="Message">An optional operator-facing message.</param>
public sealed record ModManagementResult(
    bool Succeeded, OperationId? Operation, ModManagementFailure? Failure, string? Message = null)
{
    /// <summary>An enqueued Operation.</summary>
    public static ModManagementResult Success(OperationId operation) => new(true, operation, null);

    /// <summary>A refusal or no-op with a typed reason.</summary>
    public static ModManagementResult Denied(ModManagementFailure failure, string? message = null) =>
        new(false, null, failure, message);
}

/// <summary>
/// The operator-facing entry point for mutating a Server's mod set (F22). A mod's desired state <b>is</b> the
/// Server's <c>WorkshopItems=</c> / <c>Mods=</c> config (config-as-truth), so every verb here recomputes those
/// list values from the last observed Mod Inventory (F21) and enqueues an F20b config-apply Operation — mod
/// changes become drift-checked, byte-preserving writes recorded as Configuration Revisions, audited under a
/// <c>Mod.*</c> action. <see cref="UpdateModsAsync"/> instead restarts the Server safely: mods load only on boot, and
/// PZ re-fetches <c>WorkshopItems=</c> then, so the one restart applies the changes and pulls Workshop updates (#273). Fail-closed throughout (ADR 0018).
/// </summary>
public interface IServerModManager
{
    /// <summary>Adds <paramref name="modIds"/> to <c>Mods=</c> (authorizes <c>Mod.Install</c>).</summary>
    Task<ModManagementResult> EnableModsAsync(
        UserId user, ServerId server, IReadOnlyList<string> modIds, CancellationToken cancellationToken = default);

    /// <summary>Removes <paramref name="modIds"/> from <c>Mods=</c> (authorizes <c>Mod.Remove</c>).</summary>
    Task<ModManagementResult> DisableModsAsync(
        UserId user, ServerId server, IReadOnlyList<string> modIds, CancellationToken cancellationToken = default);

    /// <summary>Rewrites <c>Mods=</c> to <paramref name="orderedModIds"/>, which must be a permutation of the
    /// currently enabled set (authorizes <c>Mod.Install</c>).</summary>
    Task<ModManagementResult> ReorderModsAsync(
        UserId user, ServerId server, IReadOnlyList<string> orderedModIds, CancellationToken cancellationToken = default);

    /// <summary>Adds a Workshop item id to <c>WorkshopItems=</c> (authorizes <c>Mod.Install</c>). The provided Mod
    /// ids are unknown until the item downloads, so this does not touch <c>Mods=</c> — the operator enables the
    /// discovered mods in a second step.</summary>
    Task<ModManagementResult> AddWorkshopItemAsync(
        UserId user, ServerId server, string workshopId, CancellationToken cancellationToken = default);

    /// <summary>#291 one-click Install (authorizes <c>Mod.Install</c>): adds <paramref name="workshopIds"/> (the item
    /// plus any dependencies) to <c>WorkshopItems=</c> <b>and</b> <paramref name="modIds"/> to <c>Mods=</c> in one
    /// config apply, so one restart downloads and loads them. <paramref name="modIds"/> may be empty (the description
    /// listed none); each must pass <see cref="ZWarden.Domain.Mods.PzModId"/>.</summary>
    Task<ModManagementResult> InstallWorkshopItemsAsync(
        UserId user,
        ServerId server,
        IReadOnlyList<string> workshopIds,
        IReadOnlyList<string> modIds,
        CancellationToken cancellationToken = default);

    /// <summary>#291 Pick parts: of <paramref name="workshopId"/>'s mods (its <c>mod.info</c> ids on disk plus its
    /// description's guesses), exactly <paramref name="modIds"/> are turned on, in one config apply. An id the item
    /// doesn't provide is <see cref="ModManagementFailure.InvalidInput"/>. Authorizes <c>Mod.Install</c> when it turns
    /// anything on, otherwise <c>Mod.Remove</c>.</summary>
    Task<ModManagementResult> SetItemPartsAsync(
        UserId user,
        ServerId server,
        string workshopId,
        IReadOnlyList<string> modIds,
        CancellationToken cancellationToken = default);

    /// <summary>#291 Undo, before a restart: puts <paramref name="workshopId"/>'s <c>WorkshopItems=</c> entry and its
    /// mod ids back the way the server last booted, in one config apply. An undo that re-adds anything (undoing a
    /// Remove) authorizes <c>Mod.Install</c>; one that only takes entries out (undoing an Install) authorizes
    /// <c>Mod.Remove</c>. <see cref="ModManagementFailure.InvalidInput"/> (behind <c>Mod.Install</c>) before the first
    /// recorded boot;
    /// <see cref="ModManagementFailure.NoChange"/> when nothing is pending for the item.</summary>
    Task<ModManagementResult> UndoPendingAsync(
        UserId user, ServerId server, string workshopId, CancellationToken cancellationToken = default);

    /// <summary>Removes <paramref name="workshopIds"/> from <c>WorkshopItems=</c>, and from <c>Mods=</c> the mods
    /// they exclusively provide — per <c>mod.info</c> on disk, or the description's guesses for an item not yet
    /// downloaded (#291) (authorizes <c>Mod.Remove</c>).</summary>
    Task<ModManagementResult> RemoveWorkshopItemsAsync(
        UserId user, ServerId server, IReadOnlyList<string> workshopIds, CancellationToken cancellationToken = default);

    /// <summary>Restarts the Server safely (player warning, #114) so it loads the mod changes and pulls newer Workshop
    /// versions (#273) — not the F17 game update, so the game build never changes. Authorizes <c>Mod.Update</c> or
    /// <c>Server.Restart</c>. <paramref name="plan"/> (#292) is the countdown chosen in the pending-changes bar, or
    /// <c>null</c> for the Agent's default warning; one <c>GracefulRestartRules</c> rejects is
    /// <see cref="ModManagementFailure.InvalidInput"/>.</summary>
    Task<ModManagementResult> UpdateModsAsync(
        UserId user, ServerId server, GracefulRestartPayload? plan = null, CancellationToken cancellationToken = default);
}
