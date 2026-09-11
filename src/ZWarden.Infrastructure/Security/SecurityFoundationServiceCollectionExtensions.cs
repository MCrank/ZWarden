using Microsoft.Extensions.DependencyInjection;
using ZWarden.Domain.Security;

namespace ZWarden.Infrastructure.Security;

/// <summary>
/// Composition seam for the security foundation (ADR 0015). The host calls one of these once secret
/// keys are provisioned; wiring is deliberately not done in <c>Program.cs</c> yet, because the loader
/// fails closed and no feature consumes a protected value until F4.
/// </summary>
public static class SecurityFoundationServiceCollectionExtensions
{
    /// <summary>
    /// Registers an <see cref="IKeyRing"/> loaded from the environment (fail-closed on startup) and an
    /// <see cref="ISecretProtector"/> over it, both as singletons.
    /// </summary>
    public static IServiceCollection AddSecurityFoundation(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<IKeyRing>(_ => KeyRingLoader.LoadFromEnvironment());
        services.AddSingleton<ISecretProtector, SecretProtector>();
        return services;
    }

    /// <summary>
    /// Registers the security foundation over a <paramref name="keyRing"/> supplied by the caller - for
    /// tests, and for hosts that load key material their own way.
    /// </summary>
    public static IServiceCollection AddSecurityFoundation(this IServiceCollection services, IKeyRing keyRing)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(keyRing);
        services.AddSingleton(keyRing);
        services.AddSingleton<ISecretProtector, SecretProtector>();
        return services;
    }
}
