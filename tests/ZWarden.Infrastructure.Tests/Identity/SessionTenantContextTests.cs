using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using ZWarden.Application.Tenancy;
using ZWarden.Domain.Tenancy;
using ZWarden.Infrastructure.Identity;
using ZWarden.Infrastructure.Persistence;
using ZWarden.Infrastructure.Tenancy;

namespace ZWarden.Infrastructure.Tests.Identity;

/// <summary>
/// F4 S5: the current tenant derives from the authenticated session (PRD 7A). The claims factory stamps
/// the tenant claim at sign-in; <see cref="ClaimsPrincipalTenantContext"/> reads it back and falls back
/// to the default tenant for an unauthenticated request (self-hosted); and the session context wins
/// registration over the single-tenant default. Offline tier.
/// </summary>
public class SessionTenantContextTests
{
    [Test]
    public async Task Sign_in_stamps_the_tenant_claim_from_the_users_own_tenant()
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
            using (IServiceScope create = root.CreateScope())
            {
                await create.ServiceProvider.GetRequiredService<ZWardenDbContext>().Database.EnsureCreatedAsync();
            }

            using IServiceScope scope = root.CreateScope();
            UserManager<ApplicationUser> users =
                scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            IUserClaimsPrincipalFactory<ApplicationUser> factory =
                scope.ServiceProvider.GetRequiredService<IUserClaimsPrincipalFactory<ApplicationUser>>();

            // The override is the registered factory (not Identity's default).
            await Assert.That(factory).IsTypeOf<TenantClaimsPrincipalFactory>();

            ApplicationUser user = new("claimer@zwarden.test") { Email = "claimer@zwarden.test" };
            await Assert.That((await users.CreateAsync(user, "SessionPass12345")).Succeeded).IsTrue();

            ClaimsPrincipal principal = await factory.CreateAsync(user);
            string? tenantClaim = principal.FindFirst(ClaimsPrincipalTenantContext.TenantClaimType)?.Value;

            await Assert.That(tenantClaim).IsEqualTo(user.TenantId.ToString());
            await Assert.That(tenantClaim).IsEqualTo(Tenant.DefaultId.ToString());
        }
        finally
        {
            try { File.Delete(file); } catch (IOException) { /* best effort */ }
        }
    }

    [Test]
    public async Task The_context_reads_the_claim_and_falls_back_to_the_default_tenant_when_anonymous()
    {
        // Authenticated principal carrying tenant A's claim.
        var tenantA = Domain.Ids.TenantId.New();
        ClaimsPrincipal signedIn = new(new ClaimsIdentity(
            [new Claim(ClaimsPrincipalTenantContext.TenantClaimType, tenantA.ToString())],
            authenticationType: "Test"));
        var withClaim = new ClaimsPrincipalTenantContext(
            new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = signedIn } });

        await Assert.That(withClaim.HasCurrentTenant).IsTrue();
        await Assert.That(withClaim.CurrentTenantId).IsEqualTo(tenantA);

        // Anonymous request (no claim) resolves to the default tenant in self-hosted.
        var anonymous = new ClaimsPrincipalTenantContext(
            new HttpContextAccessor { HttpContext = new DefaultHttpContext() });
        await Assert.That(anonymous.CurrentTenantId).IsEqualTo(Tenant.DefaultId);
    }

    [Test]
    public async Task The_session_context_wins_registration_over_the_single_tenant_default()
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddSessionTenantContext(); // registered first ...
        services.AddTenantFoundation();     // ... so this TryAdd is a no-op

        await using ServiceProvider root = services.BuildServiceProvider();
        using IServiceScope scope = root.CreateScope();
        ITenantContext resolved = scope.ServiceProvider.GetRequiredService<ITenantContext>();

        await Assert.That(resolved).IsTypeOf<ClaimsPrincipalTenantContext>();
    }
}
