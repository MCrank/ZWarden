using ZWarden.Domain.Configuration;
using ZWarden.Domain.Ids;
using ZWarden.PzConfig;

namespace ZWarden.Agent.ServerConfig;

/// <summary>
/// The one place that maps a Server's <see cref="PzConfigFile"/> to its on-disk name, its
/// <see cref="PzConfigKind"/>, and its host path under the Agent's <c>DataMountRoot</c>. The path mirrors the
/// container launch (F12/F17): the data volume is <c>&lt;root&gt;/&lt;serverId&gt;</c> (bound at <c>/pz/data</c>)
/// and PZ launches with <c>-servername servertest</c>, so the four files live under
/// <c>&lt;root&gt;/&lt;serverId&gt;/Server/servertest*</c>. Shared by the F20b writer and the F20c reader so the two
/// can never disagree about where a file is.
/// </summary>
internal static class ServerConfigFiles
{
    // ZWarden provisions with the default PZ server name; the four config files share this prefix (see
    // RconServerConfig and the container launch -servername).
    public const string ServerName = "servertest";

    public static string FileName(PzConfigFile file) => file switch
    {
        PzConfigFile.Ini => $"{ServerName}.ini",
        PzConfigFile.SandboxVars => $"{ServerName}_SandboxVars.lua",
        PzConfigFile.SpawnRegions => $"{ServerName}_spawnregions.lua",
        PzConfigFile.SpawnPoints => $"{ServerName}_spawnpoints.lua",
        _ => throw new ArgumentOutOfRangeException(nameof(file), file, "Unknown configuration file."),
    };

    public static PzConfigKind ToKind(PzConfigFile file) => file switch
    {
        PzConfigFile.Ini => PzConfigKind.Ini,
        PzConfigFile.SandboxVars => PzConfigKind.SandboxVars,
        PzConfigFile.SpawnRegions => PzConfigKind.SpawnRegions,
        PzConfigFile.SpawnPoints => PzConfigKind.SpawnPoints,
        _ => throw new ArgumentOutOfRangeException(nameof(file), file, "Unknown configuration file."),
    };

    public static string PathFor(string dataMountRoot, ServerId serverId, PzConfigFile file) =>
        Path.Combine(dataMountRoot, serverId.ToString(), "Server", FileName(file));
}
