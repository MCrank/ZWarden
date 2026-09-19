using ZWarden.Domain.Workshop;

namespace ZWarden.Domain.Tests.Workshop;

/// <summary>
/// F110 PR-B: the <see cref="WorkshopIntegrationSettings"/> aggregate (<c>wis-</c>) holds the optional search
/// key only as an already-encrypted envelope, and exposes the non-secret <see cref="WorkshopIntegrationSettings.KeyConfigured"/>
/// capability flag derived from that envelope's presence (ADR 0044/0015).
/// </summary>
public class WorkshopIntegrationSettingsTests
{
    [Test]
    public async Task Create_starts_in_keyless_mode()
    {
        WorkshopIntegrationSettings settings = WorkshopIntegrationSettings.Create();

        await Assert.That(settings.KeyConfigured).IsFalse();
        await Assert.That(settings.ProtectedApiKey).IsEqualTo(string.Empty);
        await Assert.That(settings.TenantId.IsEmpty).IsTrue(); // stamped by the interceptor on insert
    }

    [Test]
    public async Task SetProtectedApiKey_stores_the_envelope_and_flags_configured()
    {
        WorkshopIntegrationSettings settings = WorkshopIntegrationSettings.Create();

        settings.SetProtectedApiKey("envelope-v1-abc");

        await Assert.That(settings.KeyConfigured).IsTrue();
        await Assert.That(settings.ProtectedApiKey).IsEqualTo("envelope-v1-abc");
    }

    [Test]
    public async Task ClearApiKey_returns_to_keyless_mode()
    {
        WorkshopIntegrationSettings settings = WorkshopIntegrationSettings.Create();
        settings.SetProtectedApiKey("envelope-v1-abc");

        settings.ClearApiKey();

        await Assert.That(settings.KeyConfigured).IsFalse();
        await Assert.That(settings.ProtectedApiKey).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task SetProtectedApiKey_rejects_a_blank_envelope()
    {
        WorkshopIntegrationSettings settings = WorkshopIntegrationSettings.Create();

        await Assert.That(() => settings.SetProtectedApiKey("   ")).Throws<ArgumentException>();
        await Assert.That(() => settings.SetProtectedApiKey("")).Throws<ArgumentException>();
    }
}
