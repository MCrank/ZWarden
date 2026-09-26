using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using ZWarden.Agent.Configuration;
using ZWarden.Agent.ServerConfig;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Security;

namespace ZWarden.Agent.Rcon;

/// <summary>
/// Owns the RCON section of a Server's <c>servertest.ini</c> on the host (F18 decision D-2/D-5). The Agent runs
/// on the host and owns <c>DataMountRoot</c>, so it sets and reads the RCON password by a surgical write into
/// the config file the container reads - never a container env var (which would leak the secret into
/// <c>docker inspect</c>) and never through ZWarden.Web (trust-boundaries.md §5). The file is the single source
/// of truth for the password: generated once at provision, read back to authenticate.
/// </summary>
public interface IRconServerConfig
{
    /// <summary>
    /// Ensures RCON is enabled for the Server: seeds <c>RCONPort</c> and a strong <c>RCONPassword</c> into
    /// <c>servertest.ini</c> before the container first launches (PZ reads an existing INI and fills only the
    /// keys it is missing, so pre-seeded values survive). Idempotent - a password already present is kept, never
    /// regenerated (PZ captures it as final at init, so a mid-life change would be inert anyway). Creates the
    /// config directory if needed.
    /// </summary>
    void EnsureEnabled(ServerId serverId);

    /// <summary>Reads the Agent-owned RCON password from the Server's <c>servertest.ini</c>, or <c>null</c> when
    /// the file is absent or the password is unset/empty - which is exactly PZ's "RCON disabled" state.</summary>
    SecretString? ReadPassword(ServerId serverId);
}

/// <summary>The default <see cref="IRconServerConfig"/> over the Agent's <c>DataMountRoot</c>. The config path
/// mirrors the container launch (F12/F17): the data volume is <c>&lt;root&gt;/&lt;serverId&gt;</c> (bound at
/// <c>/pz/data</c>), and PZ launches with <c>-cachedir=/pz/data -servername servertest</c>, so it reads/writes
/// <c>/pz/data/Server/servertest.ini</c> - host-side <c>&lt;root&gt;/&lt;serverId&gt;/Server/servertest.ini</c>.</summary>
public sealed class RconServerConfig : IRconServerConfig
{
    /// <summary>The canonical RCON port inside every ZWarden container. Never host-published; reached only over
    /// the private ZWarden network (research §7; PortStrideAllocator never strides it).</summary>
    public const int RconPort = 27015;

    private const string RconPortKey = "RCONPort";
    private const string RconPasswordKey = "RCONPassword";
    private const int GeneratedPasswordLength = 32;

    // INI-safe alphabet: no '=', whitespace, or newline, so the value never breaks the key=value line or the
    // command tokenizer, and no shell metacharacters on the config-write path (an F40 attack target, §8).
    private const string PasswordAlphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";

    private readonly AgentOptions _options;

    public RconServerConfig(IOptions<AgentOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
    }

    /// <inheritdoc />
    public void EnsureEnabled(ServerId serverId)
    {
        string path = ConfigPath(serverId);
        List<string> lines = File.Exists(path) ? [.. File.ReadAllLines(path)] : [];

        string? existing = IniKeyLines.FindValue(lines, RconPasswordKey);
        string password = string.IsNullOrEmpty(existing) ? GeneratePassword() : existing;

        IniKeyLines.SetKey(lines, RconPortKey, RconPort.ToString(System.Globalization.CultureInfo.InvariantCulture));
        IniKeyLines.SetKey(lines, RconPasswordKey, password);

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllLines(path, lines);
    }

    /// <inheritdoc />
    public SecretString? ReadPassword(ServerId serverId)
    {
        string path = ConfigPath(serverId);
        if (!File.Exists(path))
        {
            return null;
        }

        string? value = IniKeyLines.FindValue(File.ReadAllLines(path), RconPasswordKey);
        return string.IsNullOrEmpty(value) ? null : new SecretString(value);
    }

    private string ConfigPath(ServerId serverId) =>
        Path.Combine(_options.DataMountRoot, serverId.ToString(), "Server", "servertest.ini");

    private static string GeneratePassword()
    {
        Span<char> buffer = stackalloc char[GeneratedPasswordLength];
        for (int i = 0; i < buffer.Length; i++)
        {
            buffer[i] = PasswordAlphabet[RandomNumberGenerator.GetInt32(PasswordAlphabet.Length)];
        }

        return new string(buffer);
    }
}
