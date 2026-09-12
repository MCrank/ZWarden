using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using ZWarden.Domain.Authorization;

namespace ZWarden.Infrastructure.Authorization;

/// <summary>
/// Resolves an ASP.NET Core authorization policy for any catalogue permission name on demand (ADR 0018),
/// so <c>[Authorize(Policy = "Server.Start")]</c> and <c>AuthorizeView(Policy = "Server.Start")</c> work
/// without hand-registering a policy per permission. A policy name that matches a catalogue permission
/// yields a policy requiring an authenticated user plus the matching <see cref="PermissionRequirement"/>;
/// any other name falls through to the default provider (so conventional policies still work). An unknown
/// permission-shaped name is never silently allowed — it simply is not a permission policy.
/// </summary>
public sealed class PermissionPolicyProvider : IAuthorizationPolicyProvider
{
    private readonly DefaultAuthorizationPolicyProvider _fallback;

    public PermissionPolicyProvider(IOptions<AuthorizationOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _fallback = new DefaultAuthorizationPolicyProvider(options);
    }

    /// <inheritdoc />
    public Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        ArgumentNullException.ThrowIfNull(policyName);
        if (Permissions.TryGet(policyName, out PermissionDefinition permission))
        {
            AuthorizationPolicy policy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .AddRequirements(new PermissionRequirement(permission))
                .Build();
            return Task.FromResult<AuthorizationPolicy?>(policy);
        }

        return _fallback.GetPolicyAsync(policyName);
    }

    /// <inheritdoc />
    public Task<AuthorizationPolicy> GetDefaultPolicyAsync() => _fallback.GetDefaultPolicyAsync();

    /// <inheritdoc />
    public Task<AuthorizationPolicy?> GetFallbackPolicyAsync() => _fallback.GetFallbackPolicyAsync();
}
