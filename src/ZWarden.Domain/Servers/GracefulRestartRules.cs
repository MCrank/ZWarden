namespace ZWarden.Domain.Servers;

/// <summary>
/// The validation rules for a graceful-restart countdown schedule (#114), shared by the Web edge and the Agent.
/// A schedule is a list of <b>lead-times in seconds before the stop</b> at which a <c>servermsg</c> warning fires
/// — e.g. <c>[300, 60, 30, 10]</c> broadcasts at five minutes, one minute, thirty seconds and ten seconds out,
/// then stops. The rules keep a schedule sane and, above all, <b>bounded</b>: because the mutating restart
/// Operation holds the per-server lock (ADR 0022) for the whole countdown, an unbounded schedule would pin the
/// lock indefinitely. An <b>empty</b> schedule is valid and means "skip the broadcast" (restart immediately).
/// Pure and allocation-light so both sides enforce the same contract.
/// </summary>
public static class GracefulRestartRules
{
    /// <summary>The most warning steps a schedule may carry.</summary>
    public const int MaxCountdownSteps = 8;

    /// <summary>The longest lead-time (seconds) a schedule may start at — a 15-minute ceiling on how long the
    /// countdown, and thus the per-server lock, can run.</summary>
    public const int MaxCountdownSeconds = 900;

    /// <summary>The maximum accepted operator reason length appended to each countdown notice.</summary>
    public const int MaxReasonLength = 120;

    /// <summary>
    /// Validates a countdown <paramref name="leadSeconds"/> schedule. Returns <c>null</c> when it is acceptable
    /// (including the empty schedule, which skips the broadcast), or a short, operator-facing reason why it was
    /// rejected. A non-empty schedule must be strictly descending (so distinct and ordered), every lead-time
    /// positive, hold at most <see cref="MaxCountdownSteps"/> steps, and start no earlier than
    /// <see cref="MaxCountdownSeconds"/>.
    /// </summary>
    public static string? ValidateSchedule(IReadOnlyList<int>? leadSeconds)
    {
        if (leadSeconds is null || leadSeconds.Count == 0)
        {
            return null; // An empty schedule is a valid "skip the broadcast".
        }

        if (leadSeconds.Count > MaxCountdownSteps)
        {
            return $"A countdown may have at most {MaxCountdownSteps} steps.";
        }

        if (leadSeconds[0] > MaxCountdownSeconds)
        {
            return $"A countdown may start no earlier than {MaxCountdownSeconds} seconds before the restart.";
        }

        for (int i = 0; i < leadSeconds.Count; i++)
        {
            if (leadSeconds[i] <= 0)
            {
                return "Every countdown lead-time must be a positive number of seconds.";
            }

            if (i > 0 && leadSeconds[i] >= leadSeconds[i - 1])
            {
                return "Countdown lead-times must be strictly descending (e.g. 300, 60, 30, 10).";
            }
        }

        return null;
    }

    /// <summary>
    /// Validates an optional operator <paramref name="reason"/> appended to each countdown notice. A <c>null</c>
    /// reason is valid (none is appended). A supplied reason may contain spaces but is rejected when over
    /// <see cref="MaxReasonLength"/>, or when it fails the printable-ASCII/no-quote hygiene that keeps the formatted
    /// broadcast safe (it is validated through <see cref="BroadcastMessageRules.ValidateMessage"/>).
    /// </summary>
    public static string? ValidateReason(string? reason)
    {
        if (reason is null)
        {
            return null;
        }

        if (reason.Length > MaxReasonLength)
        {
            return $"The reason must be {MaxReasonLength} characters or fewer.";
        }

        // Reuse the message hygiene (no quote, no control, printable ASCII). A whitespace-only reason is meaningless
        // as an appended clause, so it is rejected the same way an empty message is.
        return BroadcastMessageRules.ValidateMessage(reason) is null
            ? null
            : "The reason must contain only printable ASCII characters and no quotation marks.";
    }
}
