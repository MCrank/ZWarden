using ZWarden.Domain.Ids;

namespace ZWarden.Contracts.Protocol;

/// <summary>
/// The self-describing wrapper every protocol message travels in (PRD 18). It carries the
/// metadata common to all messages — the protocol version it was encoded under, a unique
/// <see cref="MessageId"/>, a UTC timestamp, and the optional routing ids the payload warrants —
/// around a strongly-typed <typeparamref name="TPayload"/>. The wire also carries a stable
/// <c>messageType</c> discriminator (from <see cref="ProtocolMessageAttribute"/>); that is a
/// serialization concern owned by <see cref="ProtocolJson"/>, not a field here.
/// </summary>
/// <typeparam name="TPayload">The message this envelope carries.</typeparam>
public sealed record Envelope<TPayload>
    where TPayload : IProtocolMessage
{
    /// <summary>The protocol version this envelope was encoded under (PRD 18).</summary>
    public required int ProtocolVersion { get; init; }

    /// <summary>This message's unique identity; also an idempotency/dedup key (PRD 18, PRD 20).</summary>
    public required MessageId MessageId { get; init; }

    /// <summary>When the message was produced, in UTC.</summary>
    public required DateTimeOffset Timestamp { get; init; }

    /// <summary>The Agent the message concerns, when applicable.</summary>
    public AgentId? AgentId { get; init; }

    /// <summary>The Server the message concerns, when applicable.</summary>
    public ServerId? ServerId { get; init; }

    /// <summary>The Operation the message concerns, when applicable (required on command/operation messages).</summary>
    public OperationId? OperationId { get; init; }

    /// <summary>The strongly-typed message payload.</summary>
    public required TPayload Payload { get; init; }
}

/// <summary>Factories for <see cref="Envelope{TPayload}"/>.</summary>
public static class Envelope
{
    /// <summary>
    /// Wraps <paramref name="payload"/> for the current protocol version with a fresh
    /// <see cref="MessageId"/>. The timestamp is supplied by the caller rather than read from a
    /// hidden clock, so producers stay testable and the time source is explicit.
    /// </summary>
    public static Envelope<TPayload> Create<TPayload>(
        TPayload payload,
        DateTimeOffset timestamp,
        AgentId? agentId = null,
        ServerId? serverId = null,
        OperationId? operationId = null)
        where TPayload : IProtocolMessage
    {
        ArgumentNullException.ThrowIfNull(payload);
        return new Envelope<TPayload>
        {
            ProtocolVersion = ProtocolVersion.Current,
            MessageId = MessageId.New(),
            Timestamp = timestamp,
            AgentId = agentId,
            ServerId = serverId,
            OperationId = operationId,
            Payload = payload,
        };
    }
}
