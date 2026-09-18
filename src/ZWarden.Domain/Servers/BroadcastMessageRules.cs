using System.Globalization;

namespace ZWarden.Domain.Servers;

/// <summary>
/// The validation and formatting rules for a server-wide broadcast (<c>servermsg "…"</c>) sent before a graceful
/// restart (#114), shared by the Web edge (fast operator feedback) and the Agent (defence in depth, where the
/// command is actually assembled — trust-boundaries.md §8). They mirror <see cref="Players.PlayerCommandRules"/>:
/// PZ's RCON tokenizer strips quotes and splits on whitespace (research §7 quirk 10), so a message is safe only
/// once it is quoted, and quoting is safe only because a message containing a <c>"</c>, a control character, or a
/// non-ASCII byte is rejected outright rather than silently rewritten. That is what makes <c>servermsg</c>
/// injection impossible by construction. The rules are pure and allocation-light so both sides agree exactly.
/// </summary>
public static class BroadcastMessageRules
{
    /// <summary>The maximum accepted broadcast length. A broadcast is a short in-game notice; the bound keeps the
    /// quoted command well under PZ's RCON packet limit.</summary>
    public const int MaxBroadcastMessageLength = 200;

    /// <summary>
    /// Validates a broadcast <paramref name="message"/>. Returns <c>null</c> when it is safe to quote into a
    /// <c>servermsg</c> command, or a short, operator-facing reason why it was rejected. A message may contain
    /// spaces (it is quoted), but is rejected when empty/whitespace, over <see cref="MaxBroadcastMessageLength"/>,
    /// or containing a double-quote, a control character (including newlines), or a non-ASCII character.
    /// </summary>
    public static string? ValidateMessage(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return "A broadcast message is required.";
        }

        if (message.Length > MaxBroadcastMessageLength)
        {
            return $"The broadcast message must be {MaxBroadcastMessageLength} characters or fewer.";
        }

        foreach (char c in message)
        {
            if (c is '"')
            {
                return "The broadcast message must not contain quotation marks.";
            }

            if (char.IsControl(c) || c > '~' || c < ' ')
            {
                return "The broadcast message must contain only printable ASCII characters.";
            }
        }

        return null;
    }

    /// <summary>
    /// Formats the in-game countdown notice broadcast at a given lead-time before the stop — a pure, total function
    /// of the remaining seconds and an optional operator reason, so the exact wire text is unit-tested with no I/O
    /// and both sides agree. The result is always printable ASCII (no em-dashes), so it satisfies
    /// <see cref="ValidateMessage"/> whenever <paramref name="reason"/> does. A whole number of minutes reads as
    /// "N minute(s)"; anything else reads as "N second(s)".
    /// </summary>
    public static string FormatCountdown(int secondsRemaining, string? reason = null)
    {
        string phrase = Humanize(secondsRemaining);
        string notice = $"Server restarting in {phrase}.";
        return string.IsNullOrWhiteSpace(reason) ? notice : $"{notice} {reason.Trim()}";
    }

    private static string Humanize(int seconds)
    {
        if (seconds >= 60 && seconds % 60 == 0)
        {
            int minutes = seconds / 60;
            return minutes == 1 ? "1 minute" : $"{minutes.ToString(CultureInfo.InvariantCulture)} minutes";
        }

        return seconds == 1 ? "1 second" : $"{seconds.ToString(CultureInfo.InvariantCulture)} seconds";
    }
}
