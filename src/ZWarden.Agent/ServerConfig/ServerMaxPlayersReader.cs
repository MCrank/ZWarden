using System.Globalization;
using Microsoft.Extensions.Options;
using ZWarden.Agent.Configuration;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Servers;
using ZWarden.PzConfig;
using ZWarden.PzConfig.Model;

namespace ZWarden.Agent.ServerConfig;

/// <summary>A Server's configured player cap (#337), read by the metrics sampler for the Fleet's Players column.</summary>
public interface IServerMaxPlayers
{
    /// <summary>
    /// The <c>MaxPlayers</c> the Server's live <c>servertest.ini</c> holds: PZ's default (32) when the key is missing,
    /// <c>null</c> when there is no ini yet or the value is not one PZ accepts (1–254). Never throws.
    /// </summary>
    int? Read(ServerId serverId);

    /// <summary>Forgets every Server not in <paramref name="managed"/> (removed or no longer owned).</summary>
    void Retain(IEnumerable<ServerId> managed);
}

/// <summary>
/// The default <see cref="IServerMaxPlayers"/> (#337). The ini is re-parsed only when its last-write time or length
/// changes, so the per-tick cost is one file stat; a config edit shows on the next sample (possibly before a restart
/// applies it — accepted). Observed, untrusted data: a value outside PZ's own bounds reads as unknown.
/// </summary>
public sealed class ServerMaxPlayersReader : IServerMaxPlayers
{
    /// <summary>The cap PZ writes into a fresh <c>servertest.ini</c> (and uses when the key is missing).</summary>
    internal const int PzDefault = 32;

    private const string Key = "MaxPlayers";

    private readonly IPzConfigParser _parser;
    private readonly AgentOptions _options;
    private readonly Dictionary<ServerId, Entry> _entries = [];
    private readonly Lock _gate = new();

    public ServerMaxPlayersReader(IPzConfigParser parser, IOptions<AgentOptions> options)
    {
        ArgumentNullException.ThrowIfNull(parser);
        ArgumentNullException.ThrowIfNull(options);
        _parser = parser;
        _options = options.Value;
    }

    /// <inheritdoc />
    public int? Read(ServerId serverId)
    {
        FileInfo file = new(Path.Combine(_options.DataMountRoot, serverId.ToString(), "Server", "servertest.ini"));
        try
        {
            if (!file.Exists)
            {
                Forget(serverId);
                return null;
            }

            DateTime stamp = file.LastWriteTimeUtc;
            long length = file.Length;
            lock (_gate)
            {
                if (_entries.TryGetValue(serverId, out Entry? cached) && cached.Stamp == stamp && cached.Length == length)
                {
                    return cached.Value;
                }
            }

            int? value = Parse(File.ReadAllBytes(file.FullName));
            lock (_gate)
            {
                _entries[serverId] = new Entry(stamp, length, value);
            }

            return value;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Mid-write or unreadable this tick: unknown now, re-read on the next sample.
            Forget(serverId);
            return null;
        }
    }

    /// <inheritdoc />
    public void Retain(IEnumerable<ServerId> managed)
    {
        ArgumentNullException.ThrowIfNull(managed);
        HashSet<ServerId> keep = [.. managed];
        lock (_gate)
        {
            foreach (ServerId gone in _entries.Keys.Where(id => !keep.Contains(id)).ToList())
            {
                _entries.Remove(gone);
            }
        }
    }

    private int? Parse(byte[] bytes)
    {
        PzConfigReadResult read = _parser.Open(PzConfigKind.Ini, bytes);
        if (!read.Parsed || read.Document is not { } document)
        {
            return null;
        }

        if (!document.TryGetValue(Key, out PzValue value))
        {
            return PzDefault;
        }

        return value is PzString text
            && int.TryParse(text.Value.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int cap)
            && InitialSettingsRules.ValidateMaxPlayers(cap) is null
                ? cap
                : null;
    }

    private void Forget(ServerId serverId)
    {
        lock (_gate)
        {
            _entries.Remove(serverId);
        }
    }

    private sealed record Entry(DateTime Stamp, long Length, int? Value);
}
