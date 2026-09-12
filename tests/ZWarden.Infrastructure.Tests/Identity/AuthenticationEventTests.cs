using System.Collections.Concurrent;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using ZWarden.Application.Authentication;
using ZWarden.Domain.Security;
using ZWarden.Infrastructure.Identity;
using ZWarden.Infrastructure.Persistence;
using ZWarden.Infrastructure.Security;
using ZWarden.Infrastructure.Tenancy;
using ZWarden.TestSupport;

namespace ZWarden.Infrastructure.Tests.Identity;

/// <summary>
/// F4 S8: the auth paths emit authentication events through the seam, and no event payload carries a
/// credential (PRD 11). F6 can bind a durable sink without touching auth code. Offline tier.
/// </summary>
public class AuthenticationEventTests
{
    private const string Password = "EventUserPass123";
    private const string WrongPassword = "wrong-password-000";

    [Test]
    public async Task Sign_in_password_reset_and_mfa_emit_secret_free_events()
    {
        await WithStack(async (sp, spy) =>
        {
            UserManager<ApplicationUser> users = sp.GetRequiredService<UserManager<ApplicationUser>>();
            SignInService signIn = sp.GetRequiredService<SignInService>();
            MfaService mfa = sp.GetRequiredService<MfaService>();
            AccountRecoveryService recovery = sp.GetRequiredService<AccountRecoveryService>();

            ApplicationUser user = new("events@zwarden.test") { Email = "events@zwarden.test", EmailConfirmed = true };
            await users.CreateAsync(user, Password);

            // Success, then failure.
            await signIn.PasswordSignInAsync(user, Password);
            await signIn.PasswordSignInAsync(user, WrongPassword);

            // Drive to lockout (5 failures configured) and sign in once more -> LockedOut.
            for (int i = 0; i < 5; i++)
            {
                await signIn.PasswordSignInAsync(user, WrongPassword);
            }

            // MFA verify.
            string key = await mfa.GetOrCreateAuthenticatorKeyAsync(user);
            await mfa.EnableAuthenticatorAsync(user, Totp.Compute(key));

            // Password reset.
            string token = await users.GeneratePasswordResetTokenAsync(user);
            await recovery.ResetPasswordAsync(user, token, "FreshResetPass99");

            await Assert.That(spy.Kinds).Contains(AuthenticationEventKind.SignInSucceeded);
            await Assert.That(spy.Kinds).Contains(AuthenticationEventKind.SignInFailed);
            await Assert.That(spy.Kinds).Contains(AuthenticationEventKind.LockedOut);
            await Assert.That(spy.Kinds).Contains(AuthenticationEventKind.MfaVerified);
            await Assert.That(spy.Kinds).Contains(AuthenticationEventKind.PasswordReset);

            // No event payload carries a credential.
            foreach (AuthenticationEvent evt in spy.Events)
            {
                await Assert.That(evt.Detail ?? string.Empty).DoesNotContain(Password);
                await Assert.That(evt.Detail ?? string.Empty).DoesNotContain(WrongPassword);
                await Assert.That(evt.Detail ?? string.Empty).DoesNotContain(token);
            }
        });
    }

    private sealed class SpySink : IAuthenticationEventSink
    {
        public ConcurrentQueue<AuthenticationEvent> Events { get; } = new();
        public IReadOnlyCollection<AuthenticationEventKind> Kinds => [.. Events.Select(e => e.Kind)];

        public Task RecordAsync(AuthenticationEvent authenticationEvent, CancellationToken cancellationToken = default)
        {
            Events.Enqueue(authenticationEvent);
            return Task.CompletedTask;
        }
    }

    private static async Task WithStack(Func<IServiceProvider, SpySink, Task> body)
    {
        string file = Path.Combine(Path.GetTempPath(), $"zw-{Guid.NewGuid():N}.db");
        SpySink spy = new();
        KeyRing ring = KeyRingLoader.Load($"k1:{Convert.ToBase64String(new byte[KeyRing.KeySizeBytes])}", "k1");

        ServiceCollection services = new();
        services.AddLogging();
        services.AddDataProtection();
        services.AddHttpContextAccessor();
        services.AddSecurityFoundation(ring);
        services.AddTenantFoundation();
        services.AddZWardenPersistence(ZWardenDbProvider.Sqlite, $"Data Source={file};Pooling=False");
        services.AddZWardenAuthentication(["localhost"]);
        services.AddSingleton<IAuthenticationEventSink>(spy);

        await using ServiceProvider root = services.BuildServiceProvider();
        try
        {
            using (IServiceScope create = root.CreateScope())
            {
                await create.ServiceProvider.GetRequiredService<ZWardenDbContext>().Database.EnsureCreatedAsync();
            }

            using IServiceScope scope = root.CreateScope();
            await body(scope.ServiceProvider, spy);
        }
        finally
        {
            try { File.Delete(file); } catch (IOException) { /* best effort */ }
        }
    }
}
