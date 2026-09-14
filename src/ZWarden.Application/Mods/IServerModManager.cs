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
/// <c>Mod.*</c> action. <see cref="UpdateModsAsync"/> instead triggers the F17 update that re-downloads Workshop
/// content. Mods load only on boot, so the operator restarts (F15) to apply. Fail-closed throughout (ADR 0018).
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

    /// <summary>Removes <paramref name="workshopIds"/> from <c>WorkshopItems=</c>, and from <c>Mods=</c> the mods
    /// they exclusively provide (authorizes <c>Mod.Remove</c>).</summary>
    Task<ModManagementResult> RemoveWorkshopItemsAsync(
        UserId user, ServerId server, IReadOnlyList<string> workshopIds, CancellationToken cancellationToken = default);

    /// <summary>Triggers the F17 update that re-downloads/validates the Server's Workshop content (authorizes
    /// <c>Mod.Update</c>).</summary>
    Task<ModManagementResult> UpdateModsAsync(
        UserId user, ServerId server, CancellationToken cancellationToken = default);
}
