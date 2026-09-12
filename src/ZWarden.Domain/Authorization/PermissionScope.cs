namespace ZWarden.Domain.Authorization;

/// <summary>
/// Whether a <see cref="Permission"/> can be narrowed to a single Server, or applies across the whole
/// tenant (PRD 12A; ADR 0018). The decision layer uses this to know whether a <c>ServerId</c> is
/// meaningful for a given permission: a <see cref="ServerScopable"/> permission checked with no resource
/// fails closed rather than silently widening, and a grant scoped to one Server never authorizes another.
/// </summary>
public enum PermissionScope
{
    /// <summary>Applies across the whole tenant; a Server resource is not meaningful (e.g. <c>Role.Manage</c>).</summary>
    TenantWide,

    /// <summary>May be granted tenant-wide or narrowed to a single Server (e.g. <c>Server.Start</c>).</summary>
    ServerScopable,
}
