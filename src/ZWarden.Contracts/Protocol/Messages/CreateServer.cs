namespace ZWarden.Contracts.Protocol.Messages;

/// <summary>
/// Provision the canonical ZWarden.PZServer container for a registered Server (F14 PR-B). The target Server
/// is the envelope's <see cref="Envelope{TPayload}.ServerId"/>. The Agent derives everything else (the pinned
/// image, the ZWarden network, the data-mount root, the memory overhead) from its own configuration. The host UDP
/// pair is the operator's <see cref="GamePort"/> when given (#229), otherwise the Agent allocates the next free
/// two-port stride itself from the host's live bindings (F13). It is a <b>mutating, server-scoped</b> Operation,
/// so it claims the per-server lock (ADR 0022). The Agent builds the container from F13's closed create-template
/// (never from caller input — trust-boundaries §4/§5.3), starts it, and reports <see cref="OperationCompleted"/>
/// carrying the ports and container id in its <see cref="OperationCompleted.Provision"/> result. It carries no
/// free-form command.
/// </summary>
/// <param name="GamePort">The operator-chosen host game port (#229); the pair is this port and the one above it.
/// <c>null</c> ⇒ the Agent allocates the next free stride. Additive (ADR 0020): an older caller omits it. The Agent
/// validates it (<c>HostPortRules</c>) and refuses a port already published on the host.</param>
/// <param name="HeapSizeBytes">The operator-chosen JVM heap (#230). The container's memory limit is this plus the
/// Agent's configured overhead. <c>null</c> ⇒ the Agent's default heap. Additive; the Agent validates it.</param>
/// <param name="Settings">Initial <c>servertest.ini</c> values the Agent seeds before the first boot (#230), or
/// <c>null</c> to leave every key to PZ's defaults. Additive; the Agent validates each value.</param>
[ProtocolMessage("provisioning.create-server")]
public sealed record CreateServer(int? GamePort = null, long? HeapSizeBytes = null, InitialServerSettings? Settings = null)
    : AgentCommand;

/// <summary>
/// The basic server settings chosen in the new-server wizard (#230), seeded into <c>servertest.ini</c> before PZ's
/// first boot. Every member is optional: <c>null</c> leaves that key to PZ's default. Values are untrusted input —
/// the Agent validates them (bounds, no line breaks or control characters) before writing a single key.
/// </summary>
/// <param name="Public">Whether the server is listed in the in-game public browser (<c>Public</c>).</param>
/// <param name="PublicName">The name shown in the server browser (<c>PublicName</c>).</param>
/// <param name="MaxPlayers">The player cap (<c>MaxPlayers</c>, 1–254).</param>
/// <param name="Password">The join password (<c>Password</c>). A secret: never printed by <see cref="ToString"/>.</param>
/// <param name="WelcomeMessage">The message shown on join (<c>ServerWelcomeMessage</c>).</param>
public sealed record InitialServerSettings(
    bool? Public = null,
    string? PublicName = null,
    int? MaxPlayers = null,
    string? Password = null,
    string? WelcomeMessage = null)
{
    // Records print every member; the join password must never reach a log line via ToString (trust-boundaries §6).
    private bool PrintMembers(System.Text.StringBuilder builder)
    {
        System.Globalization.CultureInfo invariant = System.Globalization.CultureInfo.InvariantCulture;
        builder.Append(invariant, $"Public = {Public}, PublicName = {PublicName}, MaxPlayers = {MaxPlayers}, ");
        builder.Append(invariant, $"Password = {(Password is null ? string.Empty : "***")}, WelcomeMessage = {WelcomeMessage}");
        return true;
    }
}
