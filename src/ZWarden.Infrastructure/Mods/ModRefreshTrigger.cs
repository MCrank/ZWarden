using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ZWarden.Application.Configuration;
using ZWarden.Application.Mods;
using ZWarden.Application.Tenancy;
using ZWarden.Domain.Configuration;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Mods;
using ZWarden.Domain.Operations;
using ZWarden.Infrastructure.Operations;
using ZWarden.Infrastructure.Persistence;

namespace ZWarden.Infrastructure.Mods;

/// <summary>
/// The <see cref="IModRefreshTrigger"/> (#290 D1/D2). Runs in the hub's scope, which carries the reporting Agent's
/// tenant. It writes at most one small row (the boot mark) and queues work; a failure is logged and swallowed so a
/// mod refresh can never fail an operation's completion.
/// </summary>
public sealed partial class ModRefreshTrigger : IModRefreshTrigger
{
    private readonly OperationRepository _operations;
    private readonly ServerModStateRepository _states;
    private readonly ZWardenDbContext _context;
    private readonly IModRefreshScheduler _scheduler;
    private readonly IModInventoryCache _inventory;
    private readonly ITenantContext _tenant;
    private readonly TimeProvider _clock;
    private readonly ModRefreshOptions _options;
    private readonly ILogger<ModRefreshTrigger> _logger;

    public ModRefreshTrigger(
        OperationRepository operations,
        ServerModStateRepository states,
        ZWardenDbContext context,
        IModRefreshScheduler scheduler,
        IModInventoryCache inventory,
        ITenantContext tenant,
        TimeProvider clock,
        IOptions<ModRefreshOptions> options,
        ILogger<ModRefreshTrigger> logger)
    {
        ArgumentNullException.ThrowIfNull(operations);
        ArgumentNullException.ThrowIfNull(states);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(scheduler);
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(tenant);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _operations = operations;
        _states = states;
        _context = context;
        _scheduler = scheduler;
        _inventory = inventory;
        _tenant = tenant;
        _clock = clock;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task OperationSucceededAsync(
        OperationId operationId, AgentId reportingAgent, CancellationToken cancellationToken = default)
    {
        try
        {
            Operation? operation = await _operations.FindByIdAsync(operationId, cancellationToken).ConfigureAwait(false);
            if (operation is not { ServerId: { } server } || operation.AgentId != reportingAgent)
            {
                return;
            }

            ModRefreshRequest discover = new(_tenant.CurrentTenantId, ModRefreshKind.DiscoverServer, Server: server);
            if (IsBoot(operation.Kind))
            {
                ServerModState? state = await _states.FindAsync(server, cancellationToken).ConfigureAwait(false);
                if (state is null)
                {
                    state = ServerModState.For(server);
                    _states.Add(state);
                }

                state.MarkBooted(_clock.GetUtcNow());
                await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                // Now: PZ has read servertest.ini, so this discovery fills the booted-with lists. Later: PZ downloads
                // new Workshop items after Docker reports the start, so look at the disk again (D2).
                _scheduler.Enqueue(discover);
                _scheduler.EnqueueAfter(discover, _options.PostBootRediscoverDelay);
            }
            else if (operation.Kind is OperationKind.ConfigApply or OperationKind.ConfigApplyRaw)
            {
                _scheduler.Enqueue(discover);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
#pragma warning disable CA1031 // A background mod refresh must never fail the operation's completion.
        catch (Exception ex)
        {
            LogTriggerFailed(operationId, ex);
        }
#pragma warning restore CA1031
    }

    /// <inheritdoc />
    public async Task RecordAppliedModListsAsync(
        OperationId operationId, AgentId reportingAgent, CancellationToken cancellationToken = default)
    {
        try
        {
            Operation? operation = await _operations.FindByIdAsync(operationId, cancellationToken).ConfigureAwait(false);
            if (operation is not { Kind: OperationKind.ConfigApply, ServerId: { } server, CommandPayload: { } json }
                || operation.AgentId != reportingAgent
                || _inventory.GetLatest(server, reportingAgent) is not { } current)
            {
                return;
            }

            ConfigApplyPayload payload = ConfigApplyPayload.FromJson(json);
            if (payload.File != PzConfigFile.Ini)
            {
                return;
            }

            // Each edit replaces a whole list value, exactly as the Agent wrote it.
            ModInventory patched = current;
            foreach (ConfigApplyEdit edit in payload.Edits)
            {
                if (string.Equals(edit.Path, WorkshopItemsKey, StringComparison.Ordinal))
                {
                    patched = patched with { ConfiguredWorkshopIds = SplitList(edit.Value) };
                }
                else if (string.Equals(edit.Path, ModsKey, StringComparison.Ordinal))
                {
                    patched = patched with { EnabledModIds = SplitList(edit.Value) };
                }
            }

            if (!ReferenceEquals(patched, current))
            {
                _inventory.Record(patched);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
#pragma warning disable CA1031 // Patching the cache must never fail the operation's completion; discovery corrects it.
        catch (Exception ex)
        {
            LogTriggerFailed(operationId, ex);
        }
#pragma warning restore CA1031
    }

    private const string WorkshopItemsKey = "WorkshopItems";
    private const string ModsKey = "Mods";

    private static string[] SplitList(string value) =>
        value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <inheritdoc />
    public void AgentConnected(AgentId agent) =>
        _scheduler.Enqueue(new ModRefreshRequest(_tenant.CurrentTenantId, ModRefreshKind.DiscoverAgentServers, Agent: agent));

    // The kinds after which PZ has (re)started and re-read its config: start, restart (incl. #114 graceful), update
    // (#273 restarts inside the Agent) and recreate.
    private static bool IsBoot(OperationKind kind) =>
        kind is OperationKind.StartServer or OperationKind.RestartServer or OperationKind.UpdateServer
            or OperationKind.RecreateServer;

    [LoggerMessage(Level = LogLevel.Warning, Message = "Queuing a mod refresh after operation {Operation} failed.")]
    private partial void LogTriggerFailed(OperationId operation, Exception ex);
}
