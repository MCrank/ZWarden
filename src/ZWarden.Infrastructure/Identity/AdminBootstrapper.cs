using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace ZWarden.Infrastructure.Identity;

/// <summary>
/// Seeds the first administrator so a fresh self-hosted install is reachable (F4), the identity
/// counterpart to F3A's tenant bootstrap. Idempotent: it creates the <see cref="AdminRoleName"/> role
/// and the admin user only if they are absent, so it is safe on every boot. The admin lands under the
/// ambient tenant (the default tenant in self-hosted), stamped by the ownership interceptor.
/// </summary>
public static class AdminBootstrapper
{
    /// <summary>The built-in administrator role name. F5 owns what the role can <i>do</i>; F4 only
    /// establishes it so the seeded account has a role from first boot.</summary>
    public const string AdminRoleName = "Administrator";

    /// <summary>
    /// Ensures the administrator role and a confirmed admin user exist. Returns <see langword="true"/> if
    /// it created the admin on this call, <see langword="false"/> if one already existed. Throws if
    /// creation fails (e.g. the supplied password does not meet policy) so a misconfigured first boot is
    /// loud, not silently password-less.
    /// </summary>
    public static async Task<bool> EnsureAdminAsync(
        IServiceProvider services,
        string email,
        string password,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        using IServiceScope scope = services.CreateScope();
        UserManager<ApplicationUser> users =
            scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        RoleManager<ApplicationRole> roles =
            scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();

        cancellationToken.ThrowIfCancellationRequested();

        if (!await roles.RoleExistsAsync(AdminRoleName).ConfigureAwait(false))
        {
            await roles.CreateAsync(new ApplicationRole(AdminRoleName)).ConfigureAwait(false);
        }

        if (await users.FindByEmailAsync(email).ConfigureAwait(false) is not null)
        {
            return false;
        }

        ApplicationUser admin = new(email) { Email = email, EmailConfirmed = true };
        IdentityResult created = await users.CreateAsync(admin, password).ConfigureAwait(false);
        if (!created.Succeeded)
        {
            // Codes only - never the password or the attempted value.
            throw new InvalidOperationException(
                $"Failed to seed the administrator account: {string.Join(", ", created.Errors.Select(e => e.Code))}.");
        }

        await users.AddToRoleAsync(admin, AdminRoleName).ConfigureAwait(false);
        return true;
    }
}
