namespace ZWarden.Domain.Configuration;

/// <summary>
/// Which of Project Zomboid's four server configuration files a <see cref="ConfigurationRevision"/> is for.
/// The four files are applied and drift-checked independently (the <c>.ini</c> is live-reloadable; the Lua
/// files are edit-then-restart — ADR 0011), so a revision names exactly one.
/// <para>
/// This is the <b>domain</b> identity of the file, deliberately distinct from <c>ZWarden.PzConfig</c>'s
/// <c>PzConfigKind</c> (which selects a reader): <see cref="ZWarden.Domain"/> takes no dependency on the
/// parser library (it references nothing — ReferenceDirectionTests §9 rule 2), so the persisted revision
/// cannot store the library enum. The two are mapped in the one place that sees both (the apply path,
/// PR 3). Stored by name, so the mapping is additive and never a silent renumber.
/// </para>
/// </summary>
public enum PzConfigFile
{
    /// <summary><c>&lt;name&gt;.ini</c> — the server settings file (live-reloadable).</summary>
    Ini,

    /// <summary><c>&lt;name&gt;_SandboxVars.lua</c> — the sandbox options (edit-then-restart).</summary>
    SandboxVars,

    /// <summary><c>&lt;name&gt;_spawnregions.lua</c> — the spawn regions (edit-then-restart).</summary>
    SpawnRegions,

    /// <summary><c>&lt;name&gt;_spawnpoints.lua</c> — the spawn points (edit-then-restart).</summary>
    SpawnPoints,
}
