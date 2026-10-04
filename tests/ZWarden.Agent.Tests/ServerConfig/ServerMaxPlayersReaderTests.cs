using Microsoft.Extensions.Options;
using ZWarden.Agent.Configuration;
using ZWarden.Agent.ServerConfig;
using ZWarden.Domain.Ids;
using ZWarden.PzConfig;

namespace ZWarden.Agent.Tests.ServerConfig;

/// <summary>
/// #337 D1: the Fleet's Players denominator is the live <c>servertest.ini</c>'s <c>MaxPlayers</c>, read on the metrics
/// cadence but parsed only when the file changes. Exercised against a real temp directory.
/// </summary>
public sealed class ServerMaxPlayersReaderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "zwarden-maxp-" + Guid.NewGuid().ToString("N"));
    private readonly ServerId _server = ServerId.New();

    public ServerMaxPlayersReaderTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort temp cleanup.
        }
    }

    private ServerMaxPlayersReader Reader() =>
        new(new PzConfigParser(), Options.Create(new AgentOptions { DataMountRoot = _root }));

    private string WriteIni(string contents)
    {
        string path = Path.Combine(_root, _server.ToString(), "Server", "servertest.ini");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, contents);
        return path;
    }

    [Test]
    public async Task Reads_MaxPlayers_from_the_live_ini()
    {
        WriteIni("PVP=true\nMaxPlayers=16\nPublic=false\n");

        await Assert.That(Reader().Read(_server)).IsEqualTo(16);
    }

    [Test]
    public async Task A_missing_key_is_PZs_default_of_32()
    {
        WriteIni("PVP=true\n");

        await Assert.That(Reader().Read(_server)).IsEqualTo(32);
    }

    [Test]
    public async Task No_ini_yet_is_unknown()
        => await Assert.That(Reader().Read(_server)).IsNull();

    [Test]
    [Arguments("MaxPlayers=lots\n")]
    [Arguments("MaxPlayers=0\n")]
    [Arguments("MaxPlayers=255\n")]
    public async Task A_value_PZ_would_not_accept_is_unknown(string contents)
    {
        WriteIni(contents);

        await Assert.That(Reader().Read(_server)).IsNull();
    }

    [Test]
    public async Task The_value_is_cached_until_the_file_changes()
    {
        string path = WriteIni("MaxPlayers=16\n");
        ServerMaxPlayersReader reader = Reader();
        await Assert.That(reader.Read(_server)).IsEqualTo(16);
        DateTime stamp = File.GetLastWriteTimeUtc(path);

        // Same length, same timestamp: not re-parsed (the cache answers).
        File.WriteAllText(path, "MaxPlayers=24\n");
        File.SetLastWriteTimeUtc(path, stamp);
        await Assert.That(reader.Read(_server)).IsEqualTo(16);

        // A config edit moves the timestamp: the next read picks it up.
        File.SetLastWriteTimeUtc(path, stamp.AddSeconds(5));
        await Assert.That(reader.Read(_server)).IsEqualTo(24);
    }
}
