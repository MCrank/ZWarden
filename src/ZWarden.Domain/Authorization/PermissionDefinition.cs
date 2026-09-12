namespace ZWarden.Domain.Authorization;

/// <summary>
/// A single named capability in the closed permission catalogue (PRD 12A). The <see cref="Name"/> is the
/// stable, machine-readable currency used on the wire, in the database, and as an ASP.NET Core policy
/// name — never a free-form string invented at a call site. Definitions live only in
/// <see cref="Permissions"/>; equality is by name so a definition compares equal to its catalogue entry.
/// </summary>
/// <remarks>
/// Named <c>PermissionDefinition</c> rather than <c>Permission</c> because <c>Permission</c> is a reserved
/// type suffix (CA1711, for <c>System.Security.IPermission</c> types); the domain term stays "Permission".
/// </remarks>
/// <param name="Name">The stable dotted name, e.g. <c>Server.Start</c> (PRD 12A permission naming).</param>
/// <param name="Scope">Whether the permission is tenant-wide or narrowable to a single Server.</param>
public sealed record PermissionDefinition(string Name, PermissionScope Scope)
{
    /// <summary>The stable name is the identity of a permission; scope is metadata about it.</summary>
    public bool Equals(PermissionDefinition? other) =>
        other is not null && string.Equals(Name, other.Name, StringComparison.Ordinal);

    /// <inheritdoc />
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Name);

    /// <inheritdoc />
    public override string ToString() => Name;
}
