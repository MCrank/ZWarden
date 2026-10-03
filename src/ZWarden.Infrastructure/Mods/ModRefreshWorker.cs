using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ZWarden.Application.Mods;
using ZWarden.Infrastructure.Tenancy;

namespace ZWarden.Infrastructure.Mods;

/// <summary>
/// Drains the <see cref="ModRefreshScheduler"/> (#290 D1). Each request runs in its own DI scope assigned to the
/// request's tenant (<see cref="TenantScopes"/>), through <see cref="ModRefreshProcessor"/>. A failure (Agent
/// offline, Steam down, a database hiccup) is logged and dropped: the next trigger retries, and nothing here can stop
/// the loop.
/// </summary>
public sealed partial class ModRefreshWorker : BackgroundService
{
    private readonly ModRefreshScheduler _scheduler;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ModRefreshWorker> _logger;

    public ModRefreshWorker(ModRefreshScheduler scheduler, IServiceScopeFactory scopeFactory, ILogger<ModRefreshWorker> logger)
    {
        ArgumentNullException.ThrowIfNull(scheduler);
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(logger);
        _scheduler = scheduler;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                ModRefreshRequest request = await _scheduler.ReadAsync(stoppingToken).ConfigureAwait(false);
                await ProcessOnceAsync(request, stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown.
        }
        catch (System.Threading.Channels.ChannelClosedException)
        {
            // The scheduler was disposed on shutdown.
        }
    }

    private async Task ProcessOnceAsync(ModRefreshRequest request, CancellationToken stoppingToken)
    {
        try
        {
            await using AsyncServiceScope scope = _scopeFactory.CreateTenantScope(request.Tenant);
            await scope.ServiceProvider.GetRequiredService<ModRefreshProcessor>()
                .ProcessAsync(request, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // One failed refresh must not stop the loop; the next trigger retries.
        catch (Exception ex)
        {
            LogRefreshFailed(request.Kind, ex);
        }
#pragma warning restore CA1031
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "A background mod refresh ({Kind}) failed; the next trigger retries.")]
    private partial void LogRefreshFailed(ModRefreshKind kind, Exception ex);
}
