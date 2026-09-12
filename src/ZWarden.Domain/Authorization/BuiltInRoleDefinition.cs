namespace ZWarden.Domain.Authorization;

/// <summary>
/// The fixed definition of a built-in role (PRD 12A): its <see cref="Kind"/>, its display
/// <see cref="Name"/>, and the closed permission bundle it seeds with. Pure data — the seeder
/// (Infrastructure) materializes a tenant-owned <see cref="Role"/> from one of these per tenant,
/// idempotently. Bundles are drawn only from the <see cref="Permissions"/> catalogue.
/// </summary>
/// <param name="Kind">Which built-in role this defines.</param>
/// <param name="Name">The human-readable role name seeded onto the tenant's role.</param>
/// <param name="Permissions">The permissions the role grants — a closed subset of the catalogue.</param>
public sealed record BuiltInRoleDefinition(
    BuiltInRoleKind Kind,
    string Name,
    IReadOnlyList<PermissionDefinition> Permissions);
