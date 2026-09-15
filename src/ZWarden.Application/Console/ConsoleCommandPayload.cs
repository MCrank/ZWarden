using System.Text.Json;

namespace ZWarden.Application.Console;

/// <summary>
/// The layer-neutral parameter of an F28 console command — the operator-authored RCON line — serialized as JSON
/// onto an Operation's <c>CommandPayload</c> at enqueue and read back by the Web-side dispatcher to build the wire
/// command. It lives in the Application layer precisely because both sides reference it but neither the enqueueing
/// service (Infrastructure, which cannot see the Contracts wire types) nor a test needs the protocol assembly to
/// carry the line across. The line has already passed <c>ConsoleCommandRules</c> (input safety + policy) before it
/// is stored.
/// </summary>
/// <param name="Input">The RCON command line to run.</param>
public sealed record ConsoleCommandPayload(string Input)
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    /// <summary>Serializes to the canonical JSON stored on the Operation's command payload.</summary>
    public string ToJson() => JsonSerializer.Serialize(this, Options);

    /// <summary>Reads a payload back from an Operation's stored command JSON. Throws
    /// <see cref="System.Text.Json.JsonException"/> on malformed JSON — a dispatched console Operation always has a
    /// well-formed payload this control plane wrote.</summary>
    public static ConsoleCommandPayload FromJson(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        return JsonSerializer.Deserialize<ConsoleCommandPayload>(json, Options)
            ?? throw new JsonException("The console command payload deserialized to null.");
    }
}
