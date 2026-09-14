using ZWarden.Domain.Players;

namespace ZWarden.Agent.Players;

/// <summary>
/// Builds the raw PZ admin-command text for a player-management action (F19). This is the command-construction
/// and <b>mandatory quoting</b> layer ADR 0026 deferred out of <c>ZWarden.Rcon</c> (which sends raw text) to
/// F19/F28 — it lives Agent-side because the command is assembled here, next to the socket. Every argument is
/// validated through <see cref="PlayerCommandRules"/> first (throwing <see cref="PlayerCommandException"/> on a
/// rejection, before a byte reaches RCON) and then wrapped in double quotes, which is safe precisely because a
/// value containing a quote is rejected. PZ's tokenizer strips the quotes and treats the quoted run as one
/// argument (research §7 quirk 10), so <c>kickuser "My Name"</c> is required, not optional.
/// </summary>
public static class PlayerCommandBuilder
{
    /// <summary>The <c>players</c> command — no arguments (research §7: excluded from the RCON debug log).</summary>
    public static string List() => "players";

    /// <summary>Builds <c>kickuser "&lt;user&gt;" -r "&lt;reason&gt;"</c> (the <c>-r</c> clause omitted when there is
    /// no reason). Throws <see cref="PlayerCommandException"/> if the username or reason is invalid.</summary>
    public static string Kick(string username, string? reason)
    {
        string user = QuoteUsername(username);
        string validatedReason = ValidatedReasonClause(reason);
        return $"kickuser {user}{validatedReason}";
    }

    /// <summary>Builds <c>banuser "&lt;user&gt;" -r "&lt;reason&gt;"</c> (account-username bans only, ADR 0027).
    /// Throws <see cref="PlayerCommandException"/> if the username or reason is invalid.</summary>
    public static string Ban(string username, string? reason)
    {
        string user = QuoteUsername(username);
        string validatedReason = ValidatedReasonClause(reason);
        return $"banuser {user}{validatedReason}";
    }

    /// <summary>Builds <c>unbanuser "&lt;user&gt;"</c>. Throws <see cref="PlayerCommandException"/> if the username
    /// is invalid.</summary>
    public static string Unban(string username) => $"unbanuser {QuoteUsername(username)}";

    /// <summary>Builds <c>removeuserfromwhitelist "&lt;user&gt;"</c>. Throws <see cref="PlayerCommandException"/> if
    /// the username is invalid.</summary>
    public static string RemoveFromWhitelist(string username) => $"removeuserfromwhitelist {QuoteUsername(username)}";

    /// <summary>Builds <c>changeoption Open true</c> / <c>changeoption Open false</c> — the whitelist mode toggle.
    /// The value is a fixed literal, so there is nothing to validate.</summary>
    public static string SetWhitelistMode(bool open) => $"changeoption Open {(open ? "true" : "false")}";

    private static string QuoteUsername(string username)
    {
        if (PlayerCommandRules.ValidateUsername(username) is { } error)
        {
            throw new PlayerCommandException(error);
        }

        return $"\"{username}\"";
    }

    private static string ValidatedReasonClause(string? reason)
    {
        if (PlayerCommandRules.ValidateReason(reason) is { } error)
        {
            throw new PlayerCommandException(error);
        }

        return reason is null ? string.Empty : $" -r \"{reason}\"";
    }
}
