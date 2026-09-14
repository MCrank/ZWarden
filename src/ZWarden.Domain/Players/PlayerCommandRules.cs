namespace ZWarden.Domain.Players;

/// <summary>
/// The validation rules for the arguments of a player-management RCON command (F19), shared by the Web edge
/// (fast operator feedback before an Operation is enqueued) and the Agent (defence in depth, where the command
/// string is actually assembled — trust-boundaries.md §8). They exist because PZ echoes an operator-supplied
/// username verbatim into a command whose tokenizer strips quotes and splits on whitespace (research §7 quirk
/// 10): an unquoted space reparses the command, an embedded <c>"</c> breaks the quoting, a control character
/// desyncs framing, and a leading <c>-</c> is read as a flag. Rejecting those inputs — rather than silently
/// rewriting them — is what makes RCON command injection impossible by construction (D-3). The rules are pure
/// and allocation-light so both sides enforce exactly the same contract.
/// </summary>
public static class PlayerCommandRules
{
    /// <summary>The maximum accepted username length. PZ account usernames are short single tokens; the bound
    /// caps the command size an operator can push through RCON.</summary>
    public const int MaxUsernameLength = 64;

    /// <summary>The maximum accepted kick/ban reason length. The reason is quoted into <c>-r</c>; the bound keeps
    /// the command well under PZ's packet limits.</summary>
    public const int MaxReasonLength = 200;

    /// <summary>
    /// Validates a player account <paramref name="username"/>. Returns <c>null</c> when it is safe to quote into a
    /// command, or a short, operator-facing reason why it was rejected. Rejects: empty/whitespace; over
    /// <see cref="MaxUsernameLength"/>; any whitespace, control character, or non-ASCII character; a double-quote;
    /// or a leading dash (which PZ would read as a command flag).
    /// </summary>
    public static string? ValidateUsername(string? username)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            return "A username is required.";
        }

        if (username.Length > MaxUsernameLength)
        {
            return $"The username must be {MaxUsernameLength} characters or fewer.";
        }

        if (username[0] == '-')
        {
            return "The username must not start with '-'.";
        }

        foreach (char c in username)
        {
            if (char.IsWhiteSpace(c))
            {
                return "The username must not contain spaces.";
            }

            if (c is '"')
            {
                return "The username must not contain quotation marks.";
            }

            if (char.IsControl(c) || c > '~' || c < ' ')
            {
                return "The username must contain only printable ASCII characters.";
            }
        }

        return null;
    }

    /// <summary>
    /// Validates an optional kick/ban <paramref name="reason"/>. A <c>null</c> reason is valid (the reason is
    /// omitted). A supplied reason may contain spaces (it is quoted), but returns a rejection reason when it is
    /// over <see cref="MaxReasonLength"/>, or contains a double-quote, a control character (including newlines),
    /// or a non-ASCII character.
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

        foreach (char c in reason)
        {
            if (c is '"')
            {
                return "The reason must not contain quotation marks.";
            }

            if (char.IsControl(c) || c > '~' || c < ' ')
            {
                return "The reason must contain only printable ASCII characters.";
            }
        }

        return null;
    }
}
