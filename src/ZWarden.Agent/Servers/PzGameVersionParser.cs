using System.Text.RegularExpressions;

namespace ZWarden.Agent.Servers;

/// <summary>
/// Reads the Project Zomboid game version from the server's boot output (#262). PZ prints it once, early in boot,
/// on a <c>General</c> log line, verified live on B42 42.20.4:
/// <c> LOG  : General      f:0 st:1,802,558,676&gt; version=42.20.4 b0bbce05d5 demo=false</c>. 42.19.2 also prints
/// the build date before <c>demo=</c> (<c>version=42.19.2 99198cd6ef 2026-08-25 14:51:21 (local) demo=false</c>, #258),
/// so up to 120 characters between the version and <c>demo=</c> (a revision, a date) are ignored. Container logs are untrusted, so the parse is total and
/// bounded: only a short <c>General</c> line of that shape yields a version, and the first match wins.
/// </summary>
public static partial class PzGameVersionParser
{
    private const int MaxLineLength = 512;

    /// <summary>The game version (e.g. <c>42.20.4</c>) from the first matching boot line in
    /// <paramref name="log"/>, or <c>null</c> when there is none.</summary>
    public static string? Parse(string log)
    {
        ArgumentNullException.ThrowIfNull(log);
        foreach (string line in log.Split('\n'))
        {
            if (line.Length > MaxLineLength)
            {
                continue;
            }

            Match match = BootVersionRegex().Match(line);
            if (match.Success)
            {
                return match.Groups["version"].Value;
            }
        }

        return null;
    }

    [GeneratedRegex(
        @"^\s*LOG\s*:\s*General\b[^>]*>\s*version=(?<version>\d{1,3}\.\d{1,3}(?:\.\d{1,4})?)(?:\s[^\r\n]{0,120}?)?\s+demo=",
        RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 100)]
    private static partial Regex BootVersionRegex();
}
