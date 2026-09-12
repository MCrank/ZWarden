using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ZWarden.Infrastructure.Identity;
using ZWarden.Infrastructure.Persistence;
using ZWarden.Infrastructure.Security;
using ZWarden.Infrastructure.Tenancy;

namespace ZWarden.Infrastructure.Tests.Identity;

/// <summary>
/// F4 S11: a smoke test of the host composition Program.cs wires. The full stack resolves the Identity
/// managers and a tenant-scoped, secret-protecting context, and completes migrate-then-bootstrap with an
/// idempotent first admin under the default tenant. Offline tier (SQLite; env key ring stubbed).
/// </summary>
public class HostCompositionTests
{
    [Test]
    public async Task The_composed_host_resolves_identity_migrates_and_seeds_the_admin_idempotently()
    {
        string file = Path.Combine(Path.GetTempPath(), $"zw-{Guid.NewGuid():N}.db");
        KeyRing ring = KeyRingLoader.Load($"k1:{Convert.ToBase64String(new byte[KeyRing.KeySizeBytes])}", "k1");

        ServiceCollection services = new();
        services.AddLogging();
        services.AddDataProtection();
        services.AddHttpContextAccessor();
        // The same order Program.cs uses (test key ring instead of the environment loader).
        services.AddSecurityFoundation(ring);
        services.AddSessionTenantContext();
        services.AddTenantFoundation();
        services.AddZWardenPersistence(ZWardenDbProvider.Sqlite, $"Data Source={file};Pooling=False");
        services.AddZWardenAuthentication(["localhost"]);

        await using ServiceProvider root = services.BuildServiceProvider();
        try
        {
            await root.MigrateAndBootstrapDefaultTenantAsync();

            bool seeded = await AdminBootstrapper.EnsureAdminAsync(root, "admin@zwarden.test", "HostAdminPass123");
            bool again = await AdminBootstrapper.EnsureAdminAsync(root, "admin@zwarden.test", "HostAdminPass123");
            await Assert.That(seeded).IsTrue();
            await Assert.That(again).IsFalse();

            using IServiceScope scope = root.CreateScope();
            // The Identity managers and a tenant-scoped context all resolve from one scope.
            await Assert.That(scope.ServiceProvider.GetService<UserManager<ApplicationUser>>()).IsNotNull();
            await Assert.That(scope.ServiceProvider.GetService<SignInManager<ApplicationUser>>()).IsNotNull();
            await Assert.That(scope.ServiceProvider.GetService<SignInService>()).IsNotNull();

            ZWardenDbContext db = scope.ServiceProvider.GetRequiredService<ZWardenDbContext>();
            await Assert.That(await db.Users.CountAsync()).IsEqualTo(1);
        }
        finally
        {
            try { File.Delete(file); } catch (IOException) { /* best effort */ }
        }
    }
}
