using ZWarden.Contracts.Protocol.Messages;

namespace ZWarden.Web.Agents;

/// <summary>
/// The operator-facing line for a completed game update (#273): the Steam build it moved from and to, or that the
/// server was already current. Both ids are Agent-observed from the app manifest, before and after SteamCMD ran — the
/// "before" is not the control plane's stored build, which a metrics report may already have moved to the new one.
/// </summary>
public static class GameUpdateText
{
    /// <summary>The result line for an update, or <see langword="null"/> when the installed build couldn't be read.</summary>
    public static string? Describe(UpdateResult update)
    {
        ArgumentNullException.ThrowIfNull(update);

        if (update.InstalledBuildId is not { } installed)
        {
            return null;
        }

        return update.PreviousBuildId switch
        {
            null => $"Installed Steam build {installed}.",
            { } previous when previous == installed => $"Already on the latest build (Steam build {installed}).",
            { } previous => $"Updated from Steam build {previous} to {installed}.",
        };
    }
}
