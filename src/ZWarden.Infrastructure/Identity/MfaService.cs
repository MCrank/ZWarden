using Microsoft.AspNetCore.Identity;

namespace ZWarden.Infrastructure.Identity;

/// <summary>
/// Multi-factor authentication (F4, PRD 11): authenticator (TOTP) enrolment and verification, and
/// two-factor recovery codes. The shared authenticator key and the recovery codes are stored in the
/// Identity token table, which <see cref="Persistence.ZWardenDbContext"/> encrypts at rest through
/// <c>ISecretProtector</c> (ADR 0015) — so this service works in plaintext in memory while the database
/// only ever holds envelopes. Passkeys are deliberately not part of this (ADR 0006): they are a primary
/// factor with no 2FA story and unverified attestation by default, deferred to v1.1.
/// </summary>
public sealed class MfaService
{
    private readonly UserManager<ApplicationUser> _users;

    public MfaService(UserManager<ApplicationUser> users)
    {
        ArgumentNullException.ThrowIfNull(users);
        _users = users;
    }

    /// <summary>The default number of recovery codes issued on enrolment.</summary>
    public const int RecoveryCodeCount = 10;

    /// <summary>Returns the user's authenticator shared key, creating one on first enrolment. The value
    /// is the unformatted base32 secret an authenticator app is seeded with.</summary>
    public async Task<string> GetOrCreateAuthenticatorKeyAsync(ApplicationUser user)
    {
        ArgumentNullException.ThrowIfNull(user);
        string? key = await _users.GetAuthenticatorKeyAsync(user).ConfigureAwait(false);
        if (string.IsNullOrEmpty(key))
        {
            await _users.ResetAuthenticatorKeyAsync(user).ConfigureAwait(false);
            key = await _users.GetAuthenticatorKeyAsync(user).ConfigureAwait(false);
        }

        return key!;
    }

    /// <summary>Verifies a TOTP code against the user's authenticator key and, on success, enables
    /// two-factor authentication. Returns whether the code was valid.</summary>
    public async Task<bool> EnableAuthenticatorAsync(ApplicationUser user, string code)
    {
        ArgumentNullException.ThrowIfNull(user);
        bool valid = await _users.VerifyTwoFactorTokenAsync(
            user, _users.Options.Tokens.AuthenticatorTokenProvider, code).ConfigureAwait(false);
        if (!valid)
        {
            return false;
        }

        await _users.SetTwoFactorEnabledAsync(user, true).ConfigureAwait(false);
        return true;
    }

    /// <summary>Generates a fresh set of single-use recovery codes (replacing any existing ones).</summary>
    public Task<IEnumerable<string>?> GenerateRecoveryCodesAsync(ApplicationUser user, int count = RecoveryCodeCount)
    {
        ArgumentNullException.ThrowIfNull(user);
        return _users.GenerateNewTwoFactorRecoveryCodesAsync(user, count);
    }

    /// <summary>Redeems a recovery code; each code works once.</summary>
    public Task<IdentityResult> RedeemRecoveryCodeAsync(ApplicationUser user, string code)
    {
        ArgumentNullException.ThrowIfNull(user);
        return _users.RedeemTwoFactorRecoveryCodeAsync(user, code);
    }

    /// <summary>The number of unredeemed recovery codes the user has left.</summary>
    public Task<int> CountRecoveryCodesAsync(ApplicationUser user)
    {
        ArgumentNullException.ThrowIfNull(user);
        return _users.CountRecoveryCodesAsync(user);
    }
}
