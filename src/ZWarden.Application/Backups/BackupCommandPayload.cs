using System.Text.Json;

namespace ZWarden.Application.Backups;

/// <summary>
/// The layer-neutral payload stored on a backup Operation's <c>CommandPayload</c> at enqueue and read back at
/// completion (F24). It lives in the Application layer because both the enqueueing service (Infrastructure, which
/// cannot see the Contracts wire types) and the Web-side dispatcher/ingest reference it. Only the fields a given
/// kind uses are set: a <see cref="Reason"/> for a <c>Backup</c> (so the ingest records why it was taken); a
/// <see cref="BackupId"/> and <see cref="ArchiveName"/> for a <c>DeleteBackup</c> (the dispatcher sends the name,
/// the ingest removes the record by id).
/// </summary>
/// <param name="Reason">The <see cref="Domain.Backups.BackupReason"/> name, for a <c>Backup</c> Operation.</param>
/// <param name="BackupId">The backup record's id, for a <c>DeleteBackup</c> Operation.</param>
/// <param name="ArchiveName">The archive file name, for a <c>DeleteBackup</c> Operation.</param>
public sealed record BackupCommandPayload(string? Reason = null, string? BackupId = null, string? ArchiveName = null)
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    /// <summary>Serializes to the canonical JSON stored on the Operation's command payload.</summary>
    public string ToJson() => JsonSerializer.Serialize(this, Options);

    /// <summary>Reads a payload back from an Operation's stored command JSON. Throws
    /// <see cref="JsonException"/> on malformed JSON — a dispatched backup Operation always carries the well-formed
    /// payload this control plane wrote.</summary>
    public static BackupCommandPayload FromJson(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        return JsonSerializer.Deserialize<BackupCommandPayload>(json, Options)
            ?? throw new JsonException("The backup command payload deserialized to null.");
    }
}
