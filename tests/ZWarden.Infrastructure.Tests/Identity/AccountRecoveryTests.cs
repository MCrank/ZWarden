using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using ZWarden.Infrastructure.Identity;
using ZWarden.Infrastructure.Persistence;
using ZWarden.Infrastructure.Tenancy;

namespace ZWarden.Infrastructure.Tests.Identity;

/// <summary>
/// F4 S6: account recovery. Reset and confirmation tokens flow through the notification seam and reset
/// tokens are single-use in effect (the security stamp they bind to rotates on success). PRD 11. No
/// transport is asserted - only the lifecycle. Offline tier.
/// </summary>
public class AccountRecoveryTests
{
    private const string StrongPassword = "RecoverPass12345";

    [Test]
    public async Task A_reset_token_resets_the_password_and_cannot_be_replayed()
    {
        await WithRecovery(async (recovery, users, spy) =>
        {
            ApplicationUser user = new("reset@zwarden.test") { Email = "reset@zwarden.test", EmailConfirmed = true };
            await users.CreateAsync(user, StrongPassword);

            await recovery.RequestPasswordResetAsync("reset@zwarden.test");
            await Assert.That(spy.PasswordResetToken).IsNotNull();

            string token = spy.PasswordResetToken!;
            IdentityResult first = await recovery.ResetPasswordAsync(user, token, "BrandNewPass9876");
            await Assert.That(first.Succeeded).IsTrue();

            // The same token is spent - the successful reset rotated the security stamp it was bound to.
            IdentityResult replay = await recovery.ResetPasswordAsync(user, token, "AnotherPass54321");
            await Assert.That(replay.Succeeded).IsFalse();
        });
    }

    [Test]
    public async Task An_email_confirmation_token_confirms_the_account_and_invokes_the_seam()
    {
        await WithRecovery(async (recovery, users, spy) =>
        {
            ApplicationUser user = new("confirm@zwarden.test") { Email = "confirm@zwarden.test" };
            await users.CreateAsync(user, StrongPassword);
            await Assert.That(user.EmailConfirmed).IsFalse();

            await recovery.RequestEmailConfirmationAsync(user);
            await Assert.That(spy.EmailConfirmationToken).IsNotNull();
            await Assert.That(spy.Calls).IsGreaterThanOrEqualTo(1);

            IdentityResult confirmed = await recovery.ConfirmEmailAsync(user, spy.EmailConfirmationToken!);
            await Assert.That(confirmed.Succeeded).IsTrue();

            ApplicationUser reloaded = (await users.FindByEmailAsync("confirm@zwarden.test"))!;
            await Assert.That(reloaded.EmailConfirmed).IsTrue();
        });
    }

    private sealed class SpyNotification : IAccountNotification
    {
        public string? PasswordResetToken { get; private set; }
        public string? EmailConfirmationToken { get; private set; }
        public int Calls { get; private set; }

        public Task SendPasswordResetAsync(ApplicationUser user, string resetToken, CancellationToken cancellationToken = default)
        {
            PasswordResetToken = resetToken;
            Calls++;
            return Task.CompletedTask;
        }

        public Task SendEmailConfirmationAsync(ApplicationUser user, string confirmationToken, CancellationToken cancellationToken = default)
        {
            EmailConfirmationToken = confirmationToken;
            Calls++;
            return Task.CompletedTask;
        }
    }

    private static async Task WithRecovery(
        Func<AccountRecoveryService, UserManager<ApplicationUser>, SpyNotification, Task> body)
    {
        string file = Path.Combine(Path.GetTempPath(), $"zw-{Guid.NewGuid():N}.db");
        SpyNotification spy = new();
        ServiceCollection services = new();
        services.AddLogging();
        services.AddDataProtection();
        services.AddHttpContextAccessor();
        services.AddTenantFoundation();
        services.AddZWardenPersistence(ZWardenDbProvider.Sqlite, $"Data Source={file};Pooling=False");
        services.AddZWardenAuthentication(["localhost"]);
        services.AddSingleton<IAccountNotification>(spy);

        await using ServiceProvider root = services.BuildServiceProvider();
        try
        {
            using (IServiceScope create = root.CreateScope())
            {
                await create.ServiceProvider.GetRequiredService<ZWardenDbContext>().Database.EnsureCreatedAsync();
            }

            using IServiceScope scope = root.CreateScope();
            await body(
                scope.ServiceProvider.GetRequiredService<AccountRecoveryService>(),
                scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>(),
                spy);
        }
        finally
        {
            try { File.Delete(file); } catch (IOException) { /* best effort */ }
        }
    }
}
