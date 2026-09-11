using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ZWarden.Application.Authentication;
using ZWarden.Domain.Tenancy;
using ZWarden.Infrastructure.Identity;
using ZWarden.Infrastructure.Persistence;
using ZWarden.Infrastructure.Tenancy;

namespace ZWarden.Infrastructure.Tests.Identity;

/// <summary>
/// F4 S9: the OIDC / identity-provider seam, proven against a test double (ADR 0017). The double
/// authenticates a subject; the mapping provisions a local user under the tenant and links it; a repeat
/// sign-in resolves to the same user. The concrete Auth0/OIDC provider is F3B - nothing here references
/// one (arch rule 6 keeps Domain/Application IdP-free). Offline tier.
/// </summary>
public class ExternalIdentitySeamTests
{
    [Test]
    public async Task A_subject_maps_to_a_local_tenant_user_and_repeats_link_to_the_same_user()
    {
        await WithStack(async (external, users) =>
        {
            TestDoubleIdentityProvider provider = new("https://test-idp.example");

            ExternalIdentity? identity = await provider.AuthenticateAsync("subject-abc");
            await Assert.That(identity).IsNotNull();

            ApplicationUser first = await external.MapToLocalUserAsync(identity!);
            await Assert.That(first.TenantId).IsEqualTo(Tenant.DefaultId);
            await Assert.That(first.EmailConfirmed).IsTrue();

            // A second sign-in for the same subject links to the same local user.
            ApplicationUser second = await external.MapToLocalUserAsync((await provider.AuthenticateAsync("subject-abc"))!);
            await Assert.That(second.Id).IsEqualTo(first.Id);
            await Assert.That(await users.Users.CountAsync()).IsEqualTo(1);

            // The link is discoverable by (issuer, subject).
            ApplicationUser? byLogin = await users.FindByLoginAsync("https://test-idp.example", "subject-abc");
            await Assert.That(byLogin!.Id).IsEqualTo(first.Id);
        });
    }

    [Test]
    public async Task A_failed_external_authentication_yields_no_identity()
    {
        TestDoubleIdentityProvider provider = new("https://test-idp.example");
        await Assert.That(await provider.AuthenticateAsync(string.Empty)).IsNull();
    }

    private sealed class TestDoubleIdentityProvider : IExternalIdentityProvider
    {
        public TestDoubleIdentityProvider(string issuer) => Issuer = issuer;

        public string Issuer { get; }

        public Task<ExternalIdentity?> AuthenticateAsync(string credential, CancellationToken cancellationToken = default)
            => Task.FromResult<ExternalIdentity?>(
                string.IsNullOrEmpty(credential)
                    ? null
                    : new ExternalIdentity(Issuer, credential, $"{credential}@example.test"));
    }

    private static async Task WithStack(Func<ExternalLoginService, UserManager<ApplicationUser>, Task> body)
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
            await body(
                scope.ServiceProvider.GetRequiredService<ExternalLoginService>(),
                scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>());
        }
        finally
        {
            try { File.Delete(file); } catch (IOException) { /* best effort */ }
        }
    }
}
