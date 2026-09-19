using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using ZWarden.Domain.Security;

namespace ZWarden.Web.Hosting;

/// <summary>
/// #186: configures ASP.NET Core Data Protection so the cookie/antiforgery key ring is durable and
/// protected at rest. Out of the box the keys land in <c>~/.aspnet/DataProtection-Keys</c> inside the
/// container (lost on every recreate, invalidating all auth cookies and antiforgery tokens) and are
/// written in plaintext. This:
/// <list type="bullet">
///   <item>persists keys to a durable directory (the persisted <c>zwarden_data</c> volume in the reference
///     Compose deployment, both SQLite and Postgres modes), so they survive container recreate/upgrade;</item>
///   <item>encrypts the key ring at rest with the app's existing AES-256-GCM key ring (ADR 0015), so the
///     persisted keys are never plaintext;</item>
///   <item>sets a stable application discriminator so the key ring is portable across recreates.</item>
/// </list>
/// </summary>
public static class DataProtectionExtensions
{
    /// <summary>The stable application discriminator; keeps the persisted key ring valid across recreates.</summary>
    public const string ApplicationDiscriminator = "ZWarden";

    /// <summary>Configuration key for the durable key-ring directory (set to <c>/data/dp-keys</c> in Compose).</summary>
    public const string KeyRingPathConfigurationKey = "ZWarden:DataProtection:KeyRingPath";

    /// <summary>Directory name used under the content root when no explicit key-ring path is configured.</summary>
    public const string DefaultKeyRingDirectoryName = "dp-keys";

    /// <summary>
    /// Persists the Data Protection key ring to a durable directory and protects it at rest with the app's
    /// secret protector (ADR 0015). Requires <c>AddSecurityFoundation</c> to have registered
    /// <see cref="ISecretProtector"/> first. The directory is taken from
    /// <see cref="KeyRingPathConfigurationKey"/> when set, otherwise <c>&lt;contentRoot&gt;/dp-keys</c>.
    /// </summary>
    public static IServiceCollection AddZWardenDataProtection(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        string keyRingPath = configuration[KeyRingPathConfigurationKey] is { Length: > 0 } configured
            ? configured
            : Path.Combine(environment.ContentRootPath, DefaultKeyRingDirectoryName);

        Directory.CreateDirectory(keyRingPath);

        services.AddDataProtection()
            .SetApplicationName(ApplicationDiscriminator)
            .PersistKeysToFileSystem(new DirectoryInfo(keyRingPath));

        // Protect keys at rest with the AES-256-GCM key ring (ADR 0015). There is no built-in
        // ProtectKeysWith(IXmlEncryptor) overload, so the encryptor is set on KeyManagementOptions after
        // AddDataProtection's own configuration, resolving the ISecretProtector singleton from the container.
        services.AddSingleton<IConfigureOptions<KeyManagementOptions>>(sp =>
            new ConfigureOptions<KeyManagementOptions>(options =>
                options.XmlEncryptor = new SecretProtectorXmlEncryptor(sp.GetRequiredService<ISecretProtector>())));

        return services;
    }
}
