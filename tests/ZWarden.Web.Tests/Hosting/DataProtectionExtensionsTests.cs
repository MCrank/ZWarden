using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.AspNetCore.DataProtection.XmlEncryption;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using ZWarden.Domain.Security;
using ZWarden.Infrastructure.Security;
using ZWarden.Web.Hosting;

namespace ZWarden.Web.Tests.Hosting;

/// <summary>
/// #186: the Data Protection key ring (auth cookies + antiforgery) must survive a container recreate and never
/// be persisted in plaintext. These offline assertions prove the host composition (a) persists keys to a
/// durable directory, (b) protects them at rest with the app's AES-256-GCM key ring (ADR 0015), and (c) pins a
/// stable application discriminator so the ring stays valid across recreates. The reference deployment points
/// the directory at the persisted <c>zwarden_data</c> volume (both SQLite and Postgres modes).
/// </summary>
public sealed class DataProtectionExtensionsTests
{
    [Test]
    public async Task AddZWardenDataProtection_sets_a_stable_application_discriminator()
    {
        using TempDirectory keys = new();
        await using ServiceProvider provider = BuildProvider(keys.Path);

        DataProtectionOptions options = provider.GetRequiredService<IOptions<DataProtectionOptions>>().Value;

        await Assert.That(options.ApplicationDiscriminator).IsEqualTo(DataProtectionExtensions.ApplicationDiscriminator);
    }

    [Test]
    public async Task AddZWardenDataProtection_persists_keys_to_the_configured_durable_directory()
    {
        using TempDirectory keys = new();
        await using ServiceProvider provider = BuildProvider(keys.Path);

        KeyManagementOptions options = provider.GetRequiredService<IOptions<KeyManagementOptions>>().Value;

        await Assert.That(options.XmlRepository).IsTypeOf<FileSystemXmlRepository>();
        FileSystemXmlRepository repository = (FileSystemXmlRepository)options.XmlRepository!;
        await Assert.That(repository.Directory.FullName).IsEqualTo(new DirectoryInfo(keys.Path).FullName);
    }

    [Test]
    public async Task AddZWardenDataProtection_encrypts_the_key_ring_at_rest_with_the_secret_protector()
    {
        using TempDirectory keys = new();
        await using ServiceProvider provider = BuildProvider(keys.Path);

        KeyManagementOptions options = provider.GetRequiredService<IOptions<KeyManagementOptions>>().Value;

        await Assert.That(options.XmlEncryptor).IsTypeOf<SecretProtectorXmlEncryptor>();
    }

    [Test]
    public async Task The_secret_protector_encryptor_round_trips_a_key_element()
    {
        ISecretProtector protector = new SecretProtector(TestKeyRing());
        SecretProtectorXmlEncryptor encryptor = new(protector);
        SecretProtectorXmlDecryptor decryptor = new(protector);
        XElement original = new("key", new XElement("secret", "top-secret-key-material"));

        EncryptedXmlInfo encrypted = encryptor.Encrypt(original);
        XElement decrypted = decryptor.Decrypt(encrypted.EncryptedElement);

        await Assert.That(XNode.DeepEquals(decrypted, original)).IsTrue();
        await Assert.That(encrypted.DecryptorType).IsEqualTo(typeof(SecretProtectorXmlDecryptor));
    }

    [Test]
    public async Task The_secret_protector_encryptor_does_not_persist_plaintext()
    {
        ISecretProtector protector = new SecretProtector(TestKeyRing());
        SecretProtectorXmlEncryptor encryptor = new(protector);
        XElement original = new("key", new XElement("secret", "top-secret-key-material"));

        EncryptedXmlInfo encrypted = encryptor.Encrypt(original);

        await Assert.That(encrypted.EncryptedElement.ToString()).DoesNotContain("top-secret-key-material");
    }

    private static ServiceProvider BuildProvider(string keyRingPath)
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [DataProtectionExtensions.KeyRingPathConfigurationKey] = keyRingPath,
            })
            .Build();

        IServiceCollection services = new ServiceCollection();
        services.AddSingleton<ISecretProtector>(new SecretProtector(TestKeyRing()));
        services.AddZWardenDataProtection(configuration, new StubHostEnvironment());
        return services.BuildServiceProvider();
    }

    private static KeyRing TestKeyRing() =>
        new(new Dictionary<string, byte[]> { ["k1"] = new byte[32] }, "k1");

    private sealed class StubHostEnvironment : IHostEnvironment
    {
        public string ApplicationName { get; set; } = "ZWarden.Web.Tests";
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"zw-dp-test-{Guid.NewGuid():N}");
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                {
                    Directory.Delete(Path, recursive: true);
                }
            }
            catch (IOException)
            {
                // Best effort - the OS reclaims the temp directory eventually.
            }
        }
    }
}
