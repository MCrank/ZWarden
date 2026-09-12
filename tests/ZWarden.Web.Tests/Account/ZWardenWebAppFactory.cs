using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using ZWarden.Infrastructure.Identity;

namespace ZWarden.Web.Tests.Account;

/// <summary>
/// Boots the real <c>Program</c> host over an isolated SQLite file for the F4 UI cookie-flow tests
/// (issue #63) — the repo's first <see cref="WebApplicationFactory{TEntryPoint}"/>. It supplies the
/// things the composed host needs to start: a fixed test key ring (the security foundation fails closed
/// without one) and a throwaway database. The migrate-then-bootstrap step runs during host startup, so
/// the schema exists before the first request.
/// </summary>
/// <remarks>
/// Tests hit the app over <b>https</b> (<see cref="CreateWebClient"/>) so the always-Secure application
/// cookie is actually stored by the client — the point of the hardening being tested. Each factory owns
/// its own SQLite file, so tests are isolated and can run in parallel.
/// </remarks>
public sealed class ZWardenWebAppFactory : WebApplicationFactory<Program>
{
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"zw-web-{Guid.NewGuid():N}.db");

    static ZWardenWebAppFactory()
    {
        // AddSecurityFoundation loads the key ring from the environment (ADR 0015) and fails closed
        // without one. A fixed all-zero 256-bit key is fine for tests - nothing here guards real secrets.
        Environment.SetEnvironmentVariable("ZW_SECRET_KEYS", $"k1:{Convert.ToBase64String(new byte[32])}");
        Environment.SetEnvironmentVariable("ZW_SECRET_ACTIVE_KEY_ID", "k1");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.UseEnvironment("Development");
        // Pooling=False so the file handle is released for deletion at teardown.
        builder.UseSetting("ConnectionStrings:ZWarden", $"Data Source={_databasePath};Pooling=False");
    }

    /// <summary>A client that speaks https (so the Secure cookie is kept) and does not chase redirects
    /// (so a test can assert the 302 and its Set-Cookie itself).</summary>
    public HttpClient CreateWebClient() => CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false,
        HandleCookies = true,
    });

    /// <summary>Creates a confirmed user directly through the store (arranging state the HTTP tests
    /// then exercise). Confirmed because the sign-in policy requires it.</summary>
    public async Task CreateConfirmedUserAsync(string email, string password)
    {
        using IServiceScope scope = Services.CreateScope();
        UserManager<ApplicationUser> users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        ApplicationUser user = new(email) { Email = email, EmailConfirmed = true };
        IdentityResult result = await users.CreateAsync(user, password);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"Could not seed test user: {string.Join(", ", result.Errors.Select(e => e.Code))}.");
        }
    }

    /// <summary>Enables an authenticator on an existing user and returns the unformatted key, so a test
    /// can compute a valid TOTP with <c>TestSupport.Totp</c>.</summary>
    public async Task<string> EnableAuthenticatorAsync(string email)
    {
        using IServiceScope scope = Services.CreateScope();
        UserManager<ApplicationUser> users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        MfaService mfa = scope.ServiceProvider.GetRequiredService<MfaService>();
        ApplicationUser user = await users.FindByEmailAsync(email)
            ?? throw new InvalidOperationException($"No user {email}.");

        string key = await mfa.GetOrCreateAuthenticatorKeyAsync(user);
        await users.SetTwoFactorEnabledAsync(user, true);
        return key;
    }

    /// <summary>Generates a password-reset token for an existing user (the token a reset link carries).</summary>
    public async Task<string> CreatePasswordResetTokenAsync(string email)
    {
        using IServiceScope scope = Services.CreateScope();
        UserManager<ApplicationUser> users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        ApplicationUser user = await users.FindByEmailAsync(email)
            ?? throw new InvalidOperationException($"No user {email}.");
        return await users.GeneratePasswordResetTokenAsync(user);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            try
            {
                File.Delete(_databasePath);
            }
            catch (IOException)
            {
                // Best effort - the temp file is cleaned up by the OS eventually.
            }
        }
    }
}
