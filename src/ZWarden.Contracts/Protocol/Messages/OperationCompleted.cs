using ZWarden.Domain.Configuration;

namespace ZWarden.Contracts.Protocol.Messages;

/// <summary>
/// The terminal report for an Operation (PRD 18). The Operation it concerns is the envelope's
/// <see cref="Envelope{TPayload}.OperationId"/>; F11 turns this into the operation's final state.
/// This reports what the Agent <i>observed</i> — a success here means the command was carried out,
/// not that any higher-level state is now true (trust-boundaries.md §3).
/// </summary>
/// <param name="Outcome">Whether the Operation succeeded or failed.</param>
/// <param name="FailureReason">
/// On failure, an optional <b>untrusted</b> reason (trust-boundaries.md §8); <c>null</c> on success.
/// </param>
/// <param name="Provision">
/// For a successful provisioning Operation (<see cref="CreateServer"/>), the container facts the Agent
/// observed — the allocated ports and the created container id — so ZWarden.Web can record the Server's
/// container linkage. <c>null</c> for every other Operation. Additive and optional (ADR 0020); observed
/// data, recorded only against the Server the envelope's <c>ServerId</c> names, in the current tenant.
/// </param>
/// <param name="Update">
/// For a successful update Operation (<see cref="UpdateServer"/>), the installed build the Agent observed
/// after SteamCMD finished — read from the Steam app manifest. <c>null</c> for every other Operation, and for
/// a failed update. Additive and optional (ADR 0020); observed data, recorded only against the Server the
/// envelope's <c>ServerId</c> names, in the current tenant.
/// </param>
/// <param name="Rcon">
/// For an RCON health probe (<see cref="ProbeRconHealth"/>), the reachability the Agent observed — whether the
/// Server's RCON listener could be reached and authenticated. <c>null</c> for every other Operation. Additive
/// and optional (ADR 0020); carries no secret (never the password) and no free-form runtime output, only the
/// two booleans and an Agent-authored detail. Recorded against the Server the envelope's <c>ServerId</c> names.
/// </param>
/// <param name="Roster">
/// For a successful player enumeration (<see cref="ListPlayers"/>), the connected players the Agent observed
/// over RCON. <c>null</c> for every other Operation. Additive and optional (ADR 0020); the usernames are
/// <b>untrusted</b> PZ output (trust-boundaries.md §8), carried verbatim for escaping at render (F28).
/// </param>
/// <param name="PlayerAction">
/// For a player action (<see cref="KickPlayer"/>, <see cref="BanPlayer"/>, <see cref="UnbanPlayer"/>,
/// <see cref="RemoveFromWhitelist"/>, <see cref="SetWhitelistMode"/>), the outcome the Agent parsed from PZ's
/// reply. <c>null</c> for every other Operation. Additive and optional (ADR 0020); the detail is
/// <b>untrusted</b> PZ output (trust-boundaries.md §8), carried verbatim for escaping at render (F28).
/// </param>
/// <param name="Config">
/// For a successful configuration apply (<see cref="ConfigApply"/>), the revision the Agent recorded — the
/// canonical value snapshot the file holds after the write and its hash — so the control plane can persist a
/// Configuration Revision (F20b, ADR 0011). <c>null</c> for every other Operation, and for a failed or
/// drift-refused apply. Additive and optional (ADR 0020); observed data, recorded only against the Server the
/// envelope's <c>ServerId</c> names.
/// </param>
/// <param name="Mods">
/// For a successful mod discovery (<see cref="DiscoverMods"/>), the Workshop content and mods the Agent observed
/// on disk, mapped against the Server's config lists (F21). <c>null</c> for every other Operation. Additive and
/// optional (ADR 0020); the ids and names are <b>untrusted</b> PZ/Workshop output (trust-boundaries.md §8),
/// carried verbatim for escaping at render. Observed data, recorded only against the Server the envelope's
/// <c>ServerId</c> names.
/// </param>
/// <param name="Backup">
/// For a successful backup Operation (<see cref="BackupServer"/>), the archive the Agent wrote host-side — its
/// relative locator, byte size, and lowercase-hex SHA-256 (F24). <c>null</c> for every other Operation, and for a
/// failed backup. Additive and optional (ADR 0020); observed data, recorded only against the Server the envelope's
/// <c>ServerId</c> names, in the current tenant. Carries no host path and no secret.
/// </param>
/// <param name="BackupDeletion">
/// For a successful backup-deletion Operation (<see cref="DeleteBackup"/>), the signal that the Agent removed the
/// archive (F24), echoing the deleted archive name for the audit trail. <c>null</c> for every other Operation, and
/// for a failed deletion. Additive and optional (ADR 0020); the control plane removes the backup record on seeing
/// it. Carries no host path and no secret.
/// </param>
[ProtocolMessage("operation.completed")]
public sealed record OperationCompleted(
    OperationOutcome Outcome,
    string? FailureReason = null,
    ProvisionResult? Provision = null,
    UpdateResult? Update = null,
    RconHealthResult? Rcon = null,
    PlayerRosterResult? Roster = null,
    PlayerActionResult? PlayerAction = null,
    ConfigApplyResult? Config = null,
    ModDiscoveryResult? Mods = null,
    BackupResult? Backup = null,
    BackupDeletionResult? BackupDeletion = null) : AgentEvent;

/// <summary>The archive a successful <see cref="BackupServer"/> Operation wrote (F24): the compressed
/// <c>.tar.gz</c> of the Server's world tree the Agent produced host-side under its <c>BackupRoot</c>. Carries the
/// <b>relative</b> locator (the archive file name, resolved against the Agent's <c>BackupRoot</c> + <c>ServerId</c>
/// — never an absolute host path), the produced size, and the lowercase-hex SHA-256 over the archive bytes — the
/// integrity value F25 re-verifies before restoring. Non-secret; the Server it belongs to is the completion
/// envelope's <c>ServerId</c>.</summary>
/// <param name="ArchiveName">The archive's file name under <c>&lt;BackupRoot&gt;/&lt;ServerId&gt;/</c> (relative
/// locator; Agent-authored, stored length-bounded).</param>
/// <param name="SizeBytes">The produced archive's size in bytes.</param>
/// <param name="Sha256">The lowercase-hex SHA-256 over the produced archive bytes.</param>
/// <param name="CreatedAt">When the Agent finished writing the archive (UTC).</param>
public sealed record BackupResult(string ArchiveName, long SizeBytes, string Sha256, DateTimeOffset CreatedAt);

/// <summary>The signal a successful <see cref="DeleteBackup"/> Operation sends (F24): the Agent removed the archive
/// from its <c>BackupRoot</c>. Carries the deleted archive name (a bare file name, non-secret) for the audit trail;
/// the backup record is removed by the control plane on seeing this. The Server it belongs to is the completion
/// envelope's <c>ServerId</c>.</summary>
/// <param name="ArchiveName">The archive file name the Agent deleted.</param>
public sealed record BackupDeletionResult(string ArchiveName);

/// <summary>The container facts a successful <see cref="CreateServer"/> Operation observed (F14 PR-B): the two
/// allocated host UDP ports and the created Docker container id. Non-secret; the Server it belongs to is the
/// completion envelope's <c>ServerId</c>.</summary>
/// <param name="GamePort">The allocated game UDP port.</param>
/// <param name="QueryPort">The allocated query/direct-connect UDP port.</param>
/// <param name="ContainerId">The created container's Docker id (observed; untrusted, stored length-bounded).</param>
public sealed record ProvisionResult(int GamePort, int QueryPort, string ContainerId);

/// <summary>The install facts a successful <see cref="UpdateServer"/> Operation observed (F17): the Project
/// Zomboid build now on disk, read from the Steam app manifest (<c>appmanifest_380870.acf</c>) after SteamCMD
/// finished. Non-secret; the Server it belongs to is the completion envelope's <c>ServerId</c>.</summary>
/// <param name="InstalledBuildId">The Steam build id installed (observed; untrusted, stored length-bounded), or
/// <c>null</c> when the manifest could not be read even though the update itself succeeded.</param>
public sealed record UpdateResult(string? InstalledBuildId);

/// <summary>The reachability a <see cref="ProbeRconHealth"/> Operation observed (F18): whether the Server's
/// private RCON listener could be reached over the ZWarden network and whether the Agent-owned password
/// authenticated. Never carries the password or any PZ runtime output — only the two facts and a short
/// Agent-authored detail (e.g. "RCON disabled: empty password", "connection refused", "password rejected",
/// "timed out"). The Server it belongs to is the completion envelope's <c>ServerId</c>.</summary>
/// <param name="Reachable">True when a TCP connection to the Server's RCON port succeeded.</param>
/// <param name="Authenticated">True when the Agent-owned password was accepted; implies
/// <paramref name="Reachable"/>.</param>
/// <param name="Detail">A short, Agent-authored explanation of the outcome, or <c>null</c> when healthy.</param>
public sealed record RconHealthResult(bool Reachable, bool Authenticated, string? Detail);

/// <summary>The players a successful <see cref="ListPlayers"/> Operation observed over RCON (F19), parsed from
/// PZ's <c>players</c> reply (<c>"Players connected (N): "</c> then one <c>-&lt;username&gt;</c> per line). The
/// usernames are <b>untrusted</b> PZ output (trust-boundaries.md §8) — carried verbatim, never interpreted,
/// escaped only at render (F28). The Server they belong to is the completion envelope's <c>ServerId</c>.</summary>
/// <param name="Count">The connected-player count PZ reported in its header line.</param>
/// <param name="Players">The connected usernames, in the order PZ listed them (may be empty).</param>
public sealed record PlayerRosterResult(int Count, IReadOnlyList<string> Players);

/// <summary>How a player action turned out, as the Agent parsed it from PZ's reply (F19). PZ's kick replies are
/// stable strings; ban/unban/remove and the whitelist-mode toggle are prose, parsed with a light heuristic and
/// otherwise reported as <see cref="PlayerActionOutcome.Applied"/> with the raw detail.</summary>
public enum PlayerActionOutcome
{
    /// <summary>The action was carried out (or PZ confirmed it) — e.g. <c>User X kicked.</c>,
    /// <c>Option : Open is now : false</c>.</summary>
    Applied,

    /// <summary>PZ reported the target user does not exist — e.g. <c>User X doesn't exist.</c></summary>
    NotFound,

    /// <summary>PZ refused the action on an existing target — e.g. <c>This user can't be kicked.</c></summary>
    Rejected,

    /// <summary>PZ's reply could not be classified (unexpected or empty). The <see cref="PlayerActionResult.Detail"/>
    /// carries the raw reply for the operator to read.</summary>
    Unknown,
}

/// <summary>The outcome a player action Operation observed (F19: <see cref="KickPlayer"/>,
/// <see cref="BanPlayer"/>, <see cref="UnbanPlayer"/>, <see cref="RemoveFromWhitelist"/>,
/// <see cref="SetWhitelistMode"/>). Never carries a secret; the detail is <b>untrusted</b> PZ output
/// (trust-boundaries.md §8), bounded and carried verbatim for escaping at render (F28). The Server it belongs to
/// is the completion envelope's <c>ServerId</c>.</summary>
/// <param name="Outcome">The classified outcome.</param>
/// <param name="Detail">PZ's reply text (bounded, untrusted), or <c>null</c> when PZ sent no reply.</param>
public sealed record PlayerActionResult(PlayerActionOutcome Outcome, string? Detail);

/// <summary>The revision a successful <see cref="ConfigApply"/> Operation recorded (F20b, ADR 0011): the
/// canonical, order-normalized snapshot of the file's parsed values <b>after</b> the write, its SHA-256 hash
/// (the next write's drift baseline), and how many values the edits actually changed. The control plane persists
/// this as a Configuration Revision against the Server the completion envelope's <c>ServerId</c> names. Never
/// carries file bytes (ADR 0011) or a secret; the snapshot is parsed values the operator authored.</summary>
/// <param name="File">Which of the Server's four configuration files was written.</param>
/// <param name="SnapshotHash">The lowercase-hex SHA-256 of <paramref name="CanonicalSnapshot"/> — the drift
/// baseline the next write re-checks.</param>
/// <param name="CanonicalSnapshot">The order-normalized serialization of the file's scalar values after the
/// write (from <c>PzValueSnapshot</c>) — the "state" the recorded revision holds.</param>
/// <param name="ChangedCount">How many edits the Agent applied to the file.</param>
public sealed record ConfigApplyResult(PzConfigFile File, string SnapshotHash, string CanonicalSnapshot, int ChangedCount);

/// <summary>What a successful <see cref="DiscoverMods"/> Operation observed (F21): the Workshop items installed on
/// disk and the mods each provides, the Server's own <c>WorkshopItems=</c>/<c>Mods=</c> lists as read through the
/// F20a config seam, and the compatibility findings computed by reconciling the three. All ids and names are
/// <b>untrusted</b> PZ/Workshop output (trust-boundaries.md §8) — carried verbatim, never interpreted, escaped only
/// at render. The Server it belongs to is the completion envelope's <c>ServerId</c>. Carries no Steam credential and
/// no external metadata (titles/previews are #110).</summary>
/// <param name="InstalledItems">The Workshop items present under <c>content/108600/</c>, each with the mods its
/// <c>mod.info</c> files declare (the Workshop→Mod mapping; a single item may provide several mods).</param>
/// <param name="ConfiguredWorkshopIds">The Workshop ids the config's <c>WorkshopItems=</c> line references, in file
/// order (may be empty).</param>
/// <param name="EnabledModIds">The Mod ids the config's <c>Mods=</c> line enables, in file order (may be empty).</param>
/// <param name="Findings">The compatibility findings (may be empty when everything reconciles).</param>
public sealed record ModDiscoveryResult(
    IReadOnlyList<DiscoveredWorkshopItem> InstalledItems,
    IReadOnlyList<string> ConfiguredWorkshopIds,
    IReadOnlyList<string> EnabledModIds,
    IReadOnlyList<ModCompatFinding> Findings);

/// <summary>A Workshop item found on disk under <c>content/108600/&lt;WorkshopId&gt;/</c> (F21) and the mods it
/// provides, read from the <c>mod.info</c> under each of its <c>mods/&lt;folder&gt;/</c> subdirectories. The
/// <see cref="WorkshopId"/> is the Steam numeric id as a string; both it and the mod ids/names are untrusted,
/// bounded, and carried verbatim.</summary>
/// <param name="WorkshopId">The Steam Workshop item id (numeric, carried as a string).</param>
/// <param name="Mods">The mods this item provides (may be empty when the item has no readable <c>mod.info</c>).</param>
public sealed record DiscoveredWorkshopItem(string WorkshopId, IReadOnlyList<DiscoveredMod> Mods);

/// <summary>A mod declared by a <c>mod.info</c> (F21): its Mod id (the <c>id=</c> value, the token used in the
/// config's <c>Mods=</c> line) and the display name (<c>name=</c>) when present. Both are untrusted, bounded, and
/// carried verbatim for escaping at render.</summary>
/// <param name="ModId">The PZ Mod id from <c>mod.info</c>'s <c>id=</c>.</param>
/// <param name="Name">The mod's declared display name (<c>name=</c>), or <c>null</c> when it declares none.</param>
public sealed record DiscoveredMod(string ModId, string? Name);

/// <summary>A compatibility problem F21 found by reconciling the on-disk mods against the config lists. All four
/// kinds are derived locally, with no Steam call.</summary>
public enum ModCompatKind
{
    /// <summary>A <c>WorkshopItems=</c> id has no folder under <c>content/108600/</c> — referenced but not
    /// installed (the most common real failure; the server will try to fetch it on next Steam-mode start).</summary>
    ReferencedNotInstalled,

    /// <summary>A <c>Mods=</c> id is provided by no installed <c>mod.info</c> — enabled but missing, so the server
    /// will fail to load it.</summary>
    EnabledButMissing,

    /// <summary>A mod is present on disk but absent from <c>Mods=</c> — installed but inactive (informational:
    /// downloaded, not enabled).</summary>
    InstalledButInactive,

    /// <summary>The same Mod id is provided by more than one installed Workshop item — a load conflict.</summary>
    DuplicateModId,
}

/// <summary>One compatibility finding (F21): its <see cref="Kind"/>, the offending id (a Mod id or Workshop id,
/// untrusted and bounded), and an optional short Agent-authored detail. The Server it belongs to is the completion
/// envelope's <c>ServerId</c>.</summary>
/// <param name="Kind">Which compatibility problem this is.</param>
/// <param name="Subject">The offending id — a Mod id or a Workshop id depending on <paramref name="Kind"/>.</param>
/// <param name="Detail">An optional short explanation authored by the Agent, or <c>null</c>.</param>
public sealed record ModCompatFinding(ModCompatKind Kind, string Subject, string? Detail);
