using ZWarden.Domain.Ids;

namespace ZWarden.Diagnostics.SupportPackage;

/// <summary>
/// Orchestrates support-package generation (F30): authorize <c>Diagnostics.Export</c> fail-closed, collect the F29
/// report and the environment facts, run the pure pipeline (<see cref="ISupportPackageBuilder"/>), and audit the
/// outcome — including a fail-closed secret-scan abort. On success it returns a <see cref="BuiltSupportPackage"/>
/// the Web-side writer ZIPs and streams; it performs no I/O itself. A package is transient (F30 D-2): the only
/// durable record is the <c>Diagnostics.Export</c> audit event.
/// </summary>
public interface ISupportPackageService
{
    /// <summary>Generates the tenant-wide support package (the platform + connected-Agent host domains) for
    /// <paramref name="user"/>. Fail-closed on <c>Diagnostics.Export</c>; a detected secret aborts generation.</summary>
    Task<SupportPackageResult> CreateTenantPackageAsync(UserId user, CancellationToken cancellationToken = default);

    /// <summary>Generates the per-server support package for <paramref name="serverId"/> (the caller resolved the
    /// Server through the tenant filter and supplies its <paramref name="owningAgentId"/>). Fail-closed on
    /// <c>Diagnostics.Export</c>; a detected secret aborts generation.</summary>
    Task<SupportPackageResult> CreateServerPackageAsync(
        UserId user, ServerId serverId, AgentId owningAgentId, CancellationToken cancellationToken = default);
}
