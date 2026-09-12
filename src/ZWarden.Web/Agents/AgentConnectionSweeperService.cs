using Microsoft.Extensions.Options;
using ZWarden.Infrastructure.Agents;

namespace ZWarden.Web.Agents;

/// <summary>
/// The timer that drives <see cref="AgentConnectionSweeper"/> (F10) — the connection monitor's heartbeat. On
/// each tick it opens a scope and reconciles stale connections to disconnected. A sweep is durable maintenance
/// that must survive a transient database error, so a failed tick is logged and the loop continues rather than
/// tearing the service down.
/// </summary>
public sealed partial class AgentConnectionSweeperService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;
    private readonly AgentConnectionMonitorOptions _options;
    private readonly ILogger<AgentConnectionSweeperService> _logger;

    public AgentConnectionSweeperService(
        IServiceScopeFactory scopeFactory,
        TimeProvider timeProvider,
        IOptions<AgentConnectionMonitorOptions> options,
        ILogger<AgentConnectionSweeperService> logger)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _scopeFactory = scopeFactory;
        _timeProvider = timeProvider;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            using PeriodicTimer timer = new(_options.SweepInterval, _timeProvider);
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                await SweepOnceAsync(stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown.
        }
    }

    private async Task SweepOnceAsync(CancellationToken stoppingToken)
    {
        try
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            AgentConnectionSweeper sweeper = scope.ServiceProvider.GetRequiredService<AgentConnectionSweeper>();
            int reconciled = await sweeper.SweepAsync(stoppingToken).ConfigureAwait(false);
            if (reconciled > 0)
            {
                LogReconciled(reconciled);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
#pragma warning disable CA1031 // A maintenance sweep must not terminate the loop on a transient error.
        catch (Exception ex)
        {
            LogSweepFailed(ex);
        }
#pragma warning restore CA1031
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Reconciled {Count} stale Agent connection(s) to disconnected.")]
    private partial void LogReconciled(int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The Agent connection sweep failed; will retry on the next tick.")]
    private partial void LogSweepFailed(Exception ex);
}
