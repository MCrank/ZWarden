using Microsoft.Extensions.Options;
using ZWarden.Agent.Configuration;
using ZWarden.Domain.Ids;

namespace ZWarden.Agent.SteamCmd;

/// <summary>
/// The host-side files F17 touches for a Server (the Agent runs on the host and owns <c>DataMountRoot</c>): the
/// update control-file it drops for the entrypoint, and the Steam app manifest it reads the installed build id
/// from. Abstracted so the update runner is unit-tested without a real filesystem.
/// </summary>
public interface IServerInstallPaths
{
    /// <summary>Writes the update control-file into the Server's data volume, carrying the OperationId so the
    /// entrypoint's session banner matches what the Agent parses back (F17). Creates the directory if needed.</summary>
    void WriteUpdateRequest(ServerId serverId, OperationId operationId);

    /// <summary>Reads the installed Steam build id from the Server's install volume manifest, or <c>null</c> when
    /// the manifest is absent or unreadable.</summary>
    string? ReadInstalledBuildId(ServerId serverId);

    /// <summary>The host path of the Server's Steam Workshop content root —
    /// <c>&lt;installVolume&gt;/steamapps/workshop/content/108600</c> — under which each downloaded Workshop item
    /// lives as <c>&lt;workshopId&gt;/</c> (F21). The directory may not exist (nothing downloaded yet); callers
    /// treat its absence as "no Workshop content".</summary>
    string GetWorkshopContentRoot(ServerId serverId);

    /// <summary>Reads the Steam <c>timeupdated</c> of each Workshop item installed on the Server's install volume
    /// from <c>steamapps/workshop/appworkshop_108600.acf</c> (#275), keyed by Workshop id. Empty when the manifest is
    /// absent, unreadable, oversized, or doesn't parse. It can still list items whose folders were deleted.</summary>
    IReadOnlyDictionary<string, DateTimeOffset> ReadWorkshopInstalledTimes(ServerId serverId);
}

/// <summary>The default <see cref="IServerInstallPaths"/> over the Agent's <c>DataMountRoot</c>. The paths mirror
/// the create template (F17): the data volume is <c>&lt;root&gt;/&lt;serverId&gt;</c> (bound at <c>/pz/data</c>)
/// and the install volume is the host sibling <c>&lt;root&gt;/&lt;serverId&gt;.server</c> (bound at
/// <c>/pz/server</c>), where SteamCMD writes <c>steamapps/appmanifest_380870.acf</c>.</summary>
public sealed class ServerInstallPaths : IServerInstallPaths
{
    private const string UpdateRequestFile = ".zwarden-update-requested";
    private const string AppManifestFile = "appmanifest_380870.acf";

    // PZ Workshop content is published under the client app id 108600, not the dedicated-server app 380870
    // (research: project-zomboid-runtime.md §4). SteamCMD/the server download items to
    // steamapps/workshop/content/108600/<workshopId>/.
    private const string WorkshopAppId = "108600";
    private const string WorkshopManifestFile = "appworkshop_" + WorkshopAppId + ".acf";
    private const long MaxWorkshopManifestBytes = 4 * 1024 * 1024;
    private static readonly IReadOnlyDictionary<string, DateTimeOffset> EmptyTimes = new Dictionary<string, DateTimeOffset>();

    private readonly AgentOptions _options;

    public ServerInstallPaths(IOptions<AgentOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
    }

    /// <inheritdoc />
    public void WriteUpdateRequest(ServerId serverId, OperationId operationId)
    {
        string dataDir = Path.Combine(_options.DataMountRoot, serverId.ToString());
        Directory.CreateDirectory(dataDir);
        File.WriteAllText(Path.Combine(dataDir, UpdateRequestFile), operationId.ToString());
    }

    /// <inheritdoc />
    public string? ReadInstalledBuildId(ServerId serverId)
    {
        string manifest = Path.Combine(_options.DataMountRoot, $"{serverId}.server", "steamapps", AppManifestFile);
        try
        {
            return File.Exists(manifest) ? SteamAppManifest.ParseBuildId(File.ReadAllText(manifest)) : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public string GetWorkshopContentRoot(ServerId serverId) =>
        Path.Combine(_options.DataMountRoot, $"{serverId}.server", "steamapps", "workshop", "content", WorkshopAppId);

    /// <inheritdoc />
    public IReadOnlyDictionary<string, DateTimeOffset> ReadWorkshopInstalledTimes(ServerId serverId)
    {
        string manifest = Path.Combine(
            _options.DataMountRoot, $"{serverId}.server", "steamapps", "workshop", WorkshopManifestFile);
        try
        {
            // The file is a few hundred bytes per item; anything far larger is not a Steam manifest we trust.
            FileInfo file = new(manifest);
            return file.Exists && file.Length <= MaxWorkshopManifestBytes
                ? SteamWorkshopManifest.ParseInstalledTimeUpdated(File.ReadAllText(manifest))
                : EmptyTimes;
        }
        catch (IOException)
        {
            return EmptyTimes;
        }
        catch (UnauthorizedAccessException)
        {
            return EmptyTimes;
        }
    }
}
