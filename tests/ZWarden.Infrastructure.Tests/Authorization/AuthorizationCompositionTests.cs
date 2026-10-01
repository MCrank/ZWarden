using System.Collections.Concurrent;
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

            await using AsyncServiceScope scope = root.CreateSystemScope();

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

    [Test]
    public async Task Concurrent_permission_checks_do_not_collide_on_a_shared_dbcontext()
    {
        // Regression for the interactive Blazor Server crash: a layout's permission-gated nav, an
        // AuthorizeView, and a page's own checks all evaluate in one render batch, concurrently. Before
        // ScopedPermissionChecker gave each check its own scope, they shared the circuit's ZWardenDbContext
        // and EF Core threw "A second operation was started on this context instance…".
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

            await using AsyncServiceScope scope = root.CreateSystemScope();
            UserManager<ApplicationUser> users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            ApplicationUser admin = (await users.FindByEmailAsync(adminEmail))!;

            // One checker instance, hit concurrently — exactly what a render batch does.
            IPermissionChecker checker = scope.ServiceProvider.GetRequiredService<IPermissionChecker>();
            Task<bool>[] checks = [.. Enumerable.Range(0, 32).Select(async _ =>
                (await checker.EvaluateAsync(admin.UserId, Permissions.RoleManage)).IsAllowed)];

            bool[] results = await Task.WhenAll(checks);

            await Assert.That(results.Length).IsEqualTo(32);
            await Assert.That(results.All(allowed => allowed)).IsTrue();
        }
        finally
        {
            try { File.Delete(file); } catch (IOException) { /* best effort */ }
        }
    }

    [Test]
    public async Task Thirty_two_concurrent_actions_in_one_circuit_each_get_their_own_dbcontext()
    {
        // #297 PR-B (ADR 0046 Q7): the general form of the #154 fix. Not just permission checks: any service an
        // interactive page calls - here UserManager (Identity's store) plus a raw context read - runs through
        // ActionScopeRunner, which gives each action its own scope and therefore its own ZWardenDbContext.
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

            // The circuit's long-lived scope: a captured tenant and no HttpContext.
            await using AsyncServiceScope circuit = root.CreateSystemScope();
            circuit.ServiceProvider.GetRequiredService<TenantAssignment>().MarkCircuit();
            ActionScopeRunner actions = circuit.ServiceProvider.GetRequiredService<ActionScopeRunner>();
            ConcurrentBag<ZWardenDbContext> contexts = [];

            Task<bool>[] work = [.. Enumerable.Range(0, 32).Select(i => i % 2 == 0
                ? actions.RunAsync<UserManager<ApplicationUser>, bool>(
                    async (users, _) => await users.FindByEmailAsync(adminEmail) is not null)
                : actions.RunAsync<ZWardenDbContext, bool>(async (db, ct) =>
                {
                    contexts.Add(db);
                    return await db.Set<Role>().AnyAsync(ct);
                }))];

            bool[] results = await Task.WhenAll(work);

            await Assert.That(results.All(found => found)).IsTrue();
            await Assert.That(contexts.Distinct().Count()).IsEqualTo(16);
        }
        finally
        {
            try { File.Delete(file); } catch (IOException) { /* best effort */ }
        }
    }
}
