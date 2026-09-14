using System.Text.Json;

namespace ZWarden.Application.Backups;

/// <summary>
/// The layer-neutral payload stored on a restore Operation's <c>CommandPayload</c> at enqueue and read back when the
/// dispatcher builds the wire command (F25). It lives in the Application layer because both the enqueueing service
/// (Infrastructure, which cannot see the Contracts wire types) and the Web-side dispatcher/ingest reference it. It
/// carries the target backup's record id (so the ingest can correlate the completion), the archive's relative
/// locator, and the lowercase-hex SHA-256 the Agent re-verifies before unpacking (ADR 0028/0029).
/// </summary>
/// <param name="BackupId">The backup record's id being restored.</param>
/// <param name="ArchiveName">The archive file name to restore (a bare name; resolved Agent-side under BackupRoot).</param>
/// <param name="Sha256">The lowercase-hex SHA-256 the control plane recorded for the archive.</param>
public sealed record RestoreCommandPayload(string BackupId, string ArchiveName, string Sha256)
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    /// <summary>Serializes to the canonical JSON stored on the Operation's command payload.</summary>
    public string ToJson() => JsonSerializer.Serialize(this, Options);

    /// <summary>Reads a payload back from an Operation's stored command JSON. Throws <see cref="JsonException"/> on
    /// malformed JSON — a dispatched restore Operation always carries the well-formed payload this control plane
    /// wrote.</summary>
    public static RestoreCommandPayload FromJson(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        return JsonSerializer.Deserialize<RestoreCommandPayload>(json, Options)
            ?? throw new JsonException("The restore command payload deserialized to null.");
    }
}
