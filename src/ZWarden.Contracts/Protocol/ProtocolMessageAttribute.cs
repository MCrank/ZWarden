namespace ZWarden.Contracts.Protocol;

/// <summary>
/// Declares the stable wire discriminator for a protocol message leaf. The string is the
/// message's identity on the wire (e.g. <c>"agent.hello"</c>): it is written into the envelope's
/// <c>messageType</c> and is how a receiver picks the concrete type to deserialize into. It must
/// never change once shipped — renaming it is a breaking protocol change (bump
/// <see cref="ProtocolVersion.Current"/>), never a refactor.
/// </summary>
/// <remarks>
/// Adding a message: declare a <c>sealed record</c> deriving from <see cref="AgentCommand"/> or
/// <see cref="AgentEvent"/>, put <c>[ProtocolMessage("area.name")]</c> on it, and it is registered
/// automatically (<see cref="ProtocolJson"/> scans the Contracts assembly). The closed-vocabulary
/// and discriminator-uniqueness tests enforce the rules.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ProtocolMessageAttribute(string discriminator) : Attribute
{
    /// <summary>The stable wire discriminator, e.g. <c>"agent.heartbeat"</c>.</summary>
    public string Discriminator { get; } = discriminator;
}
