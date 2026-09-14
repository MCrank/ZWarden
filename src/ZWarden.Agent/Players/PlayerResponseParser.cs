using System.Text.RegularExpressions;
using ZWarden.Contracts.Protocol.Messages;

namespace ZWarden.Agent.Players;

/// <summary>
/// Parses the <b>untrusted</b> replies PZ returns to player-management commands (F19, trust-boundaries.md §8)
/// into typed results. It matches only PZ's known-stable strings and never interprets the reply as anything but
/// display text; every field carried onward (usernames, detail) is bounded and passed verbatim for escaping at
/// render (F28). Kick replies are stable (<c>User X kicked.</c> / <c>User X doesn't exist.</c> / <c>This user
/// can't be kicked.</c>); ban/unban/remove are prose, classified with a light heuristic and otherwise reported
/// as <see cref="PlayerActionOutcome.Applied"/> with the raw detail; an empty reply is <b>not</b> a fault
/// (research §7 quirk 3) but, for an action, is reported <see cref="PlayerActionOutcome.Unknown"/> because it
/// cannot be confirmed.
/// </summary>
public static partial class PlayerResponseParser
{
    private const int MaxDetailLength = 500;
    private const int MaxRosterEntries = 256;
    private const int MaxUsernameLength = 128;

    [GeneratedRegex(@"Players connected \((\d+)\)", RegexOptions.CultureInvariant)]
    private static partial Regex ConnectedCountRegex();

    /// <summary>Parses a <c>players</c> reply — <c>"Players connected (N): "</c> then one <c>-&lt;username&gt;</c>
    /// per line (<c>\n</c>-separated with a trailing separator over RCON) — into a bounded roster. Tolerant of a
    /// missing header (counts the listed usernames) and of the trailing separator.</summary>
    public static PlayerRosterResult ParseRoster(string response)
    {
        ArgumentNullException.ThrowIfNull(response);

        List<string> players = [];
        foreach (string rawLine in response.Split('\n'))
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line[0] != '-')
            {
                continue;
            }

            string username = line[1..].Trim();
            if (username.Length == 0)
            {
                continue;
            }

            players.Add(username.Length > MaxUsernameLength ? username[..MaxUsernameLength] : username);
            if (players.Count >= MaxRosterEntries)
            {
                break;
            }
        }

        Match match = ConnectedCountRegex().Match(response);
        int count = match.Success && int.TryParse(match.Groups[1].ValueSpan, out int parsed) ? parsed : players.Count;
        return new PlayerRosterResult(count, players);
    }

    /// <summary>Classifies a <c>kickuser</c> reply against PZ's three stable strings.</summary>
    public static PlayerActionResult ParseKick(string response)
    {
        ArgumentNullException.ThrowIfNull(response);

        PlayerActionOutcome outcome =
            Contains(response, "doesn't exist") ? PlayerActionOutcome.NotFound
            : Contains(response, "can't be kicked") ? PlayerActionOutcome.Rejected
            : Contains(response, "kicked") ? PlayerActionOutcome.Applied
            : PlayerActionOutcome.Unknown;
        return new PlayerActionResult(outcome, Detail(response));
    }

    /// <summary>Classifies a prose reply for a ban/unban/remove-from-whitelist action: <c>doesn't exist</c> ⇒
    /// <see cref="PlayerActionOutcome.NotFound"/>; an empty reply ⇒ <see cref="PlayerActionOutcome.Unknown"/>
    /// (cannot be confirmed); any other non-empty reply ⇒ <see cref="PlayerActionOutcome.Applied"/> with the raw
    /// detail.</summary>
    public static PlayerActionResult ParseProseAction(string response)
    {
        ArgumentNullException.ThrowIfNull(response);

        PlayerActionOutcome outcome =
            Contains(response, "doesn't exist") ? PlayerActionOutcome.NotFound
            : string.IsNullOrWhiteSpace(response) ? PlayerActionOutcome.Unknown
            : PlayerActionOutcome.Applied;
        return new PlayerActionResult(outcome, Detail(response));
    }

    /// <summary>Classifies a <c>changeoption Open</c> reply — PZ echoes <c>Option : Open is now : &lt;value&gt;</c>
    /// on success.</summary>
    public static PlayerActionResult ParseWhitelistMode(string response)
    {
        ArgumentNullException.ThrowIfNull(response);

        PlayerActionOutcome outcome = Contains(response, "is now")
            ? PlayerActionOutcome.Applied
            : PlayerActionOutcome.Unknown;
        return new PlayerActionResult(outcome, Detail(response));
    }

    private static bool Contains(string haystack, string needle) =>
        haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);

    private static string? Detail(string response)
    {
        if (string.IsNullOrWhiteSpace(response))
        {
            return null;
        }

        string trimmed = response.Trim();
        return trimmed.Length > MaxDetailLength ? trimmed[..MaxDetailLength] : trimmed;
    }
}
