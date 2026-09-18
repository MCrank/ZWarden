using System.Text.Json;

namespace ZWarden.Application.Servers;

/// <summary>
/// The layer-neutral parameters of a graceful restart (#114), serialized as JSON onto a
/// <c>OperationKind.RestartServer</c> Operation's <c>CommandPayload</c> at enqueue and read back by the Web-side
/// dispatcher to build the wire command's <c>GracefulRestartPlan</c>. Like
/// <see cref="Players.PlayerCommandPayload"/>, it lives in the Application layer so the enqueueing service
/// (Infrastructure, which cannot see the Contracts wire types) and the dispatcher can share it without the
/// protocol assembly. A restart enqueued <b>without</b> a payload restarts with the Agent's default warning
/// schedule; a payload with an <b>empty</b> <see cref="WarningLeadSeconds"/> restarts immediately without warning.
/// </summary>
/// <param name="WarningLeadSeconds">The seconds-before-stop at which to broadcast, strictly descending. Empty =
/// skip the broadcast. Validated by <c>GracefulRestartRules</c> before enqueue.</param>
/// <param name="Reason">The optional clause appended to each countdown notice.</param>
public sealed record GracefulRestartPayload(IReadOnlyList<int> WarningLeadSeconds, string? Reason = null)
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    /// <summary>Serializes to the canonical JSON stored on the Operation's command payload.</summary>
    public string ToJson() => JsonSerializer.Serialize(this, Options);

    /// <summary>Reads a payload back from an Operation's stored command JSON. Throws
    /// <see cref="JsonException"/> on malformed JSON — a dispatched restart Operation always has a well-formed
    /// payload this control plane wrote.</summary>
    public static GracefulRestartPayload FromJson(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        return JsonSerializer.Deserialize<GracefulRestartPayload>(json, Options)
            ?? throw new JsonException("The graceful-restart payload deserialized to null.");
    }
}
