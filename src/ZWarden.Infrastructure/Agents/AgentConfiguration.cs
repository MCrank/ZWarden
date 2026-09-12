using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ZWarden.Domain.Agents;

namespace ZWarden.Infrastructure.Agents;

/// <summary>
/// Maps the <c>Agents</c> table (F9) — the trusted Agent record (<c>agt-</c>). The model conventions supply
/// the typed-id conversions, the concurrency token, and the tenant filter (ADR 0016). Only the credential's
/// one-way <b>hash</b> is stored (decision 2), and it is indexed because the verifier resolves an Agent by
/// hashing the presented credential; a revoked Agent stores an empty hash, which no presented credential
/// matches. <c>EnrolledVia</c> carries no relational FK (provenance).
/// </summary>
public sealed class AgentConfiguration : IEntityTypeConfiguration<Agent>
{
    public void Configure(EntityTypeBuilder<Agent> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("Agents");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.CredentialHash).IsRequired().HasMaxLength(64);
        builder.Property(a => a.EnrolledVia).IsRequired();
        builder.Property(a => a.Label).HasMaxLength(200);

        // Observed connection state (F10) — stored beside the trust state, never conflated with it. The enum
        // is stored by name (the model convention), and LastSeenAt/LastProtocolVersion are nullable until the
        // Agent first connects.
        builder.Property(a => a.ConnectionState).HasConversion<string>().HasMaxLength(32).IsRequired();

        // The verifier finds an Agent by the hash of the presented credential.
        builder.HasIndex(a => a.CredentialHash);
    }
}
