using System.Text.Json;
using System.Text.Json.Serialization;
using ZWarden.Domain.Configuration;

namespace ZWarden.Application.Configuration;

/// <summary>
/// The layer-neutral parameters of an F20c raw whole-file configuration-apply Operation, serialized as JSON onto
/// the Operation's <c>CommandPayload</c> at enqueue and read back by the Web-side dispatcher to build the
/// <c>ConfigApplyRaw</c> wire command (ADR 0042). Like <see cref="ConfigApplyPayload"/> it lives in the Application
/// layer so both the enqueueing service (Infrastructure) and the dispatcher reference it without the Contracts
/// assembly. It is deliberately tiny — the operator's whole-file text does <b>not</b> travel here (it is staged to
/// the Agent over a separate channel); this carries only which <see cref="PzConfigFile"/> to overwrite, the drift
/// <see cref="BaselineHash"/> the Agent re-checks, and the <see cref="CorrelationId"/> the Agent retrieves the
/// staged text by — so it stays well under the 2 KB command-payload cap.
/// </summary>
/// <param name="File">Which of the Server's four configuration files to overwrite.</param>
/// <param name="BaselineHash">The drift baseline the Agent re-checks before writing (the operator's live-read
/// baseline, ADR 0042), or <c>null</c> for a first write with no baseline.</param>
/// <param name="CorrelationId">The id the operator's whole-file text was staged to the Agent under.</param>
public sealed record ConfigApplyRawPayload(
    PzConfigFile File,
    string? BaselineHash,
    string CorrelationId)
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Serializes to the canonical JSON stored on the Operation's command payload.</summary>
    public string ToJson() => JsonSerializer.Serialize(this, Options);

    /// <summary>Reads a payload back from an Operation's stored command JSON. Throws
    /// <see cref="JsonException"/> on malformed JSON — a dispatched raw-config Operation always has a well-formed
    /// payload this control plane wrote.</summary>
    public static ConfigApplyRawPayload FromJson(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        return JsonSerializer.Deserialize<ConfigApplyRawPayload>(json, Options)
            ?? throw new JsonException("The raw config apply payload deserialized to null.");
    }
}
