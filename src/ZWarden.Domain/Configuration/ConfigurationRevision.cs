using ZWarden.Domain.Ids;
using ZWarden.Domain.Tenancy;

namespace ZWarden.Domain.Configuration;

/// <summary>
/// A <b>Configuration Revision</b> (<c>cfg-</c>, F20b) — a recorded before/after state of one of a Server's
/// four configuration files, captured as <b>parsed values, not file bytes</b> (ADR 0011; CONTEXT.md). A
/// byte-level record would be actively wrong: the server rewrites its <c>.ini</c> and <c>_SandboxVars.lua</c>
/// on every start from its own model, reseeding a randomised number inside a comment and regenerating
/// comments from its locale, so a byte diff would assert changes the operator never made. This record
/// therefore persists the file's <see cref="CanonicalSnapshot"/> — the order-normalized serialization of its
/// scalar values produced by <c>ZWarden.PzConfig</c>'s <c>PzValueSnapshot</c> — together with the
/// <see cref="SnapshotHash"/>, the drift-baseline fingerprint the Agent re-derives from the live file and
/// compares before every write, failing closed on a mismatch (ADR 0011).
/// <para>
/// It is <see cref="ITenantOwned"/> (stamped and filtered by the ambient tenant, ADR 0016) and
/// <see cref="IVersioned"/>. The snapshot and hash are computed by the parser library and passed in — the
/// domain takes no dependency on it (ReferenceDirectionTests §9 rule 2). The value-level diff between
/// consecutive revisions is <b>derived</b> for display (PR 4), never stored.
/// </para>
/// </summary>
public sealed class ConfigurationRevision : IVersioned, ITenantOwned
{
    /// <summary>The length of a lowercase-hex SHA-256 fingerprint — the exact size of <see cref="SnapshotHash"/>.</summary>
    public const int HashLength = 64;

    /// <summary>EF / factory use.</summary>
    public ConfigurationRevision()
    {
    }

    /// <summary>The revision identifier (<c>cfg-&lt;uuid&gt;</c>).</summary>
    public ConfigurationRevisionId Id { get; init; } = ConfigurationRevisionId.New();

    /// <inheritdoc />
    public TenantId TenantId { get; init; }

    /// <summary>The Server whose configuration this revision records.</summary>
    public ServerId ServerId { get; init; }

    /// <summary>Which of the Server's four configuration files this revision is for.</summary>
    public PzConfigFile File { get; init; }

    /// <summary>The order-normalized serialization of the file's parsed scalar values — the "state" this
    /// revision records (ADR 0011). Never the file's bytes; a key reorder (which the server performs on every
    /// start) yields an identical snapshot.</summary>
    public string CanonicalSnapshot { get; init; } = string.Empty;

    /// <summary>The lowercase-hex SHA-256 of <see cref="CanonicalSnapshot"/> — the drift baseline. The Agent
    /// re-parses the live file, re-canonicalizes, re-hashes, and refuses the write on a mismatch (ADR 0011).</summary>
    public string SnapshotHash { get; init; } = string.Empty;

    /// <summary>When the revision was recorded (UTC).</summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>The user who applied the change this revision records, or <c>null</c> for an unattributed
    /// capture (a baseline read from an already-existing on-disk file, which no ZWarden user authored).</summary>
    public UserId? CreatedByUserId { get; init; }

    /// <inheritdoc />
    public Guid Version { get; set; }

    /// <summary>
    /// Records a revision of <paramref name="file"/> on <paramref name="serverId"/> from its canonical value
    /// snapshot and that snapshot's hash (both computed by the parser library). The <see cref="TenantId"/> is
    /// left unset so the ownership interceptor stamps the ambient tenant on insert (ADR 0016).
    /// <paramref name="createdBy"/> is the acting user, or <c>null</c> for an unattributed baseline capture.
    /// </summary>
    public static ConfigurationRevision Record(
        ServerId serverId,
        PzConfigFile file,
        string canonicalSnapshot,
        string snapshotHash,
        DateTimeOffset now,
        UserId? createdBy = null)
    {
        if (serverId.IsEmpty)
        {
            throw new ArgumentException("A configuration revision must name its Server.", nameof(serverId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalSnapshot);
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshotHash);

        return new ConfigurationRevision
        {
            Id = ConfigurationRevisionId.New(),
            ServerId = serverId,
            File = file,
            CanonicalSnapshot = canonicalSnapshot,
            SnapshotHash = snapshotHash,
            CreatedAt = now,
            CreatedByUserId = createdBy,
        };
    }
}
