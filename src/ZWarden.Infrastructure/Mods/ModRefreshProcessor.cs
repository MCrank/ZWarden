using Microsoft.Extensions.Options;
using ZWarden.Application.Mods;
using ZWarden.Application.Operations;
using ZWarden.Application.Workshop;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Mods;
using ZWarden.Domain.Operations;
using ZWarden.Domain.Servers;
using ZWarden.Infrastructure.Persistence;
using ZWarden.Infrastructure.Servers;

namespace ZWarden.Infrastructure.Mods;

/// <summary>
/// Carries out one <see cref="ModRefreshRequest"/> inside its tenant's scope (#290). It runs from
/// <see cref="ModRefreshWorker"/>, never from a request or the Agent hub.
/// <list type="bullet">
/// <item><b>Discovery:</b> enqueues a system <see cref="OperationKind.ModDiscovery"/> (non-mutating, no actor). The
/// result returns through the hub to <see cref="IModStateRecorder"/>.</item>
/// <item><b>Metadata refresh:</b> one batched keyless Steam call for the Server's items whose details are missing or
/// stale. The Workshop description is re-parsed into the guessed mod ids each time. A lookup that found nothing is not
/// applied.</item>
/// </list>
/// </summary>
public sealed class ModRefreshProcessor
{
    private readonly ServerRepository _servers;
    private readonly ServerWorkshopItemRepository _items;
    private readonly ZWardenDbContext _context;
    private readonly IOperationCoordinator _operations;
    private readonly IWorkshopMetadataClient _metadata;
    private readonly TimeProvider _clock;
    private readonly ModRefreshOptions _options;

    public ModRefreshProcessor(
        ServerRepository servers,
        ServerWorkshopItemRepository items,
        ZWardenDbContext context,
        IOperationCoordinator operations,
        IWorkshopMetadataClient metadata,
        TimeProvider clock,
        IOptions<ModRefreshOptions> options)
    {
        ArgumentNullException.ThrowIfNull(servers);
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(operations);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(options);
        _servers = servers;
        _items = items;
        _context = context;
        _operations = operations;
        _metadata = metadata;
        _clock = clock;
        _options = options.Value;
    }

    /// <summary>Carries out <paramref name="request"/>.</summary>
    public async Task ProcessAsync(ModRefreshRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        switch (request)
        {
            case { Kind: ModRefreshKind.DiscoverServer, Server: { } server }:
                if (await _servers.FindByIdAsync(server, cancellationToken).ConfigureAwait(false) is { } found)
                {
                    await DiscoverAsync(found, cancellationToken).ConfigureAwait(false);
                }

                break;

            case { Kind: ModRefreshKind.DiscoverAgentServers, Agent: { } agent }:
                foreach (Server owned in await _servers.ListByAgentAsync(agent, cancellationToken).ConfigureAwait(false))
                {
                    await DiscoverAsync(owned, cancellationToken).ConfigureAwait(false);
                }

                break;

            case { Kind: ModRefreshKind.RefreshMetadata, Server: { } server }:
                await RefreshMetadataAsync(server, request.MaxAge ?? _options.MetadataMaxAge, cancellationToken)
                    .ConfigureAwait(false);
                break;
        }
    }

    // A read: non-mutating, so it takes no per-Server lock and runs beside a busy server. No actor: the system asked.
    private Task<Operation> DiscoverAsync(Server server, CancellationToken cancellationToken) =>
        _operations.EnqueueAsync(
            new EnqueueOperationRequest(
                server.AgentId, OperationKind.ModDiscovery, IsMutating: false, Guid.NewGuid().ToString("N"),
                ServerId: server.Id),
            actor: null,
            cancellationToken);

    // Asks Steam directly (never the client's in-memory cache): the stored refresh time is the freshness that counts,
    // and the Update-ready check (#275) needs the current time_updated.
    private async Task RefreshMetadataAsync(ServerId server, TimeSpan maxAge, CancellationToken cancellationToken)
    {
        DateTimeOffset now = _clock.GetUtcNow();
        List<ServerWorkshopItem> due = [.. (await _items.ListForServerAsync(server, cancellationToken).ConfigureAwait(false))
            .Where(i => i.MetadataRefreshedAt is not { } refreshed || now - refreshed >= maxAge)];
        if (due.Count == 0)
        {
            return;
        }

        IReadOnlyList<WorkshopItemMetadata> fetched = await _metadata
            .RefreshItemsAsync([.. due.Select(i => i.WorkshopId)], cancellationToken).ConfigureAwait(false);
        Dictionary<string, WorkshopItemMetadata> byId = fetched
            .Where(m => m.Found)
            .GroupBy(m => m.WorkshopId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        foreach (ServerWorkshopItem item in due)
        {
            if (byId.TryGetValue(item.WorkshopId, out WorkshopItemMetadata? metadata))
            {
                item.ApplyMetadata(
                    metadata.Title, metadata.PreviewUrl, metadata.SizeBytes, metadata.UpdatedAt, metadata.Tags,
                    WorkshopDescriptionModIds.Parse(metadata.Description), now);
            }
        }

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
