using Microsoft.Extensions.Options;
using ZWarden.Agent.Configuration;
using ZWarden.Agent.ServerConfig;
using ZWarden.Contracts.Protocol.Messages;
using ZWarden.Domain.Ids;

namespace ZWarden.Agent.Tests.ServerConfig;

/// <summary>
/// #230 D3: the wizard's basic settings are seeded into <c>servertest.ini</c> on the host before PZ's first boot (PZ
/// keeps keys that exist and fills in the rest). Only the chosen keys are written, every other line is preserved, and
/// a value that could break the line is refused before anything is written. Exercised against a real temp directory.
/// </summary>
public class InitialSettingsSeederTests
{
    private static string NewRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), "zwarden-seed-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static InitialSettingsSeeder SeederOver(string root) =>
        new(Options.Create(new AgentOptions { DataMountRoot = root }));

    private static string IniPath(string root, ServerId serverId) =>
        Path.Combine(root, serverId.ToString(), "Server", "servertest.ini");

    [Test]
    public async Task Every_chosen_setting_is_written_as_a_PZ_key()
    {
        string root = NewRoot();
        try
        {
            ServerId server = ServerId.New();

            SeederOver(root).Seed(server, new InitialServerSettings(
                Public: true, PublicName: "Friends of Knox", MaxPlayers: 12, Password: "p@ss word", WelcomeMessage: "Hi <LINE> all"));

            string[] lines = await File.ReadAllLinesAsync(IniPath(root, server));
            await Assert.That(lines).Contains("Public=true");
            await Assert.That(lines).Contains("PublicName=Friends of Knox");
            await Assert.That(lines).Contains("MaxPlayers=12");
            await Assert.That(lines).Contains("Password=p@ss word");
            await Assert.That(lines).Contains("ServerWelcomeMessage=Hi <LINE> all");
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Test]
    public async Task Unchosen_settings_are_left_to_PZ_and_existing_lines_are_preserved()
    {
        string root = NewRoot();
        try
        {
            ServerId server = ServerId.New();
            string path = IniPath(root, server);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllLinesAsync(path, ["RCONPort=27015", "RCONPassword=abc", "Public=true"]);

            SeederOver(root).Seed(server, new InitialServerSettings(Public: false, MaxPlayers: 8));

            string[] lines = await File.ReadAllLinesAsync(path);
            await Assert.That(lines).IsEquivalentTo(["RCONPort=27015", "RCONPassword=abc", "Public=false", "MaxPlayers=8"]);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Test]
    public async Task A_value_that_could_inject_another_key_is_refused_and_nothing_is_written()
    {
        string root = NewRoot();
        try
        {
            ServerId server = ServerId.New();

            await Assert.That(() => SeederOver(root).Seed(server, new InitialServerSettings(
                    PublicName: "ok", WelcomeMessage: "hi\nRCONPassword=pwned")))
                .Throws<ArgumentException>();

            await Assert.That(File.Exists(IniPath(root, server))).IsFalse();
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Test]
    public async Task An_out_of_range_player_cap_is_refused()
    {
        string root = NewRoot();
        try
        {
            await Assert.That(() => SeederOver(root).Seed(ServerId.New(), new InitialServerSettings(MaxPlayers: 0)))
                .Throws<ArgumentException>();
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Test]
    public async Task The_refusal_never_echoes_the_password()
    {
        string root = NewRoot();
        try
        {
            ArgumentException? refused = null;
            try
            {
                SeederOver(root).Seed(ServerId.New(), new InitialServerSettings(Password: "topsecret\n"));
            }
            catch (ArgumentException ex)
            {
                refused = ex;
            }

            await Assert.That(refused).IsNotNull();
            await Assert.That(refused!.Message).DoesNotContain("topsecret");
        }
        finally
        {
            TryDelete(root);
        }
    }

    private static void TryDelete(string root)
    {
        try
        {
            Directory.Delete(root, recursive: true);
        }
        catch (IOException)
        {
            // Best effort — a temp directory.
        }
    }
}
