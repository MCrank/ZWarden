using Microsoft.Extensions.Options;
using ZWarden.Agent.Configuration;
using ZWarden.Domain.Ids;
using ZWarden.PzConfig;
using ZWarden.PzConfig.Model;

namespace ZWarden.Agent.Mods;

/// <summary>Whether a Server's mod lists could be read.</summary>
public enum ServerModConfigStatus
{
    /// <summary>The config was read and parsed.</summary>
    Read,

    /// <summary>There is no <c>servertest.ini</c> yet.</summary>
    Absent,

    /// <summary>The file exists but could not be read.</summary>
    Unreadable,

    /// <summary>The file was read but did not parse.</summary>
    Unparsed,
}

/// <summary>A Server's <c>WorkshopItems=</c> and <c>Mods=</c> lists as the live <c>servertest.ini</c> holds them. Both
/// lists are empty unless <see cref="Status"/> is <see cref="ServerModConfigStatus.Read"/>.</summary>
/// <param name="Status">Whether the lists could be read.</param>
/// <param name="WorkshopIds">The <c>WorkshopItems=</c> entries, in file order.</param>
/// <param name="ModIds">The <c>Mods=</c> entries, in load order, with a B42 leading <c>\</c> stripped.</param>
/// <param name="Reason">Why the file could not be read (an I/O message), for the log; otherwise <c>null</c>.</param>
public sealed record ServerModConfig(
    ServerModConfigStatus Status,
    IReadOnlyList<string> WorkshopIds,
    IReadOnlyList<string> ModIds,
    string? Reason = null);

/// <summary>
/// Reads the <c>WorkshopItems=</c>/<c>Mods=</c> lists from a Server's live <c>servertest.ini</c> on the host data
/// volume, through the F20a config seam. Shared by discovery (F21), which treats an unread config as empty, and the
/// Workshop-content remover (#293), which fails closed on it.
/// </summary>
public sealed class ServerModConfigReader
{
    // ZWarden provisions with the default PZ server name; the config files share this prefix (see ServerConfigWriter
    // and the container launch -servername).
    private const string ServerName = "servertest";
    private const string ConfigDirName = "Server";
    private const string WorkshopItemsKey = "WorkshopItems";
    private const string ModsKey = "Mods";

    private readonly IPzConfigParser _parser;
    private readonly AgentOptions _options;

    public ServerModConfigReader(IPzConfigParser parser, IOptions<AgentOptions> options)
    {
        ArgumentNullException.ThrowIfNull(parser);
        ArgumentNullException.ThrowIfNull(options);
        _parser = parser;
        _options = options.Value;
    }

    /// <summary>Reads the lists for <paramref name="serverId"/>. Never throws for a missing, unreadable or unparseable
    /// file; the status says which.</summary>
    public async Task<ServerModConfig> ReadAsync(ServerId serverId, CancellationToken cancellationToken)
    {
        string path = Path.Combine(_options.DataMountRoot, serverId.ToString(), ConfigDirName, $"{ServerName}.ini");
        byte[] bytes;
        try
        {
            if (!File.Exists(path))
            {
                return new ServerModConfig(ServerModConfigStatus.Absent, [], []);
            }

            bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new ServerModConfig(ServerModConfigStatus.Unreadable, [], [], ex.Message);
        }

        PzConfigReadResult read = _parser.Open(PzConfigKind.Ini, bytes);
        if (!read.Parsed || read.Document is not { } document)
        {
            return new ServerModConfig(ServerModConfigStatus.Unparsed, [], []);
        }

        IReadOnlyList<string> enabled = [.. ReadList(document, ModsKey)
            .Select(id => id.StartsWith('\\') ? id[1..].Trim() : id)
            .Where(id => id.Length > 0)];
        return new ServerModConfig(ServerModConfigStatus.Read, ReadList(document, WorkshopItemsKey), enabled);
    }

    // PZ writes both lists as a single semicolon-separated INI value (research §4). Empty entries are dropped.
    // B42 also accepts a "\ModId" entry in Mods= (spike #291, PZ 42.21); the caller strips it to the bare mod.info id.
    private static IReadOnlyList<string> ReadList(IPzConfigDocument document, string key) =>
        document.TryGetValue(key, out PzValue value) && value is PzString text
            ? [.. text.Value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)]
            : [];
}
