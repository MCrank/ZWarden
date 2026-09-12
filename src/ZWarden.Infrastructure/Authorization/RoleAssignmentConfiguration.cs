using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ZWarden.Domain.Authorization;

namespace ZWarden.Infrastructure.Authorization;

/// <summary>
/// Maps the <c>RoleAssignments</c> table (F5) — the tenant-owned user→role binding (<c>prm-</c>). The
/// conventions supply the typed-id conversions (including the nullable <c>ServerId?</c> server scope), the
/// concurrency token, and the tenant filter (ADR 0016). Neither <c>RoleId</c> nor <c>ServerId</c> carries a
/// relational FK: the Server entity is a later feature (Track D), and F5 authorizes against ids, with the
/// decision resolver failing closed on a dangling reference (a missing role grants nothing). Role deletion
/// removes its assignments explicitly (F5 custom-role management), not by cascade.
/// </summary>
public sealed class RoleAssignmentConfiguration : IEntityTypeConfiguration<RoleAssignment>
{
    public void Configure(EntityTypeBuilder<RoleAssignment> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("RoleAssignments");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.UserId).IsRequired();
        builder.Property(a => a.RoleId).IsRequired();

        // A typed-id struct is not a primitive EF discovers on its own; declare the optional server scope
        // so it is a mapped property, and the model's typed-id convention attaches the (nullable-aware)
        // ServerId <-> Guid converter (ADR 0014), storing null for a tenant-wide assignment.
        builder.Property(a => a.ServerId);

        // Membership checks read a tenant's assignments for a user; index that access path.
        builder.HasIndex(nameof(RoleAssignment.TenantId), nameof(RoleAssignment.UserId));
    }
}
