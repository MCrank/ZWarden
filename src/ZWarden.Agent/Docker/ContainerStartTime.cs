using System.Globalization;

namespace ZWarden.Agent.Docker;

/// <summary>
/// Parses Docker's inspect <c>State.StartedAt</c> (#257), the source of the fleet uptime. Docker writes RFC 3339 with
/// up to nanosecond precision (<c>2026-09-25T14:03:07.123456789Z</c>) and the zero time
/// (<c>0001-01-01T00:00:00Z</c>) for a container that never started. The value is untrusted daemon output, so the
/// parse is total: anything that is not a real start time reads as <c>null</c>.
/// </summary>
public static class ContainerStartTime
{
    /// <summary>The start time in UTC, or <c>null</c> when <paramref name="raw"/> is blank, unparseable or the
    /// zero time.</summary>
    public static DateTimeOffset? Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        string trimmed = TrimToTicks(raw.Trim());
        if (!DateTimeOffset.TryParse(
                trimmed, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset parsed))
        {
            return null;
        }

        DateTimeOffset utc = parsed.ToUniversalTime();
        return utc.Year <= 1 ? null : utc;
    }

    // .NET parses at most seven fractional digits (100ns ticks); Docker emits nine. Cut the fraction to seven so
    // the nanosecond form parses instead of failing.
    private static string TrimToTicks(string raw)
    {
        int dot = raw.IndexOf('.', StringComparison.Ordinal);
        if (dot < 0)
        {
            return raw;
        }

        int end = dot + 1;
        while (end < raw.Length && char.IsAsciiDigit(raw[end]))
        {
            end++;
        }

        int digits = end - dot - 1;
        return digits <= 7 ? raw : string.Concat(raw.AsSpan(0, dot + 8), raw.AsSpan(end));
    }
}
