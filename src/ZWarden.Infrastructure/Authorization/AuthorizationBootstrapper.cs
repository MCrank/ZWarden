using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using ZWarden.Domain.Ids;
using ZWarden.Infrastructure.Identity;
using ZWarden.Infrastructure.Persistence;
using ZWarden.Infrastructure.Tenancy;

namespace ZWarden.Infrastructure.Authorization;

/// <summary>
/// Startup step that seeds the current tenant's built-in roles and, when a first administrator is
/// configured, grants that account the Tenant Owner role (F5; ADR 0018). The authorization counterpart to
/// F4's <see cref="AdminBootstrapper"/>: run it after migrate-then-bootstrap and the admin seed, on every
/// boot — it is idempotent.
/// </summary>
public static class AuthorizationBootstrapper
{
    public static async Task EnsureSeededAsync(
        IServiceProvider services,
        string? adminEmail = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);
        await using AsyncServiceScope scope = services.CreateSystemScope();
        ZWardenDbContext context = scope.ServiceProvider.GetRequiredService<ZWardenDbContext>();

        UserId? adminId = null;
        if (!string.IsNullOrWhiteSpace(adminEmail))
        {
            UserManager<ApplicationUser> users =
                scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            ApplicationUser? admin = await users.FindByEmailAsync(adminEmail).ConfigureAwait(false);
            if (admin is not null)
            {
                adminId = admin.UserId;
            }
        }

        await BuiltInRoleSeeder.EnsureSeededAsync(context, adminId, cancellationToken).ConfigureAwait(false);
    }
}
