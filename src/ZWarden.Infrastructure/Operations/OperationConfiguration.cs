using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ZWarden.Domain.Operations;

namespace ZWarden.Infrastructure.Operations;

/// <summary>
/// Maps the <c>Operations</c> table (F11). The model conventions supply the typed-id conversions (including
/// the nullable <c>ServerId?</c>), the <c>Version</c> concurrency token, and the tenant filter (ADR 0016),
/// so this only declares columns and the two indexes.
/// <para>
/// The <b>per-server lock</b> (PRD 21) is a partial unique index: at most one <i>mutating</i> Operation may
/// be <c>Pending</c>/<c>Running</c> per Server, so acquiring the lock is inserting the row and releasing it
/// is reaching a terminal state (ADR 0005/0022). Its filter emits <b>verbatim-identically</b> on SQLite and
/// PostgreSQL — the point of ADR 0005 — so both providers' migrations carry the same predicate with no
/// branch. A second <b>unique index</b> on <c>(TenantId, IdempotencyKey)</c> enforces enqueue idempotency
/// (PRD 20).
/// </para>
/// </summary>
public sealed class OperationConfiguration : IEntityTypeConfiguration<Operation>
{
    /// <summary>The per-server lock index name (ADR 0005's verbatim DDL).</summary>
    internal const string PerServerLockIndexName = "UX_Operations_ActiveMutating_PerServer";

    /// <summary>The per-server lock filter predicate. Double-quoted identifiers and single-quoted enum-name
    /// literals are accepted identically by both providers (ADR 0005); the enum is stored by name and the
    /// bool as 0/1 (SQLite) / boolean (PostgreSQL), both truthy under <c>WHERE "IsMutating"</c>.</summary>
    internal const string PerServerLockFilter = "\"IsMutating\" AND \"State\" IN ('Pending', 'Running')";

    public void Configure(EntityTypeBuilder<Operation> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("Operations");
        builder.HasKey(o => o.Id);

        builder.Property(o => o.AgentId).IsRequired();
        // ServerId is nullable (host-level operations have none); the typed-id convention maps it.

        builder.Property(o => o.Kind).HasConversion<string>().HasMaxLength(64).IsRequired();
        builder.Property(o => o.State).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(o => o.IdempotencyKey).IsRequired().HasMaxLength(200);
        builder.Property(o => o.StatusLine).HasMaxLength(Operation.MaxReportedTextLength);
        builder.Property(o => o.FailureReason).HasMaxLength(Operation.MaxReportedTextLength);

        // Enqueue idempotency (PRD 20): one Operation per (tenant, key).
        builder.HasIndex(o => new { o.TenantId, o.IdempotencyKey }).IsUnique();

        // Per-server lock (PRD 21, ADR 0005/0022).
        builder.HasIndex(o => o.ServerId)
            .IsUnique()
            .HasDatabaseName(PerServerLockIndexName)
            .HasFilter(PerServerLockFilter);
    }
}
