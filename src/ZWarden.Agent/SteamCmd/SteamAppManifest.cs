using System.Text.RegularExpressions;

namespace ZWarden.Agent.SteamCmd;

/// <summary>
/// Reads the one fact F17 needs from Steam's app manifest (<c>appmanifest_380870.acf</c>, Valve's KeyValues
/// format): the installed <c>buildid</c>. A pure text function so it is unit-tested without any Steam files;
/// the on-disk read lives in <see cref="ServerInstallPaths"/>.
/// </summary>
public static partial class SteamAppManifest
{
    /// <summary>Extracts the <c>"buildid"</c> value from manifest text, or <c>null</c> when it is absent or the
    /// text does not parse.</summary>
    public static string? ParseBuildId(string manifestContent)
    {
        ArgumentNullException.ThrowIfNull(manifestContent);
        Match match = BuildIdRegex().Match(manifestContent);
        return match.Success ? match.Groups["buildid"].Value : null;
    }

    [GeneratedRegex("\"buildid\"\\s+\"(?<buildid>\\d+)\"", RegexOptions.CultureInvariant)]
    private static partial Regex BuildIdRegex();
}
