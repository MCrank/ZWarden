using Microsoft.AspNetCore.Identity;

namespace ZWarden.Infrastructure.Identity;

/// <summary>
/// The NIST SP 800-63-4 password checks that Identity's built-in policy does not cover (ADR 0006): a
/// mandatory <b>breached-password blocklist</b> test and the <b>upper length bound</b>. The length
/// <i>floor</i> (15) and the absence of composition rules are configured on
/// <see cref="IdentityOptions.Password"/>; this validator adds the two rules that need code.
/// </summary>
public sealed class BreachedPasswordValidator : IPasswordValidator<ApplicationUser>
{
    /// <summary>The maximum accepted password length (NIST: support at least 64). Beyond this a password
    /// is rejected rather than truncated, and it also bounds hashing work on the credential path.</summary>
    public const int MaximumLength = 64;

    private readonly IBreachedPasswordBlocklist _blocklist;

    public BreachedPasswordValidator(IBreachedPasswordBlocklist blocklist)
    {
        ArgumentNullException.ThrowIfNull(blocklist);
        _blocklist = blocklist;
    }

    /// <inheritdoc />
    public Task<IdentityResult> ValidateAsync(
        UserManager<ApplicationUser> manager,
        ApplicationUser user,
        string? password)
    {
        List<IdentityError> errors = [];

        if (password is not null)
        {
            if (password.Length > MaximumLength)
            {
                errors.Add(new IdentityError
                {
                    Code = "PasswordTooLong",
                    Description = $"Passwords must be at most {MaximumLength} characters long.",
                });
            }

            if (_blocklist.IsBreached(password))
            {
                errors.Add(new IdentityError
                {
                    Code = "PasswordBreached",
                    Description = "This password has appeared in a known data breach. Choose a different one.",
                });
            }
        }

        return Task.FromResult(errors.Count == 0
            ? IdentityResult.Success
            : IdentityResult.Failed([.. errors]));
    }
}
