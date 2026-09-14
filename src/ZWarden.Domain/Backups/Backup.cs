using ZWarden.Domain.Ids;
using ZWarden.Domain.Tenancy;

namespace ZWarden.Domain.Backups;

/// <summary>
/// A verifiable copy of a Server's world data (<c>bkp-</c>, F24), created by a Backup Operation and restorable
/// through one (F25). It is <b>observed metadata</b>, not the bytes: the archive itself is a <c>.tar.gz</c> the
/// Agent wrote host-side under its <c>BackupRoot</c> (ADR 0028), and this record holds what the control plane
/// needs to find and trust it — the Server and Agent it belongs to, the relative archive name (the locator,
/// resolved against the Agent's <c>BackupRoot</c> + <see cref="ServerId"/>; never an absolute host path, so it
/// stays portable if the root moves), the byte size, the <b>lowercase-hex SHA-256</b> over the produced archive
/// (the exact value F25 re-verifies before restoring), and the <b>retention metadata</b> — why it was taken and
/// an optional expiry hint a later scheduler (F26) prunes on. It is <see cref="ITenantOwned"/> (stamped and
/// filtered by the ambient tenant, ADR 0016) and write-once: a backup's facts are fixed the moment the Agent
/// reports them, so there is no mutator and no concurrency token.
/// </summary>
public sealed class Backup : ITenantOwned
{
    /// <summary>The greatest length stored for the relative archive name (Agent-authored locator, bounded).</summary>
    public const int MaxArchiveNameLength = 200;

    /// <summary>The greatest length stored for the lowercase-hex SHA-256 (64 hex chars; bounded generously).</summary>
    public const int MaxChecksumLength = 128;

    /// <summary>EF / factory use.</summary>
    public Backup()
    {
    }

    /// <summary>The backup identifier (<c>bkp-&lt;uuid&gt;</c>).</summary>
    public BackupId Id { get; init; } = BackupId.New();

    /// <inheritdoc />
    public TenantId TenantId { get; init; }

    /// <summary>The Server whose world data this backup copies.</summary>
    public ServerId ServerId { get; init; }

    /// <summary>The Agent that wrote the archive and on whose host it lives (a local backup is Agent-resident).</summary>
    public AgentId AgentId { get; init; }

    /// <summary>The archive's relative locator — its file name under <c>&lt;BackupRoot&gt;/&lt;ServerId&gt;/</c> on the
    /// Agent host (Agent-authored; never an absolute path). The full path is reconstructed by the Agent from its
    /// configured <c>BackupRoot</c>, so a backup stays findable if the root is relocated.</summary>
    public string ArchiveName { get; init; } = string.Empty;

    /// <summary>The produced <c>.tar.gz</c>'s size in bytes (observed).</summary>
    public long SizeBytes { get; init; }

    /// <summary>The lowercase-hex SHA-256 over the produced archive bytes — the integrity value F25 re-verifies
    /// before restoring.</summary>
    public string Sha256 { get; init; } = string.Empty;

    /// <summary>Why the backup was taken — <see cref="BackupReason.Manual"/> or <see cref="BackupReason.PreOperation"/>.</summary>
    public BackupReason Reason { get; init; } = BackupReason.Manual;

    /// <summary>When the Agent finished writing the archive (UTC).</summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>An optional retention expiry hint — the point after which a later scheduler (F26) may prune this
    /// backup — or <c>null</c> when it has no expiry. F24 records it; it enforces nothing.</summary>
    public DateTimeOffset? ExpiresAt { get; init; }

    /// <summary>Records a completed backup from the facts the Agent reported (the <c>BackupResult</c> on the
    /// Operation's completion). The <see cref="TenantId"/> is left unset so the ownership interceptor stamps the
    /// ambient tenant on insert (ADR 0016). Validates that the locator and checksum are present and the size is
    /// non-negative — a backup with no verifiable archive is never recorded.</summary>
    public static Backup Record(
        ServerId serverId,
        AgentId agentId,
        string archiveName,
        long sizeBytes,
        string sha256,
        BackupReason reason,
        DateTimeOffset createdAt,
        DateTimeOffset? expiresAt = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archiveName);
        ArgumentException.ThrowIfNullOrWhiteSpace(sha256);
        ArgumentOutOfRangeException.ThrowIfNegative(sizeBytes);

        return new Backup
        {
            Id = BackupId.New(),
            ServerId = serverId,
            AgentId = agentId,
            ArchiveName = archiveName,
            SizeBytes = sizeBytes,
            Sha256 = sha256,
            Reason = reason,
            CreatedAt = createdAt,
            ExpiresAt = expiresAt,
        };
    }
}
