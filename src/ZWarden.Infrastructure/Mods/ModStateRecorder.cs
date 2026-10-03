using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ZWarden.Application.Mods;
using ZWarden.Application.Tenancy;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Mods;
using ZWarden.Domain.Servers;
using ZWarden.Infrastructure.Persistence;
using ZWarden.Infrastructure.Servers;

namespace ZWarden.Infrastructure.Mods;

/// <summary>
/// The <see cref="IModStateRecorder"/> (#290, ADR 0047). Called from the Agent hub when a mod discovery succeeds, in
/// the reporting Agent's tenant scope. Observations are timed by the control-plane clock, not the Agent's, so a boot
/// marked here and the discovery that follows it compare on one clock.
/// </summary>
public sealed partial class ModStateRecorder : IModStateRecorder
{
    private readonly ServerRepository _servers;
    private readonly ServerModStateRepository _states;
    private readonly ServerWorkshopItemRepository _items;
    private readonly ZWardenDbContext _context;
    private readonly IModRefreshScheduler _scheduler;
    private readonly ITenantContext _tenant;
    private readonly TimeProvider _clock;
    private readonly ModRefreshOptions _options;
    private readonly ILogger<ModStateRecorder> _logger;

    public ModStateRecorder(
        ServerRepository servers,
        ServerModStateRepository states,
        ServerWorkshopItemRepository items,
        ZWardenDbContext context,
        IModRefreshScheduler scheduler,
        ITenantContext tenant,
        TimeProvider clock,
        IOptions<ModRefreshOptions> options,
        ILogger<ModStateRecorder> logger)
    {
        ArgumentNullException.ThrowIfNull(servers);
        ArgumentNullException.ThrowIfNull(states);
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(scheduler);
        ArgumentNullException.ThrowIfNull(tenant);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _servers = servers;
        _states = states;
        _items = items;
        _context = context;
        _scheduler = scheduler;
        _tenant = tenant;
        _clock = clock;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task RecordAsync(ModInventory inventory, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        try
        {
            await RecordCoreAsync(inventory, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
#pragma warning disable CA1031 // A cache write must never fail the discovery's completion; the next discovery rebuilds it.
        catch (Exception ex)
        {
            LogRecordFailed(inventory.ServerId, ex);
        }
#pragma warning restore CA1031
    }

    private async Task RecordCoreAsync(ModInventory inventory, CancellationToken cancellationToken)
    {
        Server? server = await _servers.FindByIdAsync(inventory.ServerId, cancellationToken).ConfigureAwait(false);
        if (server is null || server.AgentId != inventory.AgentId)
        {
            return; // Not this tenant's Server, or not this Agent's: ignore (trust-boundaries §3).
        }

        DateTimeOffset now = _clock.GetUtcNow();
        ServerModState? state = await _states.FindAsync(server.Id, cancellationToken).ConfigureAwait(false);
        bool isNew = state is null;
        state ??= ServerModState.For(server.Id);
        try
        {
            state.ObserveConfig(inventory.ConfiguredWorkshopIds, inventory.EnabledModIds, now);
        }
        catch (ArgumentException)
        {
            LogOversizedLists(server.Id);
            return;
        }

        if (isNew)
        {
            _states.Add(state);
        }

        bool needsMetadata = await ReconcileItemsAsync(server.Id, state, inventory, now, cancellationToken).ConfigureAwait(false);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        if (needsMetadata)
        {
            _scheduler.Enqueue(new ModRefreshRequest(_tenant.CurrentTenantId, ModRefreshKind.RefreshMetadata, Server: server.Id));
        }
    }

    // One row per Workshop id that is configured, booted with, or on disk; the rest are pruned. Returns whether any
    // kept row lacks Steam details or has stale ones.
    private async Task<bool> ReconcileItemsAsync(
        ServerId server, ServerModState state, ModInventory inventory, DateTimeOffset now, CancellationToken cancellationToken)
    {
        Dictionary<string, InstalledWorkshopItem> onDisk = new(StringComparer.Ordinal);
        foreach (InstalledWorkshopItem item in inventory.InstalledItems)
        {
            onDisk.TryAdd(item.WorkshopId, item);
        }

        HashSet<string> keep = new(StringComparer.Ordinal);
        foreach (string id in state.ConfiguredWorkshopIds.Concat(state.BootedWorkshopIds).Concat(onDisk.Keys))
        {
            if (IsWorkshopId(id))
            {
                keep.Add(id);
            }
        }

        IReadOnlyList<ServerWorkshopItem> existing = await _items.ListForServerAsync(server, cancellationToken).ConfigureAwait(false);
        Dictionary<string, ServerWorkshopItem> rows = new(StringComparer.Ordinal);
        foreach (ServerWorkshopItem row in existing)
        {
            if (keep.Contains(row.WorkshopId) && rows.TryAdd(row.WorkshopId, row))
            {
                continue;
            }

            _items.Remove(row);
        }

        bool needsMetadata = false;
        int dropped = 0;
        foreach (string id in keep)
        {
            if (!rows.TryGetValue(id, out ServerWorkshopItem? row))
            {
                row = ServerWorkshopItem.Track(server, id);
                _items.Add(row);
            }

            if (onDisk.TryGetValue(id, out InstalledWorkshopItem? disk))
            {
                List<PzModId> modIds = [];
                foreach (InstalledMod mod in disk.Mods)
                {
                    if (PzModId.TryCreate(mod.ModId, out PzModId valid))
                    {
                        modIds.Add(valid);
                    }
                    else
                    {
                        dropped++;
                    }
                }

                row.ObserveDisk(onDisk: true, modIds, now);
            }
            else
            {
                row.ObserveDisk(onDisk: false, [], now);
            }

            needsMetadata |= row.MetadataRefreshedAt is not { } refreshed || now - refreshed >= _options.MetadataMaxAge;
        }

        if (dropped > 0)
        {
            LogDroppedModIds(server, dropped);
        }

        return needsMetadata;
    }

    private static bool IsWorkshopId(string id) =>
        id.Length is > 0 and <= ServerWorkshopItem.MaxWorkshopIdLength && id.All(char.IsAsciiDigit);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Recording the mod state of server {Server} failed; the next discovery rebuilds it.")]
    private partial void LogRecordFailed(ServerId server, Exception ex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Mod discovery for server {Server} reported mod lists over the stored bounds; ignored.")]
    private partial void LogOversizedLists(ServerId server);

    [LoggerMessage(Level = LogLevel.Information, Message = "Dropped {Count} mod.info id(s) for server {Server} that fail the mod-id rule.")]
    private partial void LogDroppedModIds(ServerId server, int count);
}
