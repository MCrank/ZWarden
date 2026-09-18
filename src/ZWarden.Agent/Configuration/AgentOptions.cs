using System.ComponentModel.DataAnnotations;

namespace ZWarden.Agent.Configuration;

/// <summary>
/// The Agent runtime's strongly-typed configuration (F8), bound from the <c>Agent</c> section and
/// validated at startup (<see cref="AgentOptionsValidator"/>, <c>ValidateOnStart</c>). Invalid
/// configuration stops the host before any work begins rather than starting an Agent in an
/// ambiguous state.
/// </summary>
public sealed class AgentOptions
{
    /// <summary>The configuration section these options bind from.</summary>
    public const string SectionName = "Agent";

    /// <summary>
    /// Where the Agent persists its self-identity (F8). Defaults under the per-user local
    /// application-data directory. The identity is a stable <c>agt-</c> id, not a credential —
    /// trust is bound later by enrollment (F9).
    /// </summary>
    [Required]
    public string IdentityFilePath { get; set; } = DefaultIdentityFilePath();

    /// <summary>
    /// The ZWarden.Web control-plane endpoint the Agent will connect to. <b>Declared now for F10;
    /// F8 validates it but does not connect.</b> Must be an absolute <c>https</c>/<c>wss</c> URI.
    /// </summary>
    [Required]
    public string ControlPlaneUri { get; set; } = "https://localhost:8443";

    /// <summary>
    /// Where the Agent persists its <b>trust material</b> once enrolled (F9): the assigned Agent id and the
    /// per-Agent credential. A single local file, never a database (trust-boundaries.md §9 rule 1). Defaults
    /// alongside the identity file.
    /// </summary>
    [Required]
    public string TrustFilePath { get; set; } = DefaultTrustFilePath();

    /// <summary>
    /// The one-time enrollment secret used to enrol on first run (F9). <b>A secret — never logged.</b>
    /// Supplied out of band (env/config) for the bootstrap and consumed once: after enrolment the trust file
    /// exists, so this is ignored on subsequent runs. Optional; when absent the Agent starts un-enrolled.
    /// </summary>
    public string? EnrollmentSecret { get; set; }

    /// <summary>
    /// The Docker Engine endpoint the Agent's Docker runtime connects to (F13). Optional: when unset, the
    /// OS default local endpoint is used (the unix socket on Linux, the named pipe on Windows). Set it to the
    /// socket-proxy address in the reference deployment (ADR 0008). When set, it must be an absolute URI. The
    /// client negotiates the API version over <c>/_ping</c> and never pins a <c>/v1.xx</c> prefix.
    /// </summary>
    public string? DockerEndpoint { get; set; }

    /// <summary>
    /// The pinned canonical PZ image reference used when provisioning a Server's container (F14): a
    /// <c>repo@sha256:…</c> digest in production (ADR 0008 D5), a local tag in dev. Supplied from
    /// configuration, never from the wire. Optional at startup — an Agent that never provisions may leave it
    /// unset — but provisioning fails with an actionable message until it is a real, non-floating reference
    /// (the create-template rejects a floating <c>latest</c>).
    /// </summary>
    public string? PzImageReference { get; set; }

    /// <summary>The named ZWarden Docker network a provisioned container attaches to (F14/PRD 26); never
    /// <c>host</c>/<c>none</c>. Must be non-empty.</summary>
    [Required]
    public string NetworkName { get; set; } = "zwarden";

    /// <summary>The absolute host directory under which each provisioned Server's data directory is created and
    /// bind-mounted at <c>/pz/data</c> (F14/PRD 23). Must be an absolute path.</summary>
    [Required]
    public string DataMountRoot { get; set; } = DefaultDataMountRoot();

    /// <summary>The absolute host directory under which each Server's backup archives are written, one subdirectory
    /// per Server (<c>&lt;BackupRoot&gt;/&lt;serverId&gt;/</c>, F24/ADR 0028). Kept separate from
    /// <see cref="DataMountRoot"/> so an operator can point backups at a different disk than the live world data;
    /// defaults to an Agent-owned sibling of the data root. Must be an absolute path.</summary>
    [Required]
    public string BackupRoot { get; set; } = DefaultBackupRoot();

    /// <summary>The memory limit, in bytes, applied to a provisioned container (F14/PRD 24 resource limits).
    /// Must be positive; defaults to 4 GiB.</summary>
    public long DefaultMemoryLimitBytes { get; set; } = 4L * 1024 * 1024 * 1024;

    /// <summary>
    /// How long (seconds) a Docker <c>stop</c>/<c>restart</c> waits for the container to exit before Docker
    /// SIGKILLs it (F15). This must exceed the image's in-container save grace (<c>ZW_PZ_STOP_GRACE</c>,
    /// default 30s) plus the time to save the world, so the entrypoint's SIGTERM handler completes the console
    /// <c>save</c>→<c>quit</c> over the stdin FIFO before any SIGKILL — a shorter timeout truncates the save and
    /// corrupts the world. Must be positive; defaults to 120. Operators with very large worlds may raise it.
    /// </summary>
    public int StopTimeoutSeconds { get; set; } = 120;

    /// <summary>
    /// The default graceful-restart countdown (#114): the seconds-before-stop at which the Agent broadcasts a
    /// <c>servermsg</c> warning to connected players before a restart-causing Operation takes the server down,
    /// when the command does not carry its own plan. Strictly descending; each restart path (F15/F17/F22) inherits
    /// it, so even an automated restart warns. An <b>empty</b> list disables the default broadcast. Bounded by
    /// <c>GracefulRestartRules</c> (at most 8 steps, no earlier than 900s). Defaults to five minutes, one minute,
    /// thirty seconds and ten seconds out.
    /// </summary>
    public IReadOnlyList<int> RestartWarningLeadSeconds { get; set; } = [300, 60, 30, 10];

    /// <summary>
    /// The default short, printable-ASCII clause appended to each graceful-restart countdown notice (#114), when
    /// the command does not carry its own reason — e.g. "Scheduled maintenance." <c>null</c> (the default) appends
    /// nothing. Bounded and hygiene-checked by <c>GracefulRestartRules</c>.
    /// </summary>
    public string? RestartWarningReason { get; set; }

    /// <summary>How often the Agent will emit a heartbeat once transport lands (F10). Must be positive.</summary>
    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>How often the runtime re-evaluates and logs its liveness tick. Must be positive.</summary>
    public TimeSpan HealthReportInterval { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>How often the Agent samples and reports per-Server runtime metrics — CPU, memory, disk (F16).
    /// Metrics are transient (latest-sample-only on the server), so this is a push cadence, not a retention
    /// window. Must be positive; defaults to 15s.</summary>
    public TimeSpan MetricsReportInterval { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>How long graceful shutdown may take before the host stops forcibly. Must be positive.</summary>
    public TimeSpan ShutdownTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>How often the Agent polls the container log while a SteamCMD update runs (F17), turning SteamCMD's
    /// progress lines into <c>OperationProgress</c>. Each report also extends the operation's lease.</summary>
    public TimeSpan UpdatePollInterval { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>The overall wall-clock budget for one SteamCMD update before the Agent gives up (F17). Sized for a
    /// cold ~6.72 GiB first install; the operation's lease is the control plane's independent safety net.</summary>
    public TimeSpan UpdateTimeout { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>How many trailing log lines a live-log subscription backfills when an operator starts watching a
    /// Server (F27) — the initial tail. Must be positive; defaults to 200.</summary>
    public int LogTailLines { get; set; } = 200;

    /// <summary>The maximum characters a single sanitized log line may carry on the wire (F27); a longer line is cut
    /// and flagged truncated, so one pathological line cannot blow the batch or the browser buffer. Must be
    /// positive; defaults to 2000.</summary>
    public int LogLineMaxCharacters { get; set; } = 2000;

    /// <summary>The per-Server ceiling on log lines the Agent will forward each second while following (F27); lines
    /// beyond it are coalesced away and the batch is flagged <c>Dropped</c>, bounding the stream at the source
    /// (PRD 38) rather than flooding the socket. Must be positive; defaults to 500.</summary>
    public int LogMaxLinesPerSecond { get; set; } = 500;

    /// <summary>How often the Agent flushes accumulated log lines to ZWarden.Web as a <c>ServerLogBatch</c> while
    /// following (F27) — batching avoids a per-line message storm. Must be positive; defaults to 250ms.</summary>
    public TimeSpan LogBatchFlushInterval { get; set; } = TimeSpan.FromMilliseconds(250);

    /// <summary>How long a live-log follow waits before retrying after the container's stream ends or is not yet
    /// present (F27), so watching a stopped Server resumes when it starts and a restart re-attaches. Must be
    /// positive; defaults to 2s.</summary>
    public TimeSpan LogFollowRetryInterval { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>The default identity-file location: <c>%LOCALAPPDATA%/ZWarden/Agent/agent-id.txt</c>.</summary>
    public static string DefaultIdentityFilePath() => DefaultAgentFile("agent-id.txt");

    /// <summary>The default trust-file location: <c>%LOCALAPPDATA%/ZWarden/Agent/agent-trust.json</c>.</summary>
    public static string DefaultTrustFilePath() => DefaultAgentFile("agent-trust.json");

    /// <summary>The default per-Server data root: <c>%LOCALAPPDATA%/ZWarden/Agent/servers</c>.</summary>
    public static string DefaultDataMountRoot() => DefaultAgentFile("servers");

    /// <summary>The default per-Server backup root: <c>%LOCALAPPDATA%/ZWarden/Agent/backups</c> — an Agent-owned
    /// sibling of the data root, so backups do not share the world-data directory (F24).</summary>
    public static string DefaultBackupRoot() => DefaultAgentFile("backups");

    private static string DefaultAgentFile(string fileName) =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ZWarden",
            "Agent",
            fileName);
}
