using Microsoft.Extensions.DependencyInjection;
using ZWarden.Application.Players;

namespace ZWarden.Infrastructure.Players;

/// <summary>
/// Composition seam for player management (F19). Registers the tenant-scoped <see cref="BanRecordRepository"/>
/// and the operator-facing <see cref="IPlayerManagement"/>. Call it after <c>AddZWardenAuthorization</c>,
/// <c>AddZWardenAudit</c>, <c>AddZWardenOperations</c>, and <c>AddZWardenServers</c> — the service resolves the
/// fail-closed <c>IPermissionChecker</c>, the <c>IAuditWriter</c>, the <c>IOperationCoordinator</c>, the
/// tenant-scoped <c>ServerRepository</c>, the request-scoped <c>ZWardenDbContext</c>, and the clock.
/// </summary>
public static class PlayersServiceCollectionExtensions
{
    public static IServiceCollection AddZWardenPlayers(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<BanRecordRepository>();
        services.AddScoped<IPlayerManagement, PlayerManagement>();
        services.AddScoped<IBanQuery, BanQuery>();

        // The latest-roster-per-Server cache (F19), beside the F16 metrics/health caches: an in-process singleton
        // that feeds the live roster island. Never persisted — a roster is display data, not durable state.
        services.AddSingleton<IPlayerRosterCache, PlayerRosterCache>();

        return services;
    }
}
