using Microsoft.Extensions.Options;
using ZWarden.Agent.Configuration;
using ZWarden.Agent.Rcon;
using ZWarden.Domain.Ids;

namespace ZWarden.Agent.Tests.Rcon;

/// <summary>
/// F18 decision D-2/D-5: the Agent seeds a strong RCON password into <c>servertest.ini</c> on the host at
/// provision, keeps it stable across re-provisions, preserves any other keys PZ or an operator wrote, reads it
/// back to authenticate, and only writes an INI-safe value. Exercised against a real temp directory.
/// </summary>
public class RconServerConfigTests
{
    private static string NewRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), "zwarden-rcon-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static RconServerConfig ConfigOver(string root) =>
        new(Options.Create(new AgentOptions { DataMountRoot = root }));

    private static string IniPath(string root, ServerId serverId) =>
        Path.Combine(root, serverId.ToString(), "Server", "servertest.ini");

    [Test]
    public async Task EnsureEnabled_seeds_the_rcon_port_and_a_password()
    {
        string root = NewRoot();
        try
        {
            var config = ConfigOver(root);
            ServerId server = ServerId.New();

            config.EnsureEnabled(server);

            string[] lines = await File.ReadAllLinesAsync(IniPath(root, server));
            await Assert.That(lines).Contains("RCONPort=27015");
            string password = lines.Single(l => l.StartsWith("RCONPassword=", StringComparison.Ordinal))["RCONPassword=".Length..];
            await Assert.That(password.Length).IsGreaterThanOrEqualTo(24);
            // INI-safe: no '=', whitespace, or newline that would break the line or the command tokenizer.
            await Assert.That(password.All(c => char.IsLetterOrDigit(c))).IsTrue();
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Test]
    public async Task EnsureEnabled_is_idempotent_and_keeps_the_existing_password()
    {
        string root = NewRoot();
        try
        {
            var config = ConfigOver(root);
            ServerId server = ServerId.New();

            config.EnsureEnabled(server);
            string first = config.ReadPassword(server)!.Value.Reveal();
            config.EnsureEnabled(server);
            string second = config.ReadPassword(server)!.Value.Reveal();

            await Assert.That(second).IsEqualTo(first); // never regenerated
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Test]
    public async Task EnsureEnabled_preserves_other_keys_already_in_the_file()
    {
        string root = NewRoot();
        try
        {
            ServerId server = ServerId.New();
            string path = IniPath(root, server);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllLinesAsync(path, ["PublicName=My Server", "MaxPlayers=16"]);

            ConfigOver(root).EnsureEnabled(server);

            string[] lines = await File.ReadAllLinesAsync(path);
            await Assert.That(lines).Contains("PublicName=My Server");
            await Assert.That(lines).Contains("MaxPlayers=16");
            await Assert.That(lines.Any(l => l.StartsWith("RCONPassword=", StringComparison.Ordinal))).IsTrue();
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Test]
    public async Task EnsureEnabled_replaces_an_empty_password_line()
    {
        string root = NewRoot();
        try
        {
            ServerId server = ServerId.New();
            string path = IniPath(root, server);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllLinesAsync(path, ["RCONPort=27015", "RCONPassword="]);

            ConfigOver(root).EnsureEnabled(server);

            await Assert.That(ConfigOver(root).ReadPassword(server)).IsNotNull();
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Test]
    public async Task ReadPassword_is_null_when_the_file_is_absent()
    {
        string root = NewRoot();
        try
        {
            await Assert.That(ConfigOver(root).ReadPassword(ServerId.New())).IsNull();
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
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
