using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ZWarden.Application.Authorization;
using ZWarden.Domain.Authorization;
using ZWarden.Domain.Ids;
using ZWarden.Infrastructure.Authorization;
using ZWarden.Infrastructure.Identity;
using ZWarden.Infrastructure.Persistence;
using ZWarden.Infrastructure.Security;
using ZWarden.Infrastructure.Tenancy;

namespace ZWarden.Infrastructure.Tests.Authorization;

/// <summary>
/// F5 S9 (PR 2): a smoke test of the authorization host composition Program.cs wires. The full stack
/// resolves the decision service, policy provider, and handlers; the bootstrap seeds built-in roles and
/// makes the first admin a Tenant Owner; and that admin is authorized end to end. Offline tier.
/// </summary>
public class AuthorizationCompositionTests
{
    [Test]
    public async Task The_composed_host_wires_authorization_seeds_roles_and_authorizes_the_admin()
    {
        string file = Path.Combine(Path.GetTempPath(), $"zw-{Guid.NewGuid():N}.db");
        KeyRing ring = KeyRingLoader.Load($"k1:{Convert.ToBase64String(new byte[KeyRing.KeySizeBytes])}", "k1");
        const string adminEmail = "admin@zwarden.test";

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
            await AdminBootstrapper.EnsureAdminAsync(root, adminEmail, "HostAdminPass123");
            await AuthorizationBootstrapper.EnsureSeededAsync(root, adminEmail);
            await AuthorizationBootstrapper.EnsureSeededAsync(root, adminEmail); // idempotent second boot

            using IServiceScope scope = root.CreateScope();

            // The enforcement surface resolves.
            await Assert.That(scope.ServiceProvider.GetService<IPermissionChecker>()).IsNotNull();
            await Assert.That(scope.ServiceProvider.GetService<RoleAdministrationService>()).IsNotNull();
            await Assert.That(scope.ServiceProvider.GetServices<IAuthorizationHandler>().Any()).IsTrue();
            await Assert.That(root.GetService<IAuthorizationPolicyProvider>()).IsOfType(typeof(PermissionPolicyProvider));

            // Built-in roles are seeded for the default tenant.
            ZWardenDbContext db = scope.ServiceProvider.GetRequiredService<ZWardenDbContext>();
            await Assert.That(await db.Set<Role>().CountAsync(r => r.BuiltIn != null))
                .IsEqualTo(BuiltInRoles.All.Count);

            // The admin, made Tenant Owner, is authorized end to end (Tenant Owner holds every permission).
            UserManager<ApplicationUser> users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            ApplicationUser admin = (await users.FindByEmailAsync(adminEmail))!;
            IPermissionChecker checker = scope.ServiceProvider.GetRequiredService<IPermissionChecker>();

            await Assert.That((await checker.EvaluateAsync(admin.UserId, Permissions.RoleManage)).IsAllowed).IsTrue();
            await Assert.That((await checker.EvaluateAsync(admin.UserId, Permissions.ServerStart, ServerId.New())).IsAllowed).IsTrue();
        }
        finally
        {
            try { File.Delete(file); } catch (IOException) { /* best effort */ }
        }
    }
}
