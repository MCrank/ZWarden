using ZWarden.Domain.Servers;

namespace ZWarden.Agent.Servers;

/// <summary>
/// Builds the raw PZ <c>servermsg</c> command text for a server-wide broadcast (#114). This is the
/// command-construction and <b>mandatory quoting</b> layer ADR 0026 defers out of <c>ZWarden.Rcon</c> (which
/// sends raw text) — the same pattern as F19's <see cref="Players.PlayerCommandBuilder"/>, but for a lifecycle
/// broadcast rather than a player action. The message is validated through
/// <see cref="BroadcastMessageRules.ValidateMessage"/> first (throwing <see cref="BroadcastCommandException"/>
/// before a byte reaches RCON) and then wrapped in double quotes, which is safe precisely because a message
/// containing a quote is rejected. PZ's tokenizer strips the quotes and treats the quoted run as one argument
/// (research §7 quirk 10), so <c>servermsg "My Message"</c> is required, not optional.
/// </summary>
public static class ServerBroadcastCommandBuilder
{
    /// <summary>Builds <c>servermsg "&lt;message&gt;"</c>. Throws <see cref="BroadcastCommandException"/> if the
    /// message is empty, over-long, or would break the quoting (a quote, a control character, or a non-ASCII
    /// byte).</summary>
    public static string ServerMessage(string message)
    {
        if (BroadcastMessageRules.ValidateMessage(message) is { } error)
        {
            throw new BroadcastCommandException(error);
        }

        return $"servermsg \"{message}\"";
    }
}
