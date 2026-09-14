using System.Text.Json;

namespace ZWarden.Application.Players;

/// <summary>
/// The layer-neutral parameters of an F19 player command, serialized as JSON onto an Operation's
/// <c>CommandPayload</c> at enqueue and read back by the Web-side dispatcher to build the wire command. It lives
/// in the Application layer precisely because both sides reference it but neither the enqueueing service
/// (Infrastructure, which cannot see the Contracts wire types) nor a test needs the protocol assembly to carry a
/// username across. Only the fields a given command uses are set: a username (kick/ban/unban/remove), an optional
/// reason (kick/ban), or the whitelist-mode flag (set-whitelist-mode).
/// </summary>
/// <param name="Username">The target account username, for the username-bearing commands.</param>
/// <param name="Reason">The optional kick/ban reason.</param>
/// <param name="Open">The desired whitelist-mode <c>Open</c> value, for the set-whitelist-mode command.</param>
public sealed record PlayerCommandPayload(string? Username = null, string? Reason = null, bool? Open = null)
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    /// <summary>Serializes to the canonical JSON stored on the Operation's command payload.</summary>
    public string ToJson() => JsonSerializer.Serialize(this, Options);

    /// <summary>Reads a payload back from an Operation's stored command JSON. Throws
    /// <see cref="System.Text.Json.JsonException"/> on malformed JSON — a dispatched player Operation always has a
    /// well-formed payload this control plane wrote.</summary>
    public static PlayerCommandPayload FromJson(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        return JsonSerializer.Deserialize<PlayerCommandPayload>(json, Options)
            ?? throw new JsonException("The player command payload deserialized to null.");
    }
}
