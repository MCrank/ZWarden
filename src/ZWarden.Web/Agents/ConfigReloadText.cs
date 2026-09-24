using ZWarden.Contracts.Protocol.Messages;
using ZWarden.Domain.Configuration;

namespace ZWarden.Web.Agents;

/// <summary>
/// The operator-facing line for a completed configuration write (#225): whether it is live now or takes effect on the
/// next (re)start. Only the INI reloads live (<c>reloadoptions</c>); sandbox and spawn files always wait for a restart.
/// </summary>
public static class ConfigReloadText
{
    /// <summary>The result line for a write, or <see langword="null"/> when it changed nothing.</summary>
    public static string? Describe(ConfigApplyResult config)
    {
        ArgumentNullException.ThrowIfNull(config);

        if (config.ChangedCount == 0)
        {
            return null;
        }

        return config.Reload switch
        {
            ConfigReloadOutcome.Reloaded => "Applied and reloaded live on the running server.",
            ConfigReloadOutcome.NotRunning => "Applied; the server is not running, so it takes effect when it next starts.",
            ConfigReloadOutcome.Failed => string.IsNullOrWhiteSpace(config.ReloadDetail)
                ? "Applied; takes effect on the next restart (the live reload failed)."
                : $"Applied; takes effect on the next restart (the live reload failed: {config.ReloadDetail})",
            _ => config.File == PzConfigFile.Ini
                ? "Applied; takes effect on the next restart."
                : "Applied; takes effect on the next restart (this file cannot be reloaded live).",
        };
    }
}
