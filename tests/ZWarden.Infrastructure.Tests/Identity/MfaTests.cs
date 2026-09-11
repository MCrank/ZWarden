using System.Globalization;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ZWarden.Application.Tenancy;
using ZWarden.Domain.Security;
using ZWarden.Infrastructure.Identity;
using ZWarden.Infrastructure.Persistence;
using ZWarden.Infrastructure.Security;
using ZWarden.Infrastructure.Tenancy;

namespace ZWarden.Infrastructure.Tests.Identity;

/// <summary>
/// F4 S7: MFA. TOTP enrolment verifies a real authenticator code, recovery codes are single-use, and the
/// authenticator key and recovery codes are encrypted at rest (ADR 0006/0015). Offline tier, over SQLite.
/// </summary>
public class MfaTests
{
    private const string StrongPassword = "MfaUserPass12345";
    private static readonly string[] SecretTokenNames = ["AuthenticatorKey", "RecoveryCodes"];

    [Test]
    public async Task Totp_enrolment_verifies_a_valid_code_and_rejects_a_wrong_one()
    {
        await WithMfa(async (mfa, users, _, _) =>
        {
            ApplicationUser user = new("totp@zwarden.test") { Email = "totp@zwarden.test", EmailConfirmed = true };
            await users.CreateAsync(user, StrongPassword);

            string key = await mfa.GetOrCreateAuthenticatorKeyAsync(user);

            await Assert.That(await mfa.EnableAuthenticatorAsync(user, "000000")).IsFalse();

            string valid = ComputeTotp(key);
            await Assert.That(await mfa.EnableAuthenticatorAsync(user, valid)).IsTrue();
            await Assert.That(await users.GetTwoFactorEnabledAsync(user)).IsTrue();
        });
    }

    [Test]
    public async Task Recovery_codes_are_single_use()
    {
        await WithMfa(async (mfa, users, _, _) =>
        {
            ApplicationUser user = new("codes@zwarden.test") { Email = "codes@zwarden.test", EmailConfirmed = true };
            await users.CreateAsync(user, StrongPassword);

            List<string> codes = [.. (await mfa.GenerateRecoveryCodesAsync(user))!];
            await Assert.That(codes.Count).IsEqualTo(MfaService.RecoveryCodeCount);
            await Assert.That(await mfa.CountRecoveryCodesAsync(user)).IsEqualTo(MfaService.RecoveryCodeCount);

            IdentityResult redeemed = await mfa.RedeemRecoveryCodeAsync(user, codes[0]);
            await Assert.That(redeemed.Succeeded).IsTrue();
            await Assert.That(await mfa.CountRecoveryCodesAsync(user)).IsEqualTo(MfaService.RecoveryCodeCount - 1);

            // The same code cannot be redeemed twice.
            IdentityResult replay = await mfa.RedeemRecoveryCodeAsync(user, codes[0]);
            await Assert.That(replay.Succeeded).IsFalse();
        });
    }

    [Test]
    public async Task Mfa_secrets_are_encrypted_at_rest()
    {
        await WithMfa(async (mfa, users, protector, connectionString) =>
        {
            ApplicationUser user = new("atrest@zwarden.test") { Email = "atrest@zwarden.test", EmailConfirmed = true };
            await users.CreateAsync(user, StrongPassword);
            await mfa.GetOrCreateAuthenticatorKeyAsync(user);
            await mfa.GenerateRecoveryCodesAsync(user);

            // A plain context (no protector) reads the raw stored bytes - the on-disk value.
            DbContextOptions plainOptions = new DbContextOptionsBuilder<ZWardenDbContext>()
                .UseZWardenProvider(ZWardenDbProvider.Sqlite, connectionString)
                .Options;
            await using ZWardenDbContext plain = new(plainOptions, new SingleTenantContext());

            foreach (string tokenName in SecretTokenNames)
            {
                IdentityUserToken<Guid> raw = await plain.Set<IdentityUserToken<Guid>>()
                    .AsNoTracking().SingleAsync(t => t.Name == tokenName);
                string atRest = raw.Value!;

                // What is stored is an envelope, not the secret; it round-trips back to the plaintext.
                string revealed = protector.UnprotectString(atRest);
                await Assert.That(atRest).IsNotEqualTo(revealed);
                await Assert.That(atRest).DoesNotContain(revealed);
            }
        });
    }

    // --- RFC 6238 TOTP, matching Identity's authenticator (HMAC-SHA1, 30s step, 6 digits, no modifier). ---

    private static string ComputeTotp(string base32Key)
    {
        byte[] key = Base32Decode(base32Key);
        long timestep = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30;

        byte[] counter = BitConverter.GetBytes(timestep);
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(counter);
        }

        // RFC 6238 / TOTP authenticators mandate HMAC-SHA1 - this is interop with Identity's provider,
        // not a security choice, so CA5350 is intentionally suppressed here.
#pragma warning disable CA5350
        byte[] hash = HMACSHA1.HashData(key, counter);
#pragma warning restore CA5350
        int offset = hash[^1] & 0x0f;
        int binary = ((hash[offset] & 0x7f) << 24)
            | ((hash[offset + 1] & 0xff) << 16)
            | ((hash[offset + 2] & 0xff) << 8)
            | (hash[offset + 3] & 0xff);
        return (binary % 1_000_000).ToString("D6", CultureInfo.InvariantCulture);
    }

    private static byte[] Base32Decode(string input)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        input = input.TrimEnd('=').ToUpperInvariant();
        List<byte> output = [];
        int bits = 0, value = 0;
        foreach (char c in input)
        {
            value = (value << 5) | alphabet.IndexOf(c);
            bits += 5;
            if (bits >= 8)
            {
                output.Add((byte)((value >> (bits - 8)) & 0xff));
                bits -= 8;
            }
        }

        return [.. output];
    }

    private static async Task WithMfa(Func<MfaService, UserManager<ApplicationUser>, ISecretProtector, string, Task> body)
    {
        string file = Path.Combine(Path.GetTempPath(), $"zw-{Guid.NewGuid():N}.db");
        string connectionString = $"Data Source={file};Pooling=False";
        KeyRing ring = KeyRingLoader.Load($"k1:{Convert.ToBase64String(new byte[KeyRing.KeySizeBytes])}", "k1");

        ServiceCollection services = new();
        services.AddLogging();
        services.AddDataProtection();
        services.AddHttpContextAccessor();
        services.AddSecurityFoundation(ring);
        services.AddTenantFoundation();
        services.AddZWardenPersistence(ZWardenDbProvider.Sqlite, connectionString);
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
                scope.ServiceProvider.GetRequiredService<MfaService>(),
                scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>(),
                scope.ServiceProvider.GetRequiredService<ISecretProtector>(),
                connectionString);
        }
        finally
        {
            try { File.Delete(file); } catch (IOException) { /* best effort */ }
        }
    }
}
