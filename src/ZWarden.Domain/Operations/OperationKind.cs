namespace ZWarden.Domain.Operations;

/// <summary>
/// What an <see cref="Operation"/> does. Stored by name (never its ordinal), so entries may be reordered
/// but not renumbered. F11 ships only <see cref="DiagnosticsPing"/>; the real mutating kinds
/// (<c>RestartServer</c>, <c>ApplyConfig</c>, <c>Backup</c>, …) land with their owning features (F13/F15/…),
/// each declaring its own mutating-ness at enqueue. Whether an Operation takes the per-server lock is
/// <see cref="Operation.IsMutating"/>, set at enqueue — not derived from this enum — so the lock is
/// testable before any mutating kind exists (ADR 0022).
/// </summary>
public enum OperationKind
{
    /// <summary>A non-mutating round-trip probe of a Server's Agent: dispatch a command and observe the
    /// reply. The first diagnostic tool for "is this Agent actually round-tripping commands right now?".
    /// Being non-mutating, it never claims the per-server lock.</summary>
    DiagnosticsPing = 0,

    /// <summary>A non-mutating, host-level probe of the Agent's Docker connectivity (F13): daemon reachability
    /// and the negotiated API version. It acts on the host Agent, not a Server, so it never claims a per-server
    /// lock (ADR 0022: host-level Operations never contend).</summary>
    DiagnosticsDockerHealth = 1,

    /// <summary>Provision the canonical ZWarden.PZServer container for a registered Server (F14). A
    /// <b>mutating, server-scoped</b> Operation, so it claims the per-server lock (ADR 0022). The Agent builds
    /// the container from F13's closed create-template and allocates the port stride itself, then reports the
    /// allocated ports and container id on completion.</summary>
    ProvisionServer = 2,

    /// <summary>Start a registered Server's canonical container (F15). A <b>mutating, server-scoped</b>
    /// Operation, so it claims the per-server lock (ADR 0022). The Agent resolves the container it owns for the
    /// Server and issues the Docker start verb.</summary>
    StartServer = 3,

    /// <summary>Stop a registered Server's canonical container safely (F15). A <b>mutating, server-scoped</b>
    /// Operation (per-server lock, ADR 0022). The Agent issues a Docker stop with a timeout above the image's
    /// save grace, so the entrypoint's SIGTERM handler runs the console <c>save</c>→<c>quit</c> over the stdin
    /// FIFO — never a bare SIGTERM to the JVM.</summary>
    StopServer = 4,

    /// <summary>Restart a registered Server's canonical container safely (F15). A <b>mutating, server-scoped</b>
    /// Operation (per-server lock, ADR 0022). The Agent issues a Docker restart carrying the same safe stop
    /// timeout as <see cref="StopServer"/>.</summary>
    RestartServer = 5,

    /// <summary>Update (install/validate) a Server's Project Zomboid install via anonymous SteamCMD (F17). A
    /// <b>mutating, server-scoped</b> Operation, so it holds the per-server lock (ADR 0022) for the whole
    /// SteamCMD run — a multi-minute operation kept alive under the reaper by its <c>OperationProgress</c>
    /// reports. Install/update/validate are one SteamCMD verb, so a repair is this same kind run again. The
    /// Agent drives it through the container entrypoint (no <c>exec</c>, ADR 0008) and decides the outcome by
    /// parsing stdout (ADR 0009).</summary>
    UpdateServer = 6,

    /// <summary>Probe a Server's RCON reachability (F18): connect to the private, never-host-published RCON
    /// listener over the ZWarden network and authenticate with the Agent-owned credential. A
    /// <b>non-mutating, server-scoped</b> Operation, so — like the diagnostics pings — it never claims the
    /// per-server lock (ADR 0022) and does not contend with an in-flight lifecycle Operation. The Agent reports
    /// an <c>RconHealthResult</c> (reachable / authenticated / detail) on completion.</summary>
    RconHealthProbe = 7,

    /// <summary>Enumerate a Server's connected players (F19): run PZ's <c>players</c> over RCON and report the
    /// roster. A <b>non-mutating, server-scoped</b> Operation — an RCON passthrough already serialized by the
    /// Agent's single-socket gate (ADR 0026) — so it never claims the per-server lock (ADR 0022).</summary>
    ListPlayers = 8,

    /// <summary>Kick a connected player by account username (F19). A <b>non-mutating, server-scoped</b> Operation
    /// (an RCON passthrough — ADR 0026), so it never claims the per-server lock (ADR 0022) and never contends
    /// with an in-flight lifecycle Operation. The target username rides the Operation's command payload.</summary>
    KickPlayer = 9,

    /// <summary>Ban a player by account username (F19; account-username bans only — ADR 0027). A
    /// <b>non-mutating, server-scoped</b> Operation (ADR 0026), so it never claims the per-server lock (ADR
    /// 0022). The target username rides the command payload.</summary>
    BanPlayer = 10,

    /// <summary>Lift a ban by account username (F19). A <b>non-mutating, server-scoped</b> Operation (ADR 0026),
    /// so it never claims the per-server lock (ADR 0022). The target username rides the command payload.</summary>
    UnbanPlayer = 11,

    /// <summary>Remove a user from a Server's whitelist by account username (F19; removal only — ADR 0012). A
    /// <b>non-mutating, server-scoped</b> Operation (ADR 0026), so it never claims the per-server lock (ADR
    /// 0022). The target username rides the command payload.</summary>
    RemoveFromWhitelist = 12,

    /// <summary>Toggle a Server's whitelist mode — the <c>Open</c> server option (F19). A <b>non-mutating,
    /// server-scoped</b> Operation in the F11 sense (an RCON passthrough — ADR 0026), so it never claims the
    /// per-server lock (ADR 0022); reconciling the INI write against configuration revisions is F20b's concern.
    /// The desired <c>Open</c> value rides the command payload.</summary>
    SetWhitelistMode = 13,

    /// <summary>Apply surgical value edits to one of a Server's four configuration files (F20b). A
    /// <b>mutating, server-scoped</b> Operation — it writes to the <c>/pz/</c> mount — so it claims the
    /// per-server lock (ADR 0022): a config write never runs while a lifecycle Operation is in flight. The Agent
    /// re-parses the live file, fails the write closed on a drift from the recorded baseline (ADR 0011), then
    /// writes byte-preserving, BOM-less, and atomically, reporting a <c>ConfigApplyResult</c> on success. The
    /// target file, drift baseline, and edits ride the command payload.</summary>
    ConfigApply = 14,

    /// <summary>Discover the Workshop content and mods a Server has on disk (F21). A <b>non-mutating,
    /// server-scoped</b> Operation — the Agent only reads the Workshop content subtree and the config lists — so it
    /// never claims the per-server lock (ADR 0022) and does not contend with an in-flight lifecycle Operation. The
    /// Agent reports a <c>ModDiscoveryResult</c> on completion; there is no command payload.</summary>
    ModDiscovery = 15,

    /// <summary>Back up a Server's world data (F24). A <b>mutating, server-scoped</b> Operation, so it claims the
    /// per-server lock (ADR 0022) — a backup never races a lifecycle Operation. The Agent reads the Server's
    /// <c>/pz/data</c> world tree <b>host-side</b> (it owns the bind mount — no <c>exec</c>, no <c>docker cp</c>;
    /// ADR 0008), writes a compressed <c>.tar.gz</c> to its configured <c>BackupRoot</c> excluding the SteamCMD
    /// install and not following the Workshop symlink (ADR 0028), and reports a <c>BackupResult</c> — the archive
    /// locator, byte size, and lowercase-hex SHA-256 — on completion. The <see cref="BackupReason"/> rides the
    /// command payload.</summary>
    Backup = 16,

    /// <summary>Delete a Server's backup archive from the Agent host (F24). A <b>non-mutating, server-scoped</b>
    /// Operation — it removes a backup file under the Agent's <c>BackupRoot</c>, never touching the running Server
    /// or its world — so it never claims the per-server lock (ADR 0022) and does not contend with an in-flight
    /// lifecycle Operation. The target archive name rides the command payload (path-traversal-guarded on the Agent);
    /// the backup record is removed on the Operation's confirmed completion.</summary>
    DeleteBackup = 17,

    /// <summary>Restore a Server's world data from one of its backups (F25). A <b>mutating, server-scoped</b>
    /// Operation, so it claims the per-server lock (ADR 0022) — a restore never races a lifecycle or backup
    /// Operation. The Agent re-verifies the archive against the recorded SHA-256 before unpacking (a corrupt archive
    /// is refused; ADR 0028), <b>refuses if the container is running</b>, takes an inline protective backup of the
    /// current world, then unpacks host-side into a staging tree and atomically swaps it into place — no
    /// <c>exec</c>, no <c>docker cp</c> (ADR 0008/0029). It reports a <c>RestoreResult</c> — the restored archive and
    /// the protective backup it took — on completion. The target backup id, archive name, and checksum ride the
    /// command payload.</summary>
    Restore = 18,

    /// <summary>Run one operator-authored RCON command line from the remote administrative console (F28). A
    /// <b>non-mutating, server-scoped</b> Operation (an RCON passthrough already serialized by the Agent's
    /// single-socket gate — ADR 0026), so it never claims the per-server lock (ADR 0022) and does not contend with
    /// an in-flight lifecycle Operation. Gated by the elevated <c>Console.Execute</c> permission and governed by
    /// the F28 command policy (ADR 0032 — no shell, no credential command, no <c>quit</c>). The command line rides
    /// the Operation's command payload; the Agent reports an (untrusted, bounded) <c>ConsoleCommandResult</c> on
    /// completion.</summary>
    ExecuteConsoleCommand = 19,

    /// <summary>Gather the host-level infrastructure diagnostics from an Agent (F29): Docker connectivity, SteamCMD
    /// availability + installed build, and host filesystem/disk. A <b>non-mutating, host-level</b> Operation — it
    /// only reads — so it never claims the per-server lock (ADR 0022) and carries no <c>ServerId</c>. The Agent runs
    /// every host check in one round-trip and reports a <c>HostDiagnosticsResult</c> bundle on completion.</summary>
    GatherHostDiagnostics = 20,

    /// <summary>Gather the per-server infrastructure diagnostics for a Server (F29): RCON reachability, the
    /// game/query port, and the Server's filesystem (plus mods/config/compatibility in F29 PR-C). A
    /// <b>non-mutating, server-scoped</b> Operation — it only reads — so it never claims the per-server lock (ADR
    /// 0022) and does not contend with an in-flight lifecycle Operation. The target Server rides the envelope
    /// <c>ServerId</c>; the Agent reports a <c>ServerDiagnosticsResult</c> bundle on completion.</summary>
    GatherServerDiagnostics = 21,

    /// <summary>Apply an operator-authored <b>whole-file</b> edit to one of a Server's four configuration files
    /// (F20c PR-D, ADR 0042). A <b>mutating, server-scoped</b> Operation — it writes to the <c>/pz/</c> mount — so
    /// it claims the per-server lock (ADR 0022) exactly like <see cref="ConfigApply"/>. The file text is far larger
    /// than the 2 KB command-payload cap, so it is <b>staged</b> to the Agent over a separate transport channel
    /// before this Operation is enqueued; the command payload carries only the file, the drift baseline, and the
    /// staging correlation id. The Agent retrieves the staged text, parse-validates it, fails the write closed on a
    /// drift from the baseline (ADR 0011), and writes it BOM-less and atomically, reporting the same
    /// <c>ConfigApplyResult</c> a surgical apply does (so it records a revision). The whole-file text never rides
    /// an <c>AgentCommand</c>, keeping the closed-command vocabulary intact.</summary>
    ConfigApplyRaw = 22,

    /// <summary>Recreate a Server's canonical container, preserving its data (#229, ADR 0045): warn + safe stop if
    /// running, remove the container, create it again from the same closed template (optionally on a new host port
    /// pair), and start it if it was running — rolling back to the previous pair on failure. A <b>mutating,
    /// server-scoped</b> Operation, so it claims the per-server lock (ADR 0022). Its command payload carries the
    /// optional new game port and graceful-warning plan; the Agent reports the resulting ports and container id in
    /// the same <c>ProvisionResult</c> provisioning uses.</summary>
    RecreateServer = 23,
}
