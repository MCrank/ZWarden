using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ZWarden.Application.Setup;
using ZWarden.Domain.Setup;
using ZWarden.Infrastructure.Persistence;

namespace ZWarden.Infrastructure.Setup;

/// <summary>
/// Startup steps for the first-run setup record (F33), the setup counterpart to F3A's
/// <see cref="ZWarden.Infrastructure.Tenancy.TenantBootstrapper"/>. Idempotent — safe on every boot.
/// </summary>
public static class SetupBootstrapper
{
    /// <summary>Inserts the empty <see cref="InstallState"/> singleton if it is absent, so the first-run
    /// gate always has a row to read. A sanctioned unscoped read/write of a non-tenant-owned table.</summary>
    public static async Task EnsureInstallStateAsync(
        IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);
        using IServiceScope scope = services.CreateScope();
        ZWardenDbContext context = scope.ServiceProvider.GetRequiredService<ZWardenDbContext>();

        bool exists = await context.Set<InstallState>()
            .AnyAsync(s => s.Id == InstallState.DefaultId, cancellationToken)
            .ConfigureAwait(false);
        if (exists)
        {
            return;
        }

        context.Add(InstallState.CreateDefault());
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Marks setup complete out of band (headless deploys that seed the first administrator from
    /// configuration never see the wizard). Idempotent.</summary>
    public static async Task MarkSetupCompleteAsync(
        IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);
        using IServiceScope scope = services.CreateScope();
        ISetupState setup = scope.ServiceProvider.GetRequiredService<ISetupState>();
        await setup.MarkSetupCompleteAsync(cancellationToken).ConfigureAwait(false);
    }
}
