using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ZWarden.Agent.Configuration;
using ZWarden.Agent.SteamCmd;
using ZWarden.Contracts.Protocol.Messages;
using ZWarden.Domain.Ids;
using ZWarden.PzConfig;
using ZWarden.PzConfig.Model;

namespace ZWarden.Agent.Mods;

/// <summary>
/// Discovers the Workshop content and mods a Server actually has (F21), read-only and offline. It walks the Server's
/// Workshop content subtree on the host install volume, reads the <c>WorkshopItems=</c>/<c>Mods=</c> lists from the
/// live <c>servertest.ini</c> through the F20a config seam, and reconciles the two into the mapping and the
/// compatibility findings. No <c>exec</c> (ADR 0008), no writes, no external network call.
/// </summary>
public interface IModDiscovery
{
    /// <summary>Discovers the mods for <paramref name="serverId"/> and returns the observed result. Resilient: a
    /// missing Workshop tree, an absent or unparseable config, or an unreadable entry yields empty/partial data
    /// rather than throwing.</summary>
    Task<ModDiscoveryResult> DiscoverAsync(ServerId serverId, CancellationToken cancellationToken);
}

/// <inheritdoc cref="IModDiscovery" />
public sealed partial class ModDiscovery : IModDiscovery
{
    private const string ModsDirName = "mods";
    private const string ModInfoFileName = "mod.info";

    // Build 42 mods add version folders (<modFolder>/42/, /42.13/, …) and a common/ folder, each possibly with its
    // own mod.info, alongside the legacy B41 root one (research §6; spike #291).
    private const string Build42FolderPrefix = "42";
    private const string CommonFolderName = "common";

    private readonly IServerInstallPaths _paths;
    private readonly ServerModConfigReader _config;
    private readonly ILogger<ModDiscovery> _logger;

    public ModDiscovery(
        IServerInstallPaths paths,
        IPzConfigParser parser,
        IOptions<AgentOptions> options,
        ILogger<ModDiscovery> logger)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(parser);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _paths = paths;
        _config = new ServerModConfigReader(parser, options);
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ModDiscoveryResult> DiscoverAsync(ServerId serverId, CancellationToken cancellationToken)
    {
        IReadOnlyList<DiscoveredWorkshopItem> installed = await WalkInstalledItemsAsync(serverId, cancellationToken)
            .ConfigureAwait(false);
        (IReadOnlyList<string> configured, IReadOnlyList<string> enabled) =
            await ReadConfigListsAsync(serverId, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<ModCompatFinding> findings = ModCompatAnalyzer.Analyze(installed, configured, enabled);

        LogDiscovered(serverId, installed.Count, findings.Count);
        return new ModDiscoveryResult(installed, configured, enabled, findings);
    }

    // Walks <installVolume>/steamapps/workshop/content/108600/<workshopId>/mods/<folder>/mod.info. Each top-level
    // directory is a Workshop item; each mods/ subdirectory with a readable mod.info is one mod it provides.
    private async Task<IReadOnlyList<DiscoveredWorkshopItem>> WalkInstalledItemsAsync(
        ServerId serverId, CancellationToken cancellationToken)
    {
        string root = _paths.GetWorkshopContentRoot(serverId);
        string[] itemDirs;
        try
        {
            if (!Directory.Exists(root))
            {
                return [];
            }

            itemDirs = Directory.GetDirectories(root);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogWorkshopUnreadable(serverId, ex.Message);
            return [];
        }

        Array.Sort(itemDirs, StringComparer.Ordinal);
        // #275: the version on disk comes from Steam's Workshop manifest, joined on the folders actually present (the
        // manifest keeps entries for deleted folders).
        IReadOnlyDictionary<string, DateTimeOffset> installedTimes = _paths.ReadWorkshopInstalledTimes(serverId);
        List<DiscoveredWorkshopItem> items = [];
        foreach (string itemDir in itemDirs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string workshopId = Path.GetFileName(itemDir);
            items.Add(new DiscoveredWorkshopItem(
                workshopId,
                await ReadItemModsAsync(itemDir, cancellationToken).ConfigureAwait(false),
                installedTimes.TryGetValue(workshopId, out DateTimeOffset installedAt) ? installedAt : null));
        }

        return items;
    }

    private static async Task<IReadOnlyList<DiscoveredMod>> ReadItemModsAsync(string itemDir, CancellationToken cancellationToken)
    {
        string modsDir = Path.Combine(itemDir, ModsDirName);
        string[] modDirs;
        try
        {
            if (!Directory.Exists(modsDir))
            {
                return [];
            }

            modDirs = Directory.GetDirectories(modsDir);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }

        Array.Sort(modDirs, StringComparer.Ordinal);
        List<DiscoveredMod> mods = [];
        foreach (string modDir in modDirs)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // B42 reads a mod from its version folders (42, 42.13, 42.20, …) and common/, and the id may differ from
            // the legacy root file's — More Traits' root says "ToadTraits", its B42 folders "1299328280/ToadTraits",
            // and PZ 42.21 loads only the latter (spike #291). So: the highest version folder with a mod.info, then
            // common/, then the root. (Limitation: we don't know the server's exact build, so a folder newer than it
            // still wins; a B41-only server would want the root file.)
            ModInfo? info = null;
            foreach (string candidate in ModInfoCandidates(modDir))
            {
                info = await ReadModInfoAsync(candidate, cancellationToken).ConfigureAwait(false);
                if (info is not null)
                {
                    break;
                }
            }
            if (info is not null)
            {
                mods.Add(new DiscoveredMod(
                    info.Id, info.Name, info.Version, info.PzVersion, info.VersionMin,
                    info.Requires, info.Incompatible, info.Tags));
            }
        }

        return mods;
    }

    private static async Task<ModInfo?> ReadModInfoAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            // Guard the read itself against a hostile size before pulling the bytes into memory (the reader
            // enforces the same cap defensively).
            var file = new FileInfo(path);
            if (!file.Exists || file.Length > ModInfoReader.MaxBytes)
            {
                return null;
            }

            byte[] bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
            return ModInfoReader.Read(bytes);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    // The mod.info files to try for one mod folder, best first: B42 version folders from the highest version down,
    // then common/, then the legacy root. Unreadable folders just yield fewer candidates.
    private static List<string> ModInfoCandidates(string modDir)
    {
        List<(Version Version, string Dir)> versions = [];
        try
        {
            foreach (string dir in Directory.GetDirectories(modDir))
            {
                string name = Path.GetFileName(dir);
                if (name.StartsWith(Build42FolderPrefix, StringComparison.Ordinal)
                    && Version.TryParse(name.Contains('.', StringComparison.Ordinal) ? name : name + ".0", out Version? version))
                {
                    versions.Add((version, dir));
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Fall through to common/ and the root.
        }

        return
        [
            .. versions.OrderByDescending(v => v.Version).Select(v => Path.Combine(v.Dir, ModInfoFileName)),
            Path.Combine(modDir, CommonFolderName, ModInfoFileName),
            Path.Combine(modDir, ModInfoFileName),
        ];
    }

    // Reads WorkshopItems= and Mods= from the live servertest.ini via the F20a seam. A missing or unparseable file
    // yields empty lists — discovery still reports the on-disk facts.
    private async Task<(IReadOnlyList<string> Configured, IReadOnlyList<string> Enabled)> ReadConfigListsAsync(
        ServerId serverId, CancellationToken cancellationToken)
    {
        ServerModConfig config = await _config.ReadAsync(serverId, cancellationToken).ConfigureAwait(false);
        if (config.Status is ServerModConfigStatus.Unreadable)
        {
            LogConfigUnreadable(serverId, config.Reason ?? "unknown");
        }
        else if (config.Status is ServerModConfigStatus.Unparsed)
        {
            LogConfigUnparsed(serverId);
        }

        return (config.WorkshopIds, config.ModIds);
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Mod discovery for server {ServerId}: {ItemCount} Workshop item(s), {FindingCount} finding(s).")]
    private partial void LogDiscovered(ServerId serverId, int itemCount, int findingCount);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Mod discovery for server {ServerId}: Workshop tree unreadable: {Reason}")]
    private partial void LogWorkshopUnreadable(ServerId serverId, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Mod discovery for server {ServerId}: config unreadable: {Reason}")]
    private partial void LogConfigUnreadable(ServerId serverId, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Mod discovery for server {ServerId}: servertest.ini did not parse; config lists treated as empty.")]
    private partial void LogConfigUnparsed(ServerId serverId);
}
