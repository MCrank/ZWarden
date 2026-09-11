using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ZWarden.Application.Tenancy;
using ZWarden.Infrastructure.Tenancy;

namespace ZWarden.Infrastructure.Persistence;

/// <summary>
/// Composition seam for persistence (ADR 0005/0016). Registers a tenant-scoped
/// <see cref="ZWardenDbContext"/> over the configured provider, and offers the migrate-then-bootstrap
/// startup step. Following the F3 precedent, the host calls these once it wires persistence (F4, the
/// first persisting feature); the seam lives here so that is a one-liner, not a redesign.
/// </summary>
public static class ZWardenPersistenceServiceCollectionExtensions
{
    /// <summary>
    /// Registers a scoped <see cref="ZWardenDbContext"/> bound to <paramref name="provider"/> and
    /// <paramref name="connectionString"/>, resolving the ambient <see cref="ITenantContext"/> per scope
    /// (so its tenant filter and ownership interceptor are scoped to the request). Pair with
    /// <c>AddTenantFoundation()</c>, which supplies the tenant context.
    /// </summary>
    public static IServiceCollection AddZWardenPersistence(
        this IServiceCollection services,
        ZWardenDbProvider provider,
        string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        DbContextOptionsBuilder<ZWardenDbContext> builder = new();
        builder.UseZWardenProvider(provider, connectionString);
        DbContextOptions<ZWardenDbContext> options = builder.Options;

        services.AddScoped(sp => new ZWardenDbContext(options, sp.GetRequiredService<ITenantContext>()));
        return services;
    }

    /// <summary>
    /// Applies pending migrations and seeds the single-tenant default (ADR 0016), in one scope, at
    /// startup. Idempotent: safe to run on every boot.
    /// </summary>
    public static async Task MigrateAndBootstrapDefaultTenantAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);
        using IServiceScope scope = services.CreateScope();
        ZWardenDbContext context = scope.ServiceProvider.GetRequiredService<ZWardenDbContext>();
        await MigrationRunner.EnsureMigratedAsync(context, enabled: true, cancellationToken).ConfigureAwait(false);
        await TenantBootstrapper.EnsureDefaultTenantAsync(context, cancellationToken).ConfigureAwait(false);
    }
}
