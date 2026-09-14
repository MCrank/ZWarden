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
    // ZWarden provisions with the default PZ server name; the config files share this prefix (see ServerConfigWriter
    // and the container launch -servername).
    private const string ServerName = "servertest";
    private const string ConfigDirName = "Server";
    private const string ModsDirName = "mods";
    private const string ModInfoFileName = "mod.info";
    private const string WorkshopItemsKey = "WorkshopItems";
    private const string ModsKey = "Mods";

    private readonly IServerInstallPaths _paths;
    private readonly IPzConfigParser _parser;
    private readonly AgentOptions _options;
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
        _parser = parser;
        _options = options.Value;
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
        List<DiscoveredWorkshopItem> items = [];
        foreach (string itemDir in itemDirs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string workshopId = Path.GetFileName(itemDir);
            items.Add(new DiscoveredWorkshopItem(workshopId, await ReadItemModsAsync(itemDir, cancellationToken).ConfigureAwait(false)));
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
            ModInfo? info = await ReadModInfoAsync(Path.Combine(modDir, ModInfoFileName), cancellationToken).ConfigureAwait(false);
            if (info is not null)
            {
                mods.Add(new DiscoveredMod(info.Id, info.Name));
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

    // Reads WorkshopItems= and Mods= from the live servertest.ini via the F20a seam. A missing or unparseable file
    // yields empty lists — discovery still reports the on-disk facts.
    private async Task<(IReadOnlyList<string> Configured, IReadOnlyList<string> Enabled)> ReadConfigListsAsync(
        ServerId serverId, CancellationToken cancellationToken)
    {
        string path = Path.Combine(_options.DataMountRoot, serverId.ToString(), ConfigDirName, $"{ServerName}.ini");
        byte[] bytes;
        try
        {
            if (!File.Exists(path))
            {
                return ([], []);
            }

            bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogConfigUnreadable(serverId, ex.Message);
            return ([], []);
        }

        PzConfigReadResult read = _parser.Open(PzConfigKind.Ini, bytes);
        if (!read.Parsed || read.Document is not { } document)
        {
            LogConfigUnparsed(serverId);
            return ([], []);
        }

        return (ReadList(document, WorkshopItemsKey), ReadList(document, ModsKey));
    }

    // PZ writes both lists as a single semicolon-separated INI value (research §4). Empty entries are dropped.
    private static IReadOnlyList<string> ReadList(IPzConfigDocument document, string key) =>
        document.TryGetValue(key, out PzValue value) && value is PzString text
            ? [.. text.Value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)]
            : [];

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
