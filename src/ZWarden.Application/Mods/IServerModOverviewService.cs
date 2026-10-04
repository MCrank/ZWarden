using ZWarden.Domain.Ids;

namespace ZWarden.Application.Mods;

/// <summary>
/// Reads a Server's mods with their statuses (#290, for the #292 Mods page): items and mod ids marked Active /
/// installs on restart / removed on restart / leftover, with Steam details, from the persisted mod state.
/// Authorizes <c>Mod.View</c> on the Server (ADR 0018); returns <c>null</c> for an unknown, foreign or unauthorized
/// Server.
/// </summary>
public interface IServerModOverviewService
{
    /// <summary>The overview of <paramref name="server"/> for <paramref name="user"/>, or <c>null</c>.</summary>
    Task<ServerModOverview?> GetAsync(UserId user, ServerId server, CancellationToken cancellationToken = default);

    /// <summary>Asks for a background refresh of <paramref name="server"/>'s Steam details when they are older than the
    /// update-check max age (#275 D4: opening the Mods page). Does nothing for a caller without <c>Mod.View</c>.</summary>
    Task RequestUpdateCheckAsync(UserId user, ServerId server, CancellationToken cancellationToken = default);

    /// <summary>How many Workshop items a restart would update on each of <paramref name="servers"/> (#275, the fleet
    /// hint). Servers the caller can't <c>Mod.View</c>, or that are unknown, are left out.</summary>
    Task<IReadOnlyDictionary<ServerId, int>> CountUpdatesReadyAsync(
        UserId user, IReadOnlyList<ServerId> servers, CancellationToken cancellationToken = default);
}
