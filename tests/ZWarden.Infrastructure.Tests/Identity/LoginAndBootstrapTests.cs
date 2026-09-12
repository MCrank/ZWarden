using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ZWarden.Domain.Tenancy;
using ZWarden.Infrastructure.Identity;
using ZWarden.Infrastructure.Persistence;
using ZWarden.Infrastructure.Tenancy;

namespace ZWarden.Infrastructure.Tests.Identity;

/// <summary>
/// F4 S4: the first-admin bootstrap is idempotent and lands the admin under the default tenant, and
/// sign-in locks an account out after repeated failures (PRD 11). Offline tier, over real SQLite.
/// </summary>
public class LoginAndBootstrapTests
{
    private const string StrongPassword = "AdminPass123456!";

    [Test]
    public async Task The_admin_bootstrap_is_idempotent_and_seeds_under_the_default_tenant()
    {
        await WithStack(async root =>
        {
            bool first = await AdminBootstrapper.EnsureAdminAsync(root, "admin@zwarden.test", StrongPassword);
            bool second = await AdminBootstrapper.EnsureAdminAsync(root, "admin@zwarden.test", StrongPassword);

            await Assert.That(first).IsTrue();
            await Assert.That(second).IsFalse();

            using IServiceScope scope = root.CreateScope();
            UserManager<ApplicationUser> users =
                scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            ZWardenDbContext db = scope.ServiceProvider.GetRequiredService<ZWardenDbContext>();

            await Assert.That(await db.Users.CountAsync()).IsEqualTo(1);

            ApplicationUser admin = (await users.FindByEmailAsync("admin@zwarden.test"))!;
            await Assert.That(admin.TenantId).IsEqualTo(Tenant.DefaultId);
            await Assert.That(admin.EmailConfirmed).IsTrue();
            await Assert.That(await users.IsInRoleAsync(admin, AdminBootstrapper.AdminRoleName)).IsTrue();
        });
    }

    [Test]
    public async Task An_account_locks_out_after_repeated_failures()
    {
        await WithStack(async root =>
        {
            using IServiceScope scope = root.CreateScope();
            UserManager<ApplicationUser> users =
                scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            SignInManager<ApplicationUser> signIn =
                scope.ServiceProvider.GetRequiredService<SignInManager<ApplicationUser>>();

            ApplicationUser user = new("operator@zwarden.test") { Email = "operator@zwarden.test", EmailConfirmed = true };
            await Assert.That((await users.CreateAsync(user, StrongPassword)).Succeeded).IsTrue();

            for (int attempt = 0; attempt < 5; attempt++)
            {
                await signIn.CheckPasswordSignInAsync(user, "wrong-password-123", lockoutOnFailure: true);
            }

            await Assert.That(await users.IsLockedOutAsync(user)).IsTrue();

            // Even the correct password is refused while locked.
            SignInResult afterLock = await signIn.CheckPasswordSignInAsync(user, StrongPassword, lockoutOnFailure: true);
            await Assert.That(afterLock.IsLockedOut).IsTrue();
        });
    }

    private static async Task WithStack(Func<IServiceProvider, Task> body)
    {
        string file = Path.Combine(Path.GetTempPath(), $"zw-{Guid.NewGuid():N}.db");
        ServiceCollection services = new();
        services.AddLogging();
        services.AddDataProtection();
        services.AddHttpContextAccessor();
        services.AddTenantFoundation();
        services.AddZWardenPersistence(ZWardenDbProvider.Sqlite, $"Data Source={file};Pooling=False");
        services.AddZWardenAuthentication(["localhost"]);

        await using ServiceProvider root = services.BuildServiceProvider();
        try
        {
            using (IServiceScope scope = root.CreateScope())
            {
                ZWardenDbContext db = scope.ServiceProvider.GetRequiredService<ZWardenDbContext>();
                await db.Database.EnsureCreatedAsync();
            }

            await body(root);
        }
        finally
        {
            try
            {
                File.Delete(file);
            }
            catch (IOException)
            {
                // Best effort.
            }
        }
    }
}
