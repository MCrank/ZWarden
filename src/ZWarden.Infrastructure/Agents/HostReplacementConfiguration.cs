using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ZWarden.Domain.Agents;

namespace ZWarden.Infrastructure.Agents;

/// <summary>
/// Maps the <c>HostReplacements</c> table (#368, ADR 0049). Tenant-owned, so the model conventions supply the typed-id
/// conversion and the tenant filter (ADR 0016). No relational FK to <c>Agents</c>: the predecessor is deleted by the
/// replacement itself, and removing the successor deletes its rows in code. Indexed by successor, which is how the
/// Agent's inherited ids are read on every connect.
/// </summary>
public sealed class HostReplacementConfiguration : IEntityTypeConfiguration<HostReplacement>
{
    public void Configure(EntityTypeBuilder<HostReplacement> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("HostReplacements");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.SuccessorId).IsRequired();
        builder.Property(r => r.PredecessorId).IsRequired();
        builder.Property(r => r.ReplacedBy).IsRequired();
        builder.Property(r => r.ReplacedAt).IsRequired();
        builder.HasIndex(r => r.SuccessorId);
        builder.HasIndex(r => r.PredecessorId).IsUnique();
    }
}
