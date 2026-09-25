using System.Text.Json;

namespace ZWarden.Application.Servers;

/// <summary>
/// The layer-neutral parameters of building a Server's container (#229), serialized as JSON onto a
/// <c>OperationKind.ProvisionServer</c> or <c>OperationKind.RecreateServer</c> Operation's <c>CommandPayload</c> at
/// enqueue and read back by the Web-side dispatcher to build the wire command. Like
/// <see cref="GracefulRestartPayload"/>, it lives in the Application layer so the enqueueing service (Infrastructure)
/// and the dispatcher can share it without the protocol assembly. A provision enqueued <b>without</b> a payload
/// allocates the next free stride.
/// </summary>
/// <param name="GamePort">The operator-chosen host game port (the pair is it and the port above), or <c>null</c> to
/// keep the current pair (recreate) / allocate the next free stride (provision). Validated by <c>HostPortRules</c>
/// before enqueue; the Agent re-validates and pre-flights it against the host.</param>
/// <param name="Plan">The graceful-warning plan before a recreate's safe stop, or <c>null</c> for the Agent's default
/// schedule. Unused by provisioning.</param>
public sealed record ServerContainerPayload(int? GamePort, GracefulRestartPayload? Plan = null)
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    /// <summary>Serializes to the canonical JSON stored on the Operation's command payload.</summary>
    public string ToJson() => JsonSerializer.Serialize(this, Options);

    /// <summary>Reads a payload back from an Operation's stored command JSON. Throws <see cref="JsonException"/> on
    /// malformed JSON — a dispatched Operation always carries a payload this control plane wrote.</summary>
    public static ServerContainerPayload FromJson(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        return JsonSerializer.Deserialize<ServerContainerPayload>(json, Options)
            ?? throw new JsonException("The server container payload deserialized to null.");
    }
}
