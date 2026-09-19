using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ZWarden.Domain.Workshop;

namespace ZWarden.Infrastructure.Workshop;

/// <summary>
/// Maps the <c>WorkshopIntegrationSettings</c> table (F110 PR-B; ADR 0044). The aggregate is tenant-owned and
/// versioned, so the model conventions supply the typed-id conversions, the concurrency token, and the tenant
/// filter (ADR 0016) with no wiring here. <see cref="WorkshopIntegrationSettings.ProtectedApiKey"/> is stored
/// as the opaque <c>ISecretProtector</c> envelope (ADR 0015) — the application layer encrypts before writing,
/// so the column is a plain string, never a value-converted secret. A <b>unique index on <c>TenantId</c></b>
/// enforces the one-row-per-tenant invariant at the database, not just in code.
/// </summary>
public sealed class WorkshopIntegrationSettingsConfiguration : IEntityTypeConfiguration<WorkshopIntegrationSettings>
{
    public void Configure(EntityTypeBuilder<WorkshopIntegrationSettings> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("WorkshopIntegrationSettings");
        builder.HasKey(s => s.Id);

        // The AEAD envelope (or the empty string in keyless mode); a Steam key envelope is well under 2 KiB.
        builder.Property(s => s.ProtectedApiKey).IsRequired().HasMaxLength(4096);

        // One settings row per tenant (ADR 0016 scopes reads; this guarantees uniqueness).
        builder.HasIndex(nameof(WorkshopIntegrationSettings.TenantId)).IsUnique();
    }
}
