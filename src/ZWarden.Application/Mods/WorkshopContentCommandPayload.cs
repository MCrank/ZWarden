using System.Text.Json;

namespace ZWarden.Application.Mods;

/// <summary>
/// The layer-neutral payload stored on a <c>DeleteWorkshopContent</c> Operation (#293): the Workshop ids whose
/// downloads to delete. It lives in the Application layer because the enqueueing service (Infrastructure, which cannot
/// see the Contracts wire types) writes it and the Web-side dispatcher reads it.
/// </summary>
/// <param name="WorkshopIds">The Workshop ids to delete, in request order.</param>
public sealed record WorkshopContentCommandPayload(IReadOnlyList<string> WorkshopIds)
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    /// <summary>Serializes to the canonical JSON stored on the Operation's command payload.</summary>
    public string ToJson() => JsonSerializer.Serialize(this, Options);

    /// <summary>Reads a payload back from an Operation's stored command JSON. Throws <see cref="JsonException"/> on
    /// malformed JSON — a dispatched delete always carries the payload this control plane wrote.</summary>
    public static WorkshopContentCommandPayload FromJson(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        return JsonSerializer.Deserialize<WorkshopContentCommandPayload>(json, Options)
            ?? throw new JsonException("The Workshop content command payload deserialized to null.");
    }
}
