using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ZWarden.Domain.Authorization;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Tenancy;
using ZWarden.Infrastructure.Authorization;
using ZWarden.Infrastructure.Identity;
using ZWarden.Infrastructure.Persistence;
using ZWarden.Infrastructure.Security;
using ZWarden.Infrastructure.Tenancy;

namespace ZWarden.Infrastructure.Tests.Identity;

/// <summary>
/// #297 PR-C (ADR 0046 Q3, decision D4): a session stays valid only while the user exists, the security stamp
/// matches and the user holds a role; revoking a role bumps the stamp; and the session cookie re-checks the stamp
/// every minute. Offline tier, over the composed host and a real SQLite database.
/// </summary>
public class SessionRevalidatorTests
{
    private const string AdminEmail = "admin@zwarden.test";

    [Test]
    public async Task A_fresh_session_is_valid()
    {
        await WithHostAsync(async root =>
        {
            ClaimsPrincipal session = await SignInAsync(root);

            await Assert.That(await root.GetRequiredService<SessionRevalidator>().IsStillValidAsync(session)).IsTrue();
        });
    }

    [Test]
    public async Task A_bumped_security_stamp_invalidates_the_session()
    {
        await WithHostAsync(async root =>
        {
            ClaimsPrincipal session = await SignInAsync(root);
            await WithUsersAsync(root, async users =>
                await users.UpdateSecurityStampAsync((await users.FindByEmailAsync(AdminEmail))!));

            await Assert.That(await root.GetRequiredService<SessionRevalidator>().IsStillValidAsync(session)).IsFalse();
        });
    }

    [Test]
    public async Task A_deleted_user_invalidates_the_session()
    {
        await WithHostAsync(async root =>
        {
            ClaimsPrincipal session = await SignInAsync(root);
            await WithUsersAsync(root, async users => await users.DeleteAsync((await users.FindByEmailAsync(AdminEmail))!));

            await Assert.That(await root.GetRequiredService<SessionRevalidator>().IsStillValidAsync(session)).IsFalse();
        });
    }

    [Test]
    public async Task A_user_with_no_role_left_is_no_longer_a_valid_session()
    {
        await WithHostAsync(async root =>
        {
            ClaimsPrincipal session = await SignInAsync(root);
            await using (AsyncServiceScope scope = root.CreateSystemScope())
            {
                ZWardenDbContext db = scope.ServiceProvider.GetRequiredService<ZWardenDbContext>();
                db.RemoveRange(await db.Set<RoleAssignment>().ToListAsync());
                await db.SaveChangesAsync();
            }

            await Assert.That(await root.GetRequiredService<SessionRevalidator>().IsStillValidAsync(session)).IsFalse();
        });
    }

    [Test]
    public async Task A_principal_without_a_tenant_claim_is_not_valid()
    {
        await WithHostAsync(async root =>
        {
            ClaimsPrincipal session = await SignInAsync(root);
            ClaimsIdentity stripped = new(
                session.Claims.Where(c => c.Type != ClaimsPrincipalTenantContext.TenantClaimType),
                session.Identity!.AuthenticationType);

            await Assert.That(await root.GetRequiredService<SessionRevalidator>()
                .IsStillValidAsync(new ClaimsPrincipal(stripped))).IsFalse();
        });
    }

    [Test]
    public async Task Deleting_a_role_bumps_the_stamp_of_every_user_who_held_it()
    {
        await WithHostAsync(async root =>
        {
            ClaimsPrincipal session = await SignInAsync(root);
            UserId admin = default;
            string before = string.Empty;
            await WithUsersAsync(root, async users =>
            {
                ApplicationUser user = (await users.FindByEmailAsync(AdminEmail))!;
                admin = user.UserId;
                before = await users.GetSecurityStampAsync(user);
            });

            await using (AsyncServiceScope scope = root.CreateSystemScope())
            {
                RoleAdministrationService roles = scope.ServiceProvider.GetRequiredService<RoleAdministrationService>();
                Role custom = await roles.CreateRoleAsync(admin, "Night shift", [Permissions.ServerView]);
                ZWardenDbContext db = scope.ServiceProvider.GetRequiredService<ZWardenDbContext>();
                db.Add(RoleAssignment.TenantWide(Tenant.DefaultId, admin, custom.Id));
                await db.SaveChangesAsync();

                await roles.DeleteRoleAsync(admin, custom.Id);
            }

            string after = string.Empty;
            await WithUsersAsync(root, async users =>
                after = await users.GetSecurityStampAsync((await users.FindByEmailAsync(AdminEmail))!));

            await Assert.That(after).IsNotEqualTo(before);
            // Still a Tenant Owner, but the old session must re-authenticate.
            await Assert.That(await root.GetRequiredService<SessionRevalidator>().IsStillValidAsync(session)).IsFalse();
        });
    }

    [Test]
    public async Task The_session_cookie_rechecks_the_security_stamp_every_minute()
    {
        await WithHostAsync(async root =>
        {
            CookieAuthenticationOptions cookie = root.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
                .Get(IdentityConstants.ApplicationScheme);
            SecurityStampValidatorOptions stamp = root.GetRequiredService<IOptions<SecurityStampValidatorOptions>>().Value;
            await using AsyncServiceScope scope = root.CreateSystemScope();

            await Assert.That(cookie.Events.OnValidatePrincipal).IsNotNull();
            await Assert.That(stamp.ValidationInterval).IsEqualTo(SessionRevalidator.Interval);
            await Assert.That(SessionRevalidator.Interval).IsEqualTo(TimeSpan.FromMinutes(1));
            await Assert.That(scope.ServiceProvider.GetService<ISecurityStampValidator>()).IsNotNull();
        });
    }

    private static async Task<ClaimsPrincipal> SignInAsync(IServiceProvider root)
    {
        await using AsyncServiceScope scope = root.CreateSystemScope();
        UserManager<ApplicationUser> users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        IUserClaimsPrincipalFactory<ApplicationUser> factory =
            scope.ServiceProvider.GetRequiredService<IUserClaimsPrincipalFactory<ApplicationUser>>();
        return await factory.CreateAsync((await users.FindByEmailAsync(AdminEmail))!);
    }

    private static async Task WithUsersAsync(IServiceProvider root, Func<UserManager<ApplicationUser>, Task> body)
    {
        await using AsyncServiceScope scope = root.CreateSystemScope();
        await body(scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>());
    }

    private static async Task WithHostAsync(Func<ServiceProvider, Task> body)
    {
        string file = Path.Combine(Path.GetTempPath(), $"zw-{Guid.NewGuid():N}.db");
        KeyRing ring = KeyRingLoader.Load($"k1:{Convert.ToBase64String(new byte[KeyRing.KeySizeBytes])}", "k1");

        ServiceCollection services = new();
        services.AddLogging();
        services.AddDataProtection();
        services.AddHttpContextAccessor();
        services.AddSecurityFoundation(ring);
        services.AddSessionTenantContext();
        services.AddTenantFoundation();
        services.AddZWardenPersistence(ZWardenDbProvider.Sqlite, $"Data Source={file};Pooling=False");
        services.AddZWardenAuthentication(["localhost"]);
        services.AddZWardenAuthorization();

        await using ServiceProvider root = services.BuildServiceProvider();
        try
        {
            await root.MigrateAndBootstrapDefaultTenantAsync();
            await AdminBootstrapper.EnsureAdminAsync(root, AdminEmail, "HostAdminPass123");
            await AuthorizationBootstrapper.EnsureSeededAsync(root, AdminEmail);
            await body(root);
        }
        finally
        {
            try { File.Delete(file); } catch (IOException) { /* best effort */ }
        }
    }
}
