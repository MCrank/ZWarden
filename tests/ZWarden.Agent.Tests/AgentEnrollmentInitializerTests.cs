using Microsoft.Extensions.Options;
using ZWarden.Agent.Configuration;
using ZWarden.Agent.Trust;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Security;

namespace ZWarden.Agent.Tests;

/// <summary>
/// F9 S9 (PR 2) test plan item 13: the enrollment startup step enrols and stores when configured and not yet
/// enrolled, skips when already enrolled, starts un-enrolled when no secret is configured, and writes no
/// trust file when the exchange fails (ADR 0007).
/// </summary>
public class AgentEnrollmentInitializerTests
{
    private static AgentTrustMaterial SomeMaterial() =>
        new(AgentId.New(), new SecretString("zwa_credential"), "host-alpha");

    private static AgentEnrollmentInitializer Initializer(
        IAgentTrustStore store,
        IEnrollmentClient client,
        string trustPath,
        string? secret)
        => new(
            store,
            client,
            Options.Create(new AgentOptions { TrustFilePath = trustPath, EnrollmentSecret = secret }),
            new RecordingLogger<AgentEnrollmentInitializer>());

    [Test]
    public async Task Enrolls_and_stores_when_configured_and_not_yet_enrolled()
    {
        using var temp = new TempDirectory();
        string path = temp.File("agent-trust.json");
        FileAgentTrustStore store = new(path);
        AgentTrustMaterial material = SomeMaterial();
        StubEnrollmentClient client = new(material);

        await Initializer(store, client, path, "zwe_secret").StartAsync(CancellationToken.None);

        await Assert.That(client.Calls).IsEqualTo(1);
        AgentTrustMaterial? stored = await store.TryLoadAsync();
        await Assert.That(stored!.AgentId).IsEqualTo(material.AgentId);
        await Assert.That(stored.Credential.Reveal()).IsEqualTo("zwa_credential");
    }

    [Test]
    public async Task Skips_the_exchange_when_already_enrolled()
    {
        using var temp = new TempDirectory();
        string path = temp.File("agent-trust.json");
        FileAgentTrustStore store = new(path);
        AgentTrustMaterial existing = SomeMaterial();
        await store.SaveAsync(existing);
        StubEnrollmentClient client = new(SomeMaterial());

        await Initializer(store, client, path, "zwe_secret").StartAsync(CancellationToken.None);

        await Assert.That(client.Calls).IsEqualTo(0);
        await Assert.That((await store.TryLoadAsync())!.AgentId).IsEqualTo(existing.AgentId);
    }

    [Test]
    public async Task Starts_un_enrolled_when_no_secret_is_configured()
    {
        using var temp = new TempDirectory();
        string path = temp.File("agent-trust.json");
        StubEnrollmentClient client = new(SomeMaterial());

        await Initializer(new FileAgentTrustStore(path), client, path, secret: null).StartAsync(CancellationToken.None);

        await Assert.That(client.Calls).IsEqualTo(0);
        await Assert.That(File.Exists(path)).IsFalse();
    }

    [Test]
    public async Task A_failed_exchange_writes_no_trust_file()
    {
        using var temp = new TempDirectory();
        string path = temp.File("agent-trust.json");
        StubEnrollmentClient client = new(null); // the exchange is refused

        await Initializer(new FileAgentTrustStore(path), client, path, "zwe_secret").StartAsync(CancellationToken.None);

        await Assert.That(client.Calls).IsEqualTo(1);
        await Assert.That(File.Exists(path)).IsFalse();
    }

    private sealed class StubEnrollmentClient(AgentTrustMaterial? result) : IEnrollmentClient
    {
        public int Calls { get; private set; }

        public Task<AgentTrustMaterial?> EnrollAsync(SecretString enrollmentSecret, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(result);
        }
    }
}
