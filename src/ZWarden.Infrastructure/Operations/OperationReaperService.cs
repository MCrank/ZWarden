using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ZWarden.Infrastructure.Operations;

/// <summary>
/// The timer that drives <see cref="OperationReaper"/> (F11) — the lease-expiry safety net. On each tick it
/// opens a scope and fails any Operation whose lease has expired, releasing its per-server lock. Reaping is
/// durable maintenance that must survive a transient database error, so a failed tick is logged and the loop
/// continues rather than tearing the service down. Runs on ZWarden.Web, the single writing process
/// (ADR 0005 condition 4).
/// </summary>
public sealed partial class OperationReaperService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;
    private readonly OperationEngineOptions _options;
    private readonly ILogger<OperationReaperService> _logger;

    public OperationReaperService(
        IServiceScopeFactory scopeFactory,
        TimeProvider timeProvider,
        IOptions<OperationEngineOptions> options,
        ILogger<OperationReaperService> logger)
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
            using PeriodicTimer timer = new(_options.ReaperInterval, _timeProvider);
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                await ReapOnceAsync(stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown.
        }
    }

    private async Task ReapOnceAsync(CancellationToken stoppingToken)
    {
        try
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            OperationReaper reaper = scope.ServiceProvider.GetRequiredService<OperationReaper>();
            int reaped = await reaper.ReapAsync(stoppingToken).ConfigureAwait(false);
            if (reaped > 0)
            {
                LogReaped(reaped);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
#pragma warning disable CA1031 // A maintenance sweep must not terminate the loop on a transient error.
        catch (Exception ex)
        {
            LogReapFailed(ex);
        }
#pragma warning restore CA1031
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Reaped {Count} lease-expired operation(s) to failed.")]
    private partial void LogReaped(int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The operation lease sweep failed; will retry on the next tick.")]
    private partial void LogReapFailed(Exception ex);
}
