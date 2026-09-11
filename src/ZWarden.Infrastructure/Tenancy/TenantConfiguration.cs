using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ZWarden.Domain.Tenancy;

namespace ZWarden.Infrastructure.Tenancy;

/// <summary>Maps the <c>Tenants</c> table (F3A's first production table). The typed-id and version
/// conventions supply the key conversion and concurrency token; this only declares the key and the
/// required name. A Tenant is not <see cref="ITenantOwned"/>, so it carries no tenant filter.</summary>
public sealed class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Name).IsRequired();
    }
}
