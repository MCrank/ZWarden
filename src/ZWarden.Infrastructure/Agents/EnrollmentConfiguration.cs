using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ZWarden.Domain.Enrollments;

namespace ZWarden.Infrastructure.Agents;

/// <summary>
/// Maps the <c>Enrollments</c> table (F9) — the one-time enrollment credential (<c>enr-</c>). The model
/// conventions supply the typed-id conversions (including the nullable <c>ConsumedByAgent</c>), the
/// concurrency token, and the tenant filter (ADR 0016). Only the secret's one-way <b>hash</b> is stored
/// (decision 2); it is indexed because the exchange resolves an enrollment by hashing the presented secret.
/// <c>ConsumedByAgent</c> carries no relational FK — the Agent row is created in the same exchange and the
/// reference is provenance, not a constraint.
/// </summary>
public sealed class EnrollmentConfiguration : IEntityTypeConfiguration<Enrollment>
{
    public void Configure(EntityTypeBuilder<Enrollment> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("Enrollments");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.SecretHash).IsRequired().HasMaxLength(64);
        builder.Property(e => e.CreatedBy).IsRequired();
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(32);
        builder.Property(e => e.Label).HasMaxLength(200);

        // A nullable typed-id struct is not discovered by EF on its own; declare it so the model's
        // typed-id convention attaches the nullable-aware converter (ADR 0014), storing null until consumed.
        builder.Property(e => e.ConsumedByAgent);

        // The exchange finds an enrollment by the hash of the presented secret.
        builder.HasIndex(e => e.SecretHash);
    }
}
