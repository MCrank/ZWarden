using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ZWarden.Infrastructure.Tenancy;

namespace ZWarden.Infrastructure.Mods;

/// <summary>
/// The timer that drives <see cref="ModUpdateCheck"/> (#275 D4) every <see cref="ModRefreshOptions.UpdateCheckInterval"/>,
/// so the Mods page, restart bar and fleet board learn of Workshop mod updates on servers nobody restarts. It only
/// queues work for <see cref="ModRefreshWorker"/>; a failed tick is logged and the loop continues, like
/// <c>OperationReaperService</c>.
/// </summary>
public sealed partial class ModUpdateCheckService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;
    private readonly ModRefreshOptions _options;
    private readonly ILogger<ModUpdateCheckService> _logger;

    public ModUpdateCheckService(
        IServiceScopeFactory scopeFactory,
        TimeProvider timeProvider,
        IOptions<ModRefreshOptions> options,
        ILogger<ModUpdateCheckService> logger)
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
            using PeriodicTimer timer = new(_options.UpdateCheckInterval, _timeProvider);
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                await CheckOnceAsync(stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown.
        }
    }

    private async Task CheckOnceAsync(CancellationToken stoppingToken)
    {
        try
        {
            await using AsyncServiceScope scope = _scopeFactory.CreateSystemScope();
            int queued = await scope.ServiceProvider.GetRequiredService<ModUpdateCheck>()
                .RunAsync(stoppingToken).ConfigureAwait(false);
            LogQueued(queued);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
#pragma warning disable CA1031 // A best-effort refresh must not terminate the loop on a transient error.
        catch (Exception ex)
        {
            LogCheckFailed(ex);
        }
#pragma warning restore CA1031
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Mod-update check queued a Steam refresh for {Count} server(s).")]
    private partial void LogQueued(int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The mod-update check failed; will retry on the next tick.")]
    private partial void LogCheckFailed(Exception ex);
}
