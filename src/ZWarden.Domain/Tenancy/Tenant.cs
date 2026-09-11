using ZWarden.Domain.Ids;

namespace ZWarden.Domain.Tenancy;

/// <summary>
/// The owner of every tenant-owned record (PRD 7A). A self-hosted installation has one Tenant; a
/// hosted installation has many. A Tenant is <b>not</b> <see cref="ITenantOwned"/> — it is not owned
/// by a tenant, it <i>is</i> one — so the tenant filter never applies to it, and the narrow reads
/// against the tenant table (the startup bootstrap, and F3D administration later) are the sanctioned
/// unscoped reads (ADR 0016). Membership, invitations, settings, and the Auth0-organization mapping
/// are later features (F3B/F3D); F3A's Tenant is deliberately minimal.
/// </summary>
public sealed class Tenant : IVersioned
{
    /// <summary>The internal ZWarden tenant identifier (<c>ten-&lt;uuid&gt;</c>); never an Auth0
    /// organization id, which is a separate external reference (PRD 63A).</summary>
    public TenantId Id { get; init; }

    /// <summary>A human-readable display name for the tenant.</summary>
    public string Name { get; set; } = string.Empty;

    /// <inheritdoc />
    public Guid Version { get; set; }

    /// <summary>
    /// The fixed, well-known identifier of the single tenant in a self-hosted installation (ADR 0016).
    /// A compile-time constant — deterministic across restarts, environments, and test runs — so the
    /// bootstrap and the single-tenant context can reference it without a prior read. It is an
    /// identifier, not a secret; cross-tenant access is denied by the filter regardless of who knows it.
    /// </summary>
    public static TenantId DefaultId { get; } = new(new Guid("01920000-0000-7000-8000-000000000001"));

    /// <summary>The display name seeded onto the self-hosted default tenant.</summary>
    public const string DefaultName = "Default";

    /// <summary>Builds the self-hosted default <see cref="Tenant"/> (its <see cref="Version"/> is
    /// stamped by the persistence layer on insert).</summary>
    public static Tenant CreateDefault() => new() { Id = DefaultId, Name = DefaultName };
}
