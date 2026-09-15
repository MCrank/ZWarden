using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ZWarden.Domain.Setup;

namespace ZWarden.Infrastructure.Setup;

/// <summary>
/// Maps the <c>InstallState</c> table (F33) — the single, non-tenant-owned first-run record (<c>ist-</c>).
/// The model conventions supply the typed-id conversion and the concurrency token (ADR 0004/0005); there is
/// no tenant filter because <see cref="InstallState"/> is not <see cref="ZWarden.Domain.Tenancy.ITenantOwned"/>.
/// The declared <see cref="TlsMode"/> is stored by name (additive, never a silent renumber), nullable until
/// the operator confirms it.
/// </summary>
public sealed class InstallStateConfiguration : IEntityTypeConfiguration<InstallState>
{
    public void Configure(EntityTypeBuilder<InstallState> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("InstallState");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.TlsMode).HasConversion<string>().HasMaxLength(32);
    }
}
