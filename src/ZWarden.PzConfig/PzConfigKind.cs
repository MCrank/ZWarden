namespace ZWarden.PzConfig;

/// <summary>
/// Which of Project Zomboid's four server configuration files a set of bytes is. The kind selects
/// the reader (INI vs. Lua) and the root shape the reader expects, and it keys the validation
/// schema. Measured shapes are in <c>docs/research/pz-lua-config.md</c> §2.
/// </summary>
public enum PzConfigKind
{
    /// <summary><c>&lt;name&gt;.ini</c> — <c># comment</c> / <c>KEY=value</c>, no sections, no nesting.</summary>
    Ini,

    /// <summary><c>&lt;name&gt;_SandboxVars.lua</c> — a global assignment <c>SandboxVars = { … }</c>.</summary>
    SandboxVars,

    /// <summary><c>&lt;name&gt;_spawnregions.lua</c> — <c>function SpawnRegions() return { … } end</c>.</summary>
    SpawnRegions,

    /// <summary><c>&lt;name&gt;_spawnpoints.lua</c> — <c>function SpawnPoints() return { … } end</c>.</summary>
    SpawnPoints,
}
