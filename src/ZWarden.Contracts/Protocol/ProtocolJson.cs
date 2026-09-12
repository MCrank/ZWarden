using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using ZWarden.Domain.Ids;

namespace ZWarden.Contracts.Protocol;

/// <summary>
/// The one canonical serializer both ZWarden.Web and ZWarden.Agent use, so a message written by
/// one is read identically by the other. It frames an <see cref="Envelope{TPayload}"/> as metadata
/// plus a stable <c>messageType</c> discriminator plus the payload, and dispatches on that
/// discriminator when reading. Typed ids travel as their canonical strings (PRD 6); enums travel by
/// name; unknown members are tolerated so an additive change stays backward-compatible (ADR 0020).
/// </summary>
/// <remarks>
/// Reflection-based System.Text.Json, matching <see cref="TypedIdJsonConverterFactory"/> (a runtime
/// converter factory) — deliberately not source-generated. The message registry is built by
/// scanning the Contracts assembly for <see cref="ProtocolMessageAttribute"/>, so a message leaf
/// added by a later feature is registered automatically.
/// </remarks>
public static class ProtocolJson
{
    /// <summary>
    /// The canonical options. Camel-cased members, typed ids as canonical strings, enums by name,
    /// and unknown members skipped (additive forward-compatibility).
    /// </summary>
    public static JsonSerializerOptions Options { get; } = BuildOptions();

    /// <summary>The known wire discriminators mapped to their message types (diagnostics and tests).</summary>
    public static IReadOnlyDictionary<string, Type> MessageTypes => Registry.Value.ByDiscriminator;

    /// <summary>Serializes an envelope to its canonical wire JSON.</summary>
    public static string Serialize<TPayload>(Envelope<TPayload> envelope)
        where TPayload : IProtocolMessage
    {
        ArgumentNullException.ThrowIfNull(envelope);

        Type payloadType = envelope.Payload.GetType();
        WireEnvelope wire = new()
        {
            ProtocolVersion = envelope.ProtocolVersion,
            MessageId = envelope.MessageId,
            MessageType = DiscriminatorFor(payloadType),
            Timestamp = envelope.Timestamp,
            AgentId = envelope.AgentId,
            ServerId = envelope.ServerId,
            OperationId = envelope.OperationId,
            Payload = JsonSerializer.SerializeToNode(envelope.Payload, payloadType, Options),
        };

        return JsonSerializer.Serialize(wire, Options);
    }

    /// <summary>
    /// Reads a canonical wire JSON envelope, dispatching to the concrete payload type named by its
    /// <c>messageType</c>. Throws <see cref="JsonException"/> — naming the fault — on an unknown
    /// discriminator, a missing payload, or malformed JSON.
    /// </summary>
    public static Envelope<IProtocolMessage> Deserialize(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        WireEnvelope wire = JsonSerializer.Deserialize<WireEnvelope>(json, Options)
            ?? throw new JsonException("The protocol envelope deserialized to null.");

        if (!Registry.Value.ByDiscriminator.TryGetValue(wire.MessageType, out Type? payloadType))
        {
            throw new JsonException($"Unknown protocol messageType '{wire.MessageType}'.");
        }

        if (wire.Payload is null)
        {
            throw new JsonException($"Protocol message '{wire.MessageType}' carried no payload.");
        }

        IProtocolMessage payload = (IProtocolMessage?)wire.Payload.Deserialize(payloadType, Options)
            ?? throw new JsonException($"Protocol message '{wire.MessageType}' payload deserialized to null.");

        return new Envelope<IProtocolMessage>
        {
            ProtocolVersion = wire.ProtocolVersion,
            MessageId = wire.MessageId,
            Timestamp = wire.Timestamp,
            AgentId = wire.AgentId,
            ServerId = wire.ServerId,
            OperationId = wire.OperationId,
            Payload = payload,
        };
    }

    /// <summary>
    /// Reads a canonical envelope and asserts its payload is <typeparamref name="TPayload"/>. Throws
    /// <see cref="JsonException"/> if the message on the wire is a different type.
    /// </summary>
    public static Envelope<TPayload> Deserialize<TPayload>(string json)
        where TPayload : IProtocolMessage
    {
        Envelope<IProtocolMessage> envelope = Deserialize(json);
        if (envelope.Payload is not TPayload typed)
        {
            throw new JsonException(
                $"Expected a '{typeof(TPayload).Name}' message but the envelope carried '{envelope.Payload.GetType().Name}'.");
        }

        return new Envelope<TPayload>
        {
            ProtocolVersion = envelope.ProtocolVersion,
            MessageId = envelope.MessageId,
            Timestamp = envelope.Timestamp,
            AgentId = envelope.AgentId,
            ServerId = envelope.ServerId,
            OperationId = envelope.OperationId,
            Payload = typed,
        };
    }

    private static string DiscriminatorFor(Type payloadType)
        => Registry.Value.ByType.TryGetValue(payloadType, out string? discriminator)
            ? discriminator
            : throw new JsonException(
                $"'{payloadType.Name}' is not a registered protocol message. Add a [ProtocolMessage] attribute.");

    private static JsonSerializerOptions BuildOptions()
    {
        JsonSerializerOptions options = new(JsonSerializerDefaults.Web)
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
        options.Converters.Add(new TypedIdJsonConverterFactory());
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private static readonly Lazy<MessageRegistry> Registry = new(MessageRegistry.Build);

    private sealed record WireEnvelope
    {
        public int ProtocolVersion { get; init; }
        public MessageId MessageId { get; init; }
        public string MessageType { get; init; } = string.Empty;
        public DateTimeOffset Timestamp { get; init; }
        public AgentId? AgentId { get; init; }
        public ServerId? ServerId { get; init; }
        public OperationId? OperationId { get; init; }
        public JsonNode? Payload { get; init; }
    }

    private sealed class MessageRegistry
    {
        public required Dictionary<string, Type> ByDiscriminator { get; init; }

        public required Dictionary<Type, string> ByType { get; init; }

        public static MessageRegistry Build()
        {
            Dictionary<string, Type> byDiscriminator = [];
            Dictionary<Type, string> byType = [];

            foreach (Type type in typeof(IProtocolMessage).Assembly.GetTypes())
            {
                ProtocolMessageAttribute? attribute = type.GetCustomAttribute<ProtocolMessageAttribute>(inherit: false);
                if (attribute is null)
                {
                    continue;
                }

                if (!typeof(IProtocolMessage).IsAssignableFrom(type))
                {
                    throw new InvalidOperationException(
                        $"'{type.FullName}' has [ProtocolMessage] but is not an {nameof(IProtocolMessage)}.");
                }

                if (!byDiscriminator.TryAdd(attribute.Discriminator, type))
                {
                    throw new InvalidOperationException(
                        $"Duplicate protocol messageType '{attribute.Discriminator}' on '{type.FullName}' and '{byDiscriminator[attribute.Discriminator].FullName}'.");
                }

                byType[type] = attribute.Discriminator;
            }

            return new MessageRegistry { ByDiscriminator = byDiscriminator, ByType = byType };
        }
    }
}
