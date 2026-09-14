using System.Text.Json;
using System.Text.Json.Serialization;
using ZWarden.Domain.Configuration;

namespace ZWarden.Application.Configuration;

/// <summary>The scalar kind of a <see cref="ConfigApplyEdit"/>, layer-neutral so it can be serialized onto an
/// Operation's command payload without the Application layer seeing the Contracts wire types. Mirrors the wire
/// <c>ConfigValueKind</c>; the Web-side dispatcher maps the two when it builds the command.</summary>
public enum ConfigEditKind
{
    /// <summary>A boolean; <see cref="ConfigApplyEdit.Value"/> is <c>"true"</c>/<c>"false"</c>.</summary>
    Bool,

    /// <summary>A number; <see cref="ConfigApplyEdit.Value"/> is the exact numeric lexeme to write.</summary>
    Number,

    /// <summary>A string; <see cref="ConfigApplyEdit.Value"/> is the unescaped content.</summary>
    Text,
}

/// <summary>One surgical value edit in layer-neutral form: set the scalar at a dotted <see cref="Path"/> to
/// <see cref="Value"/>, interpreted per <see cref="Kind"/>. Mirrors the wire <c>ConfigValueEdit</c>.</summary>
/// <param name="Path">The dotted path to the scalar to replace.</param>
/// <param name="Kind">How to interpret <see cref="Value"/>.</param>
/// <param name="Value">The new value in wire form (a boolean literal, a numeric lexeme, or raw string content).</param>
public sealed record ConfigApplyEdit(string Path, ConfigEditKind Kind, string Value);

/// <summary>
/// The layer-neutral parameters of an F20b configuration-apply Operation, serialized as JSON onto the Operation's
/// <c>CommandPayload</c> at enqueue and read back by the Web-side dispatcher to build the <c>ConfigApply</c> wire
/// command. It lives in the Application layer for the same reason as <see cref="Players.PlayerCommandPayload"/>:
/// both the enqueueing service (Infrastructure, which cannot see the Contracts wire types) and the dispatcher
/// reference it, and neither needs the protocol assembly to carry the edits across. It names which
/// <see cref="PzConfigFile"/> to write, the drift baseline the Agent re-checks (ADR 0011), and the edits.
/// </summary>
/// <param name="File">Which of the Server's four configuration files to write.</param>
/// <param name="BaselineHash">The last recorded revision's canonical hash — the drift baseline — or <c>null</c>
/// for the first write to this file.</param>
/// <param name="Edits">The surgical value edits to apply, in order.</param>
public sealed record ConfigApplyPayload(
    PzConfigFile File,
    string? BaselineHash,
    IReadOnlyList<ConfigApplyEdit> Edits)
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Serializes to the canonical JSON stored on the Operation's command payload.</summary>
    public string ToJson() => JsonSerializer.Serialize(this, Options);

    /// <summary>Reads a payload back from an Operation's stored command JSON. Throws
    /// <see cref="JsonException"/> on malformed JSON — a dispatched config Operation always has a well-formed
    /// payload this control plane wrote.</summary>
    public static ConfigApplyPayload FromJson(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        return JsonSerializer.Deserialize<ConfigApplyPayload>(json, Options)
            ?? throw new JsonException("The config apply payload deserialized to null.");
    }
}
