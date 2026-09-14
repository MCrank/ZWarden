using ZWarden.Domain.Ids;

namespace ZWarden.Application.Players;

/// <summary>
/// The tenant-scoped read surface over the ban registry (F19; ADR 0027). Every read is scoped to the ambient
/// tenant by construction (ADR 0016). Callers gate visibility with a server-scoped permission before reading;
/// the query itself returns the current tenant's ban records for a Server.
/// </summary>
public interface IBanQuery
{
    /// <summary>The Server's ban records, newest issued first (active and lifted).</summary>
    Task<IReadOnlyList<BanView>> ListForServerAsync(ServerId server, CancellationToken cancellationToken = default);
}
