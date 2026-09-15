namespace ZWarden.Application.Diagnostics;

/// <summary>
/// The ten diagnostic domains F29 sweeps (scope §6; PRD 50). A run produces one or more
/// <see cref="DiagnosticCheck"/>s per domain. Some domains evaluate in-process on the Web host
/// (<see cref="Web"/>, <see cref="Database"/>, <see cref="Tls"/>, <see cref="Agent"/> connectivity); the rest
/// are gathered read-only from the owning Agent (F29 PR-B/PR-C).
/// </summary>
public enum DiagnosticDomain
{
    /// <summary>The ZWarden.Web host itself is responding (base health).</summary>
    Web,

    /// <summary>The application database: reachable, schema current.</summary>
    Database,

    /// <summary>The Agents: enrolled, connected, recently seen.</summary>
    Agent,

    /// <summary>The Agent's Docker daemon connectivity.</summary>
    Docker,

    /// <summary>A Server's private RCON listener: reachable and authenticated.</summary>
    Rcon,

    /// <summary>A Server's game/query port reachability.</summary>
    GamePort,

    /// <summary>Filesystem: disk space and the Server's mount permissions.</summary>
    Filesystem,

    /// <summary>SteamCMD availability and the installed Project Zomboid build.</summary>
    SteamCmd,

    /// <summary>A Server's mods on disk parse and resolve.</summary>
    Mod,

    /// <summary>A Server's configuration files parse and are schema-valid.</summary>
    Config,

    /// <summary>The public endpoint's serving TLS certificate (validity, hostname, expiry).</summary>
    Tls,

    /// <summary>Mod/version compatibility against the installed Project Zomboid build.</summary>
    Compatibility,
}
