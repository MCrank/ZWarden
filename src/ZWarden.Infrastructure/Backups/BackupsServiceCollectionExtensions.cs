using Microsoft.Extensions.DependencyInjection;
using ZWarden.Application.Backups;

namespace ZWarden.Infrastructure.Backups;

/// <summary>
/// Composition seam for backups (F24). Registers the tenant-scoped <see cref="BackupRepository"/> and the
/// operator-facing services: <see cref="IServerBackup"/> (take/delete), the <see cref="IPreOperationBackup"/> seam
/// (the same concrete service), the <see cref="IBackupRecorder"/> ingest, and the <see cref="IBackupQuery"/> read
/// surface. Call it after <c>AddZWardenAuthorization</c>, <c>AddZWardenAudit</c>, <c>AddZWardenOperations</c>, and
/// <c>AddZWardenServers</c> — the services resolve the fail-closed <c>IPermissionChecker</c>, the
/// <c>IAuditWriter</c>, the <c>IOperationCoordinator</c>/<c>IOperationStore</c>, the tenant-scoped
/// <c>ServerRepository</c>, and the request-scoped <c>ZWardenDbContext</c>.
/// </summary>
public static class BackupsServiceCollectionExtensions
{
    public static IServiceCollection AddZWardenBackups(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<BackupRepository>();
        services.AddScoped<ServerBackup>();
        services.AddScoped<IServerBackup>(sp => sp.GetRequiredService<ServerBackup>());
        services.AddScoped<IPreOperationBackup>(sp => sp.GetRequiredService<ServerBackup>());
        services.AddScoped<IBackupRecorder, BackupRecorder>();
        services.AddScoped<IBackupQuery, BackupQuery>();

        return services;
    }
}
