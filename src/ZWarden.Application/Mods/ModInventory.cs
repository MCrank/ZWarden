using ZWarden.Domain.Ids;

namespace ZWarden.Application.Mods;

/// <summary>
/// The Workshop-and-mod inventory a Server last reported (F21) — the observed answer to a <c>DiscoverMods</c>
/// Operation. Transient display data, like F16's metrics/health and F19's roster: the newest inventory per Server,
/// held in the in-memory <see cref="IModInventoryCache"/> and never persisted. All ids and names are <b>untrusted</b>
/// PZ/Workshop output (trust-boundaries.md §8), carried verbatim for escaping at render. These are Application-side
/// twins of the wire result (mapped at the hub) so Application does not depend on the protocol contracts.
/// </summary>
/// <param name="ServerId">The Server the inventory belongs to.</param>
/// <param name="AgentId">The Agent that reported it — the cache ownership-guard key.</param>
/// <param name="InstalledItems">The Workshop items on disk, each with the mods it provides.</param>
/// <param name="ConfiguredWorkshopIds">The Workshop ids the config's <c>WorkshopItems=</c> references.</param>
/// <param name="EnabledModIds">The Mod ids the config's <c>Mods=</c> enables.</param>
/// <param name="Issues">The compatibility issues found by reconciling disk against config.</param>
/// <param name="ObservedAt">When the Agent observed the inventory (UTC).</param>
public sealed record ModInventory(
    ServerId ServerId,
    AgentId AgentId,
    IReadOnlyList<InstalledWorkshopItem> InstalledItems,
    IReadOnlyList<string> ConfiguredWorkshopIds,
    IReadOnlyList<string> EnabledModIds,
    IReadOnlyList<ModCompatIssue> Issues,
    DateTimeOffset ObservedAt);

/// <summary>A Workshop item present on disk and the mods it provides (the one-to-many Workshop→Mod mapping).</summary>
/// <param name="WorkshopId">The Steam Workshop item id (numeric, as a string).</param>
/// <param name="Mods">The mods this item provides (may be empty).</param>
public sealed record InstalledWorkshopItem(string WorkshopId, IReadOnlyList<InstalledMod> Mods);

/// <summary>A mod declared by a <c>mod.info</c>: its Mod id and optional display name.</summary>
/// <param name="ModId">The PZ Mod id (the token used in <c>Mods=</c>).</param>
/// <param name="Name">The declared display name, or <c>null</c>.</param>
public sealed record InstalledMod(string ModId, string? Name);

/// <summary>A compatibility problem found by reconciling the on-disk mods against the config lists (F21).</summary>
public enum ModCompatIssueKind
{
    /// <summary>A <c>WorkshopItems=</c> id has no folder on disk.</summary>
    ReferencedNotInstalled,

    /// <summary>A <c>Mods=</c> id is provided by no installed mod.</summary>
    EnabledButMissing,

    /// <summary>A mod is on disk but not listed in <c>Mods=</c> (informational).</summary>
    InstalledButInactive,

    /// <summary>The same Mod id is provided by more than one installed Workshop item.</summary>
    DuplicateModId,
}

/// <summary>One compatibility issue: its kind, the offending id (Mod id or Workshop id, untrusted), and an optional
/// detail.</summary>
/// <param name="Kind">Which problem this is.</param>
/// <param name="Subject">The offending id.</param>
/// <param name="Detail">A short explanation, or <c>null</c>.</param>
public sealed record ModCompatIssue(ModCompatIssueKind Kind, string Subject, string? Detail);
