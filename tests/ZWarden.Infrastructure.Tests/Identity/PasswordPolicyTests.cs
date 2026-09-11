using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ZWarden.Infrastructure.Identity;
using ZWarden.Infrastructure.Persistence;
using ZWarden.Infrastructure.Tenancy;

namespace ZWarden.Infrastructure.Tests.Identity;

/// <summary>
/// F4 S2: password hashing is configured (not inherited) and the policy meets NIST SP 800-63-4
/// (ADR 0006). Proven the way production applies it — through <see cref="UserManager{TUser}.CreateAsync(TUser, string)"/>
/// over a real SQLite database. Offline tier.
/// </summary>
public class PasswordPolicyTests
{
    [Test]
    public async Task The_hasher_is_configured_for_pbkdf2_sha512_at_the_chosen_iteration_count()
    {
        await WithIdentity(async (users, sp) =>
        {
            PasswordHasherOptions options = sp.GetRequiredService<IOptions<PasswordHasherOptions>>().Value;
            await Assert.That(options.CompatibilityMode).IsEqualTo(PasswordHasherCompatibilityMode.IdentityV3);
            // The configured value is the chosen, dated constant (220,000) - not the framework's
            // inherited 100,000 (ADR 0006, PRD 2.1). Reading it off the composed options proves the
            // host actually wired the choice, not merely that a constant exists.
            await Assert.That(options.IterationCount).IsEqualTo(IdentityHashingParameters.Pbkdf2IterationCount);
            await Assert.That(options.IterationCount).IsEqualTo(220_000);
        });
    }

    [Test]
    public async Task A_password_shorter_than_fifteen_is_rejected_and_fifteen_is_accepted_without_composition_rules()
    {
        await WithIdentity(async (users, sp) =>
        {
            // 14 characters - one under the NIST floor.
            IdentityResult tooShort = await users.CreateAsync(NewUser("short@example.test"), "short1234567ab");
            await Assert.That(tooShort.Succeeded).IsFalse();
            await Assert.That(tooShort.Errors.Any(e => e.Code == "PasswordTooShort")).IsTrue();

            // 15 characters, all lowercase letters + digits, no symbols/upper - no composition rule blocks it.
            IdentityResult ok = await users.CreateAsync(NewUser("ok@example.test"), "uniquepass12345");
            await Assert.That(ok.Succeeded).IsTrue();
        });
    }

    [Test]
    public async Task A_breached_password_is_rejected_even_when_long_enough()
    {
        await WithIdentity(async (users, sp) =>
        {
            // 16 chars - clears the length floor, but is on the blocklist.
            IdentityResult result = await users.CreateAsync(NewUser("breach@example.test"), "passwordpassword");
            await Assert.That(result.Succeeded).IsFalse();
            await Assert.That(result.Errors.Any(e => e.Code == "PasswordBreached")).IsTrue();
        });
    }

    [Test]
    public async Task A_sixty_four_character_password_is_accepted_and_a_longer_one_is_rejected()
    {
        await WithIdentity(async (users, sp) =>
        {
            IdentityResult atMax = await users.CreateAsync(NewUser("max@example.test"), new string('a', 64));
            await Assert.That(atMax.Succeeded).IsTrue();

            IdentityResult tooLong = await users.CreateAsync(NewUser("long@example.test"), new string('b', 65));
            await Assert.That(tooLong.Succeeded).IsFalse();
            await Assert.That(tooLong.Errors.Any(e => e.Code == "PasswordTooLong")).IsTrue();
        });
    }

    private static ApplicationUser NewUser(string email) => new(email) { Email = email };

    private static async Task WithIdentity(Func<UserManager<ApplicationUser>, IServiceProvider, Task> body)
    {
        string file = Path.Combine(Path.GetTempPath(), $"zw-{Guid.NewGuid():N}.db");
        ServiceCollection services = new();
        services.AddLogging();
        services.AddTenantFoundation();
        services.AddZWardenPersistence(ZWardenDbProvider.Sqlite, $"Data Source={file};Pooling=False");
        services.AddIdentityFoundation();

        await using ServiceProvider root = services.BuildServiceProvider();
        try
        {
            using (IServiceScope create = root.CreateScope())
            {
                ZWardenDbContext db = create.ServiceProvider.GetRequiredService<ZWardenDbContext>();
                await db.Database.EnsureCreatedAsync();
            }

            using IServiceScope scope = root.CreateScope();
            UserManager<ApplicationUser> users =
                scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            await body(users, scope.ServiceProvider);
        }
        finally
        {
            try
            {
                File.Delete(file);
            }
            catch (IOException)
            {
                // Best effort - a lingering pooled handle is harmless.
            }
        }
    }
}
