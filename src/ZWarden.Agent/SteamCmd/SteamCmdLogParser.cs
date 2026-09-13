using System.Globalization;
using System.Text.RegularExpressions;

namespace ZWarden.Agent.SteamCmd;

/// <summary>Whether a SteamCMD update, as seen in the container log, has reached a terminal state yet.</summary>
public enum SteamCmdOutcome
{
    /// <summary>The update session is in flight (or has not started) — no terminal banner seen yet.</summary>
    Pending,

    /// <summary>The entrypoint reported the update finished successfully (stdout-decided, ADR 0009).</summary>
    Succeeded,

    /// <summary>The entrypoint reported the update failed; the existing install is intact (F17).</summary>
    Failed,
}

/// <summary>The latest download/verify progress SteamCMD reported. <see cref="Status"/> is <b>untrusted</b>,
/// SteamCMD-originated text (trust-boundaries.md §8) — escape at render, never treat as an identifier.</summary>
/// <param name="Percent">Progress 0–100, rounded from SteamCMD's fractional figure and clamped.</param>
/// <param name="Status">The state phrase, e.g. <c>downloading</c> or <c>verifying install</c>.</param>
public sealed record SteamCmdProgress(int Percent, string Status);

/// <summary>The parsed view of one update session in a container log: the latest progress, the terminal
/// outcome, and (on failure) an untrusted reason line.</summary>
public sealed record SteamCmdUpdateState(SteamCmdProgress? LatestProgress, SteamCmdOutcome Outcome, string? FailureReason);

/// <summary>
/// Parses a Project Zomboid container's <c>docker logs</c> for one SteamCMD update session (F17). The Agent
/// cannot <c>exec</c> SteamCMD (ADR 0008), so it reads the log the container's entrypoint emits: the run is
/// bracketed by <c>steamcmd update session &lt;id&gt; begin</c> … <c>end (success|failure)</c> banners (F17
/// PR-A), and SteamCMD prints <c>Update state (0x…) &lt;phase&gt;, progress: NN.NN (bytes / total)</c> lines as
/// it works. This is a <b>pure</b> function — it maps log text to a state — so the whole outcome/progress
/// decision is unit-tested with no Docker. Success/failure comes from the entrypoint's authoritative end banner
/// (it already applied the stdout-decided fail-closed check, ADR 0009), not from exit codes.
/// </summary>
public static partial class SteamCmdLogParser
{
    /// <summary>
    /// Parses the update session identified by <paramref name="sessionId"/> out of <paramref name="log"/>. Returns
    /// <see cref="SteamCmdOutcome.Pending"/> with no progress when the session's begin banner is not present yet;
    /// otherwise the latest progress observed within the session window and, once the end banner appears, the
    /// terminal outcome (and, on failure, the first SteamCMD error line as an untrusted reason).
    /// </summary>
    public static SteamCmdUpdateState Parse(string log, string sessionId)
    {
        ArgumentNullException.ThrowIfNull(log);
        ArgumentException.ThrowIfNullOrEmpty(sessionId);

        string beginMarker = $"steamcmd update session {sessionId} begin";
        int beginIdx = log.IndexOf(beginMarker, StringComparison.Ordinal);
        if (beginIdx < 0)
        {
            return new SteamCmdUpdateState(null, SteamCmdOutcome.Pending, null);
        }

        string window = log[(beginIdx + beginMarker.Length)..];

        // The entrypoint's end banner is the authoritative outcome; bound the parse window at it so a later
        // session's or the server's own output can never be mistaken for this update's progress.
        SteamCmdOutcome outcome = SteamCmdOutcome.Pending;
        int endIdx = window.IndexOf($"steamcmd update session {sessionId} end (success)", StringComparison.Ordinal);
        if (endIdx >= 0)
        {
            outcome = SteamCmdOutcome.Succeeded;
            window = window[..endIdx];
        }
        else
        {
            endIdx = window.IndexOf($"steamcmd update session {sessionId} end (failure)", StringComparison.Ordinal);
            if (endIdx >= 0)
            {
                outcome = SteamCmdOutcome.Failed;
                window = window[..endIdx];
            }
        }

        SteamCmdProgress? latest = null;
        string? failureReason = null;
        foreach (string rawLine in window.Split('\n'))
        {
            string line = rawLine.Trim();

            Match progress = ProgressRegex().Match(line);
            if (progress.Success)
            {
                double fraction = double.Parse(progress.Groups["pct"].Value, CultureInfo.InvariantCulture);
                latest = new SteamCmdProgress(Math.Clamp((int)Math.Round(fraction), 0, 100), progress.Groups["status"].Value.Trim());
                continue;
            }

            if (failureReason is null && ErrorRegex().IsMatch(line))
            {
                failureReason = line; // untrusted; the caller length-bounds before persisting (trust §8).
            }
        }

        return new SteamCmdUpdateState(latest, outcome, outcome == SteamCmdOutcome.Failed ? failureReason : null);
    }

    [GeneratedRegex(@"Update state \(0x[0-9A-Fa-f]+\)\s+(?<status>[^,]+),\s+progress:\s+(?<pct>\d+(?:\.\d+)?)", RegexOptions.CultureInvariant)]
    private static partial Regex ProgressRegex();

    [GeneratedRegex(@"^(?:Error!|ERROR!)", RegexOptions.CultureInvariant)]
    private static partial Regex ErrorRegex();
}
