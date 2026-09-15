using System.Collections.Frozen;

namespace ZWarden.Domain.Console;

/// <summary>
/// The input-safety and command-policy rules for the remote administrative console (F28), shared by the Web edge
/// (fast operator feedback before an Operation is enqueued) and the Agent (defence in depth, where the command is
/// actually sent over RCON — trust-boundaries.md §8). The console runs arbitrary RCON, which is the server
/// console running as <c>admin</c> (research §7 "RCON and the server console are the same surface"), so it is a
/// full-admin surface gated by the elevated <c>Console.Execute</c> permission — but two families of command are
/// refused regardless (ADR 0032):
/// <list type="bullet">
/// <item><b>Credential minting/exposure</b> — <c>adduser</c>, <c>setpassword</c>, <c>setaccesslevel</c>,
/// <c>grantadmin</c>/<c>removeadmin</c>, <c>addsteamid</c>/<c>removesteamid</c>, and <c>changeoption</c> when the
/// target key is a credential key (<c>RCONPassword</c>, <c>Password</c>, <c>DiscordToken</c>) — so the console can
/// neither read nor set a secret. <c>changeoption</c> is <i>not</i> restricted to public options, so it could
/// otherwise write a new RCON/admin password into the INI (research §7).</item>
/// <item><b>Lifecycle bypass</b> — <c>quit</c>, which stops the server outside F15's safe stop
/// (docker stop → SIGTERM → FIFO <c>save</c>/<c>quit</c>); shutting a server down has a first-class, safe
/// surface (F15).</item>
/// </list>
/// Everything else runs (and is audited). Both functions are pure and allocation-light so the two enforcement
/// sites share exactly one contract; a denied or unsafe command is refused with a legible, operator-facing
/// reason and never sent.
/// </summary>
public static class ConsoleCommandRules
{
    /// <summary>The maximum accepted command length. A single RCON command line is short; the bound keeps the
    /// command well inside PZ's packet limits and inside <c>Operation.MaxCommandPayloadLength</c> after the line
    /// is JSON-encoded onto the operation's command payload.</summary>
    public const int MaxInputLength = 1024;

    /// <summary>Command names (matched case-insensitively — research §7 quirk 10) the console refuses because they
    /// mint or expose a credential or grant privilege, or (in <c>quit</c>'s case) bypass F15's safe stop.</summary>
    private static readonly FrozenSet<string> DeniedCommands = new[]
    {
        "adduser", "setpassword", "setaccesslevel", "grantadmin", "removeadmin",
        "addsteamid", "removesteamid", "quit",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>Server-option keys (matched case-insensitively) the console refuses as the target of
    /// <c>changeoption</c>, because writing them would set a credential in the INI (research §7).</summary>
    private static readonly FrozenSet<string> CredentialOptionKeys = new[]
    {
        "RCONPassword", "Password", "DiscordToken",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Validates a submitted console command <paramref name="input"/> for transport safety. Returns <c>null</c>
    /// when it is a single, bounded, printable-ASCII line safe to send over RCON, or a short, operator-facing
    /// reason why it was rejected. Rejects: empty/whitespace; over <see cref="MaxInputLength"/>; any control
    /// character (including newlines and tabs, which would split the line or desync framing) or non-ASCII
    /// character (PZ decodes request bodies with the platform default charset — research §7 quirk 11).
    /// </summary>
    public static string? ValidateInput(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return "A command is required.";
        }

        if (input.Length > MaxInputLength)
        {
            return $"The command must be {MaxInputLength} characters or fewer.";
        }

        foreach (char c in input)
        {
            if (c is '\n' or '\r')
            {
                return "The command must be a single line.";
            }

            if (char.IsControl(c) || c > '~' || c < ' ')
            {
                return "The command must contain only printable ASCII characters.";
            }
        }

        return null;
    }

    /// <summary>
    /// Evaluates a submitted console command <paramref name="input"/> against the F28 command policy (ADR 0032).
    /// Returns <c>null</c> when the command is allowed to run, or a short, operator-facing reason why it is
    /// denied. Assumes the input has already passed <see cref="ValidateInput"/> (single line, printable ASCII);
    /// it parses the first token as the command name (case-insensitive) and, for <c>changeoption</c>, the second
    /// token as the option key, and refuses the credential-minting/exposure and lifecycle-bypass families.
    /// </summary>
    public static string? EvaluatePolicy(string input)
    {
        ArgumentNullException.ThrowIfNull(input);

        string[] tokens = input.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0)
        {
            return "A command is required.";
        }

        string command = Unquote(tokens[0]);
        if (DeniedCommands.Contains(command))
        {
            return command.Equals("quit", StringComparison.OrdinalIgnoreCase)
                ? "The 'quit' command is not allowed from the console — stop the server from its lifecycle controls so the world is saved safely."
                : $"The '{command}' command is not allowed from the console because it manages credentials or access.";
        }

        if (command.Equals("changeoption", StringComparison.OrdinalIgnoreCase)
            && tokens.Length >= 2
            && CredentialOptionKeys.Contains(Unquote(tokens[1])))
        {
            return "Changing a credential option from the console is not allowed.";
        }

        return null;
    }

    private static string Unquote(string token) => token.Trim('"');
}
