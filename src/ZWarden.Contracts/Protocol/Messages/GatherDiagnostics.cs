namespace ZWarden.Contracts.Protocol.Messages;

/// <summary>
/// A non-mutating, <b>host-level</b> gather of the Agent-side infrastructure diagnostics (F29): Docker
/// connectivity, SteamCMD availability and the installed Project Zomboid build, and host filesystem/disk. Like
/// <see cref="ProbeDockerHealth"/> it carries no payload and names no Server — it probes the host Agent — and,
/// being read-only, never claims the per-server lock (ADR 0022). The Agent runs every host check in one
/// round-trip and reports <see cref="OperationCompleted"/> with a <see cref="HostDiagnosticsResult"/> bundle. It
/// carries no free-form command (trust-boundaries §9).
/// </summary>
[ProtocolMessage("diagnostics.gather-host")]
public sealed record GatherHostDiagnostics : AgentCommand;

/// <summary>
/// A non-mutating, <b>per-server</b> gather of the Agent-side infrastructure diagnostics (F29): RCON
/// reachability, the game/query port, and the Server's filesystem. Like <see cref="ProbeRconHealth"/> it carries
/// no payload — the target Server rides the envelope's <c>ServerId</c> — and, being read-only, never claims the
/// per-server lock (ADR 0022). The Agent runs every server check in one round-trip and reports
/// <see cref="OperationCompleted"/> with a <see cref="ServerDiagnosticsResult"/> bundle. F29 PR-C adds the
/// content checks (mods, config, compatibility) to this same bundle additively. It carries no free-form command.
/// </summary>
[ProtocolMessage("diagnostics.gather-server")]
public sealed record GatherServerDiagnostics : AgentCommand;

/// <summary>
/// The diagnostic domains a gather can report on the wire (F29). A wire twin of the Application's
/// <c>DiagnosticDomain</c> (which the Application layer keeps free of wire types); ZWarden.Web maps this onto the
/// Application enum when it records a completed gather's bundle.
/// </summary>
public enum DiagnosticDomain
{
    /// <summary>The Agent's Docker daemon connectivity.</summary>
    Docker,

    /// <summary>A Server's private RCON listener.</summary>
    Rcon,

    /// <summary>A Server's game/query port reachability.</summary>
    GamePort,

    /// <summary>Filesystem: disk space and mount permissions.</summary>
    Filesystem,

    /// <summary>SteamCMD availability and the installed Project Zomboid build.</summary>
    SteamCmd,

    /// <summary>A Server's mods on disk (F29 PR-C).</summary>
    Mod,

    /// <summary>A Server's configuration files (F29 PR-C).</summary>
    Config,

    /// <summary>Mod/version compatibility against the installed build (F29 PR-C).</summary>
    Compatibility,
}

/// <summary>
/// One check inside a gather bundle (F29): a <see cref="ProbeStatus"/> for a <see cref="DiagnosticDomain"/>, a
/// short Agent-authored <see cref="Summary"/>, and an optional <see cref="Detail"/>. <see cref="Detail"/> is
/// <b>untrusted</b> (trust-boundaries §8) — it may carry text a Server, its mods, or its configuration emitted —
/// carried verbatim and bounded, to be escaped only at render. <see cref="Summary"/> is Agent-authored and safe.
/// </summary>
/// <param name="Domain">Which domain this check belongs to.</param>
/// <param name="Status">The verdict.</param>
/// <param name="Summary">A short, Agent-authored, non-secret one-line summary.</param>
/// <param name="Detail">Optional, untrusted, bounded detail (an error, a value, a name).</param>
public sealed record DiagnosticCheckFact(DiagnosticDomain Domain, ProbeStatus Status, string Summary, string? Detail);

/// <summary>The host-level checks a <see cref="GatherHostDiagnostics"/> Operation observed (F29): Docker,
/// SteamCMD, and host filesystem. The Agent it belongs to is the completion envelope's Agent. Additive and
/// optional on <see cref="OperationCompleted"/> (ADR 0020).</summary>
/// <param name="Checks">The host-domain checks the Agent gathered (a failed probe is a Fail/Warn check, never a
/// thrown gather — one bad domain never sinks the bundle).</param>
public sealed record HostDiagnosticsResult(IReadOnlyList<DiagnosticCheckFact> Checks);

/// <summary>The per-server checks a <see cref="GatherServerDiagnostics"/> Operation observed (F29): RCON, game
/// port, and the Server's filesystem (plus mods/config/compatibility from F29 PR-C). The Server it belongs to is
/// the completion envelope's <c>ServerId</c>. Additive and optional on <see cref="OperationCompleted"/> (ADR
/// 0020).</summary>
/// <param name="Checks">The server-domain checks the Agent gathered.</param>
public sealed record ServerDiagnosticsResult(IReadOnlyList<DiagnosticCheckFact> Checks);
