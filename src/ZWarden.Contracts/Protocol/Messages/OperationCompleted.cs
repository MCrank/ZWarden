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
[ProtocolMessage("operation.completed")]
public sealed record OperationCompleted(
    OperationOutcome Outcome,
    string? FailureReason = null,
    ProvisionResult? Provision = null,
    UpdateResult? Update = null,
    RconHealthResult? Rcon = null,
    PlayerRosterResult? Roster = null,
    PlayerActionResult? PlayerAction = null,
    ConfigApplyResult? Config = null) : AgentEvent;

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
