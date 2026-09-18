using System.Text;
using Microsoft.Extensions.Options;
using ZWarden.Agent.Configuration;
using ZWarden.Agent.ServerConfig;
using ZWarden.Contracts.Protocol.Messages;
using ZWarden.Domain.Configuration;
using ZWarden.Domain.Ids;
using ZWarden.PzConfig;
using ZWarden.PzConfig.Revisions;

namespace ZWarden.Agent.Tests.ServerConfig;

/// <summary>
/// F20c (ADR 0041): the Agent-side live read against a real temp directory. The reader parses the live file through
/// the same seam the writer uses and returns a structured view — the current scalar values in wire form, each
/// setting's raw harvested comment, the whole raw text, the canonical drift-baseline hash, and any diagnostics —
/// carrying a missing or unparseable file as a first-class status rather than throwing.
/// </summary>
public class ServerConfigReaderTests
{
    private const string SandboxSrc = """
        SandboxVars = {
            VERSION = 6,
            -- How fast the zombies are.
            -- 1 = Sprinters
            -- 2 = Fast Shamblers
            Zombies = 2,
            XpMultiplier = 1.5,
            Map = {
                AllowMiniMap = false,
            },
        }
        """;

    private static string NewRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), "zwarden-cfgread-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static ServerConfigReader ReaderOver(string root) =>
        new(new PzConfigParser(), Options.Create(new AgentOptions { DataMountRoot = root }));

    private static void Seed(string root, ServerId server, string fileName, string content)
    {
        string path = Path.Combine(root, server.ToString(), "Server", fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static void TryDelete(string root)
    {
        try
        {
            Directory.Delete(root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Test]
    public async Task ReadAsync_returns_current_values_comments_raw_text_and_baseline_hash()
    {
        string root = NewRoot();
        try
        {
            ServerId server = ServerId.New();
            Seed(root, server, "servertest_SandboxVars.lua", SandboxSrc);

            ConfigReadPayload payload = await ReaderOver(root).ReadAsync(
                server, PzConfigFile.SandboxVars, CancellationToken.None);

            await Assert.That(payload.Status).IsEqualTo(ConfigReadStatus.Read);
            await Assert.That(payload.RawText).IsEqualTo(SandboxSrc);

            ConfigSettingValue zombies = payload.Settings.Single(s => s.Path == "Zombies");
            await Assert.That(zombies.Kind).IsEqualTo(ConfigValueKind.Number);
            await Assert.That(zombies.Value).IsEqualTo("2");
            // The harvested leading comment is the setting's raw tooltip block, markers stripped, lines joined.
            await Assert.That(zombies.Comment).IsNotNull();
            await Assert.That(zombies.Comment!).Contains("Fast Shamblers");

            ConfigSettingValue miniMap = payload.Settings.Single(s => s.Path == "Map.AllowMiniMap");
            await Assert.That(miniMap.Kind).IsEqualTo(ConfigValueKind.Bool);
            await Assert.That(miniMap.Value).IsEqualTo("false");

            // The reported hash is the drift baseline: it matches a snapshot of the same parsed file.
            string expected = PzValueSnapshot.Of(
                new PzConfigParser().Open(PzConfigKind.SandboxVars, Encoding.UTF8.GetBytes(SandboxSrc)).Document!).Hash;
            await Assert.That(payload.BaselineHash).IsEqualTo(expected);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Test]
    public async Task ReadAsync_reports_a_missing_file_as_a_status_not_an_exception()
    {
        string root = NewRoot();
        try
        {
            ConfigReadPayload payload = await ReaderOver(root).ReadAsync(
                ServerId.New(), PzConfigFile.SandboxVars, CancellationToken.None);

            await Assert.That(payload.Status).IsEqualTo(ConfigReadStatus.FileMissing);
            await Assert.That(payload.Settings).IsEmpty();
            await Assert.That(payload.RawText).IsEmpty();
            await Assert.That(payload.BaselineHash).IsNull();
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Test]
    public async Task ReadAsync_reports_a_parse_failure_with_the_raw_text_and_diagnostics()
    {
        string root = NewRoot();
        try
        {
            ServerId server = ServerId.New();
            const string broken = "SandboxVars = {\n    Zombies = ,\n"; // syntactically invalid, unclosed
            Seed(root, server, "servertest_SandboxVars.lua", broken);

            ConfigReadPayload payload = await ReaderOver(root).ReadAsync(
                server, PzConfigFile.SandboxVars, CancellationToken.None);

            await Assert.That(payload.Status).IsEqualTo(ConfigReadStatus.ParseFailed);
            await Assert.That(payload.Settings).IsEmpty();
            await Assert.That(payload.RawText).IsEqualTo(broken); // still returned for the raw view
            await Assert.That(payload.BaselineHash).IsNull();
            await Assert.That(payload.Diagnostics).IsNotEmpty();
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Test]
    public async Task ReadAsync_reads_the_ini_files_key_values()
    {
        string root = NewRoot();
        try
        {
            ServerId server = ServerId.New();
            Seed(root, server, "servertest.ini", "PublicName=My Server\nMaxPlayers=16\n");

            ConfigReadPayload payload = await ReaderOver(root).ReadAsync(
                server, PzConfigFile.Ini, CancellationToken.None);

            await Assert.That(payload.Status).IsEqualTo(ConfigReadStatus.Read);
            ConfigSettingValue name = payload.Settings.Single(s => s.Path == "PublicName");
            await Assert.That(name.Kind).IsEqualTo(ConfigValueKind.Text);
            await Assert.That(name.Value).IsEqualTo("My Server");
        }
        finally
        {
            TryDelete(root);
        }
    }
}
