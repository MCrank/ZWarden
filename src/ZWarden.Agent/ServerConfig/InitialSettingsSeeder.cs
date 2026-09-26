using System.Globalization;
using Microsoft.Extensions.Options;
using ZWarden.Agent.Configuration;
using ZWarden.Contracts.Protocol.Messages;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Servers;

namespace ZWarden.Agent.ServerConfig;

/// <summary>
/// Seeds the new-server wizard's basic settings into a Server's <c>servertest.ini</c> on the host before PZ's first
/// boot (#230 D3), the same surgical host-side write as the F18 RCON seed: PZ reads an existing INI and fills in only
/// the keys it is missing, so the seeded values survive. No env var, no exec, no restart.
/// </summary>
public interface IInitialSettingsSeeder
{
    /// <summary>
    /// Writes each chosen (non-null) setting as its PZ key, preserving every other line. Validates every value first
    /// (<see cref="InitialSettingsRules"/>) and throws <see cref="ArgumentException"/> — writing nothing — when one is
    /// refused. The message never echoes the password.
    /// </summary>
    void Seed(ServerId serverId, InitialServerSettings settings);
}

/// <summary>The default <see cref="IInitialSettingsSeeder"/> over the Agent's <c>DataMountRoot</c> — the same config
/// path as <see cref="Rcon.RconServerConfig"/>: <c>&lt;root&gt;/&lt;serverId&gt;/Server/servertest.ini</c>.</summary>
public sealed class InitialSettingsSeeder : IInitialSettingsSeeder
{
    private readonly AgentOptions _options;

    public InitialSettingsSeeder(IOptions<AgentOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
    }

    /// <summary>The first reason <paramref name="settings"/> would be refused, or <c>null</c> when every value is
    /// acceptable. Never echoes the password.</summary>
    public static string? Validate(InitialServerSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return (settings.MaxPlayers is { } players ? InitialSettingsRules.ValidateMaxPlayers(players) : null)
            ?? InitialSettingsRules.ValidatePublicName(settings.PublicName)
            ?? InitialSettingsRules.ValidatePassword(settings.Password)
            ?? InitialSettingsRules.ValidateWelcomeMessage(settings.WelcomeMessage);
    }

    /// <inheritdoc />
    public void Seed(ServerId serverId, InitialServerSettings settings)
    {
        if (Validate(settings) is { } refusal)
        {
            throw new ArgumentException(refusal, nameof(settings));
        }

        string path = Path.Combine(_options.DataMountRoot, serverId.ToString(), "Server", "servertest.ini");
        List<string> lines = File.Exists(path) ? [.. File.ReadAllLines(path)] : [];

        SetIfChosen(lines, "Public", settings.Public is { } listed ? (listed ? "true" : "false") : null);
        SetIfChosen(lines, "PublicName", settings.PublicName);
        SetIfChosen(lines, "MaxPlayers", settings.MaxPlayers?.ToString(CultureInfo.InvariantCulture));
        SetIfChosen(lines, "Password", settings.Password);
        SetIfChosen(lines, "ServerWelcomeMessage", settings.WelcomeMessage);

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllLines(path, lines);
    }

    private static void SetIfChosen(List<string> lines, string key, string? value)
    {
        if (value is not null)
        {
            IniKeyLines.SetKey(lines, key, value);
        }
    }
}
