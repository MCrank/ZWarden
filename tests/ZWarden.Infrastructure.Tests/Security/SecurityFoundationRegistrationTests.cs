using Microsoft.Extensions.DependencyInjection;
using ZWarden.Domain.Security;
using ZWarden.Infrastructure.Security;

namespace ZWarden.Infrastructure.Tests.Security;

/// <summary>S5: the host can resolve a working protector from the DI seam.</summary>
public class SecurityFoundationRegistrationTests
{
    [Test]
    public async Task AddSecurityFoundation_resolves_a_working_protector()
    {
        KeyRing ring = KeyRingLoader.Load($"k1:{Convert.ToBase64String(new byte[KeyRing.KeySizeBytes])}", "k1");
        await using ServiceProvider provider = new ServiceCollection()
            .AddSecurityFoundation(ring)
            .BuildServiceProvider();

        ISecretProtector protector = provider.GetRequiredService<ISecretProtector>();
        IKeyRing resolvedRing = provider.GetRequiredService<IKeyRing>();

        string envelope = protector.ProtectString("secret");
        await Assert.That(protector.UnprotectString(envelope)).IsEqualTo("secret");
        await Assert.That(resolvedRing.ActiveKeyId).IsEqualTo("k1");
    }
}
