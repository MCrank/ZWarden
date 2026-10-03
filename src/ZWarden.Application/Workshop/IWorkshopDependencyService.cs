using ZWarden.Domain.Ids;

namespace ZWarden.Application.Workshop;

/// <summary>
/// A Workshop item's required items — Steam's "children" of the item — offered alongside it at one-click Install
/// (#291 D5). Reading them needs the tenant's optional Steam Web API key (ADR 0044), so without a key nothing is
/// offered. Fail-closed (ADR 0018): the caller must hold <c>Mod.View</c> on the Server before any outbound call. It
/// never throws — every failure (no key, rejected key, Steam down, malformed reply) is an empty list. Returned
/// metadata is untrusted external data (trust-boundaries §8).
/// </summary>
public interface IWorkshopDependencyService
{
    /// <summary>The items <paramref name="workshopId"/> requires, in Steam's order, with their keyless details;
    /// empty when there are none or they can't be read.</summary>
    Task<IReadOnlyList<WorkshopItemMetadata>> GetRequiredItemsAsync(
        UserId actor, ServerId server, string workshopId, CancellationToken cancellationToken = default);
}
