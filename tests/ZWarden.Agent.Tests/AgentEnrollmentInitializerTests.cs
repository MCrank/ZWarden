using System.Net.Sockets;
using System.Security.Authentication;
using Microsoft.Extensions.Options;
using ZWarden.Agent.Configuration;
using ZWarden.Agent.Trust;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Security;

namespace ZWarden.Agent.Tests;

/// <summary>
/// F9 S9 (PR 2) test plan item 13: the enrollment startup step enrols and stores when configured and not yet
/// enrolled, skips when already enrolled, starts un-enrolled when no secret is configured, and writes no
/// trust file when the exchange fails (ADR 0007). Plus #185: a transient/transport failure (TLS trust,
/// socket/timeout, unreachable control plane) never crashes host startup — it logs one actionable message and
/// retries in the background until enrolment succeeds, while a genuine refusal stops (no crash-loop).
/// </summary>
public class AgentEnrollmentInitializerTests
{
    private static AgentTrustMaterial SomeMaterial() =>
        new(AgentId.New(), new SecretString("zwa_credential"), "host-alpha");

    private static AgentEnrollmentInitializer Initializer(
        IAgentTrustStore store,
        IEnrollmentClient client,
        string trustPath,
        string? secret,
        AgentEnrollmentSignal? signal = null)
        => new(
            store,
            client,
            signal ?? new AgentEnrollmentSignal(),
            Options.Create(new AgentOptions
            {
                TrustFilePath = trustPath,
                EnrollmentSecret = secret,
                // Tiny back-off so the background retry loop turns over quickly under test.
                EnrollmentRetryInitialDelay = TimeSpan.FromMilliseconds(1),
                EnrollmentRetryMaxDelay = TimeSpan.FromMilliseconds(5),
            }),
            TimeProvider.System,
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

    [Test]
    public async Task A_tls_trust_failure_does_not_crash_startup_and_logs_the_ca_trust_docs()
    {
        using var temp = new TempDirectory();
        string path = temp.File("agent-trust.json");
        // Always fails the TLS handshake, exactly the Private-mode "Agent doesn't trust Caddy's CA" case (#185).
        ScriptedEnrollmentClient client = new(_ => throw TlsTrustFailure());
        RecordingLogger<AgentEnrollmentInitializer> logger = new();
        AgentEnrollmentInitializer initializer = new(
            new FileAgentTrustStore(path),
            client,
            new AgentEnrollmentSignal(),
            Options.Create(new AgentOptions
            {
                TrustFilePath = path,
                EnrollmentSecret = "zwe_secret",
                EnrollmentRetryInitialDelay = TimeSpan.FromMilliseconds(1),
                EnrollmentRetryMaxDelay = TimeSpan.FromMilliseconds(5),
            }),
            TimeProvider.System,
            logger);

        // StartAsync must return normally — the host stays up rather than throwing and crash-looping.
        await initializer.StartAsync(CancellationToken.None);
        await initializer.StopAsync(CancellationToken.None);

        await Assert.That(File.Exists(path)).IsFalse();
        await Assert.That(logger.Entries.Any(e => e.Message.Contains("compose-reference.md", StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    public async Task Retries_in_the_background_and_enrols_after_a_tls_trust_failure_is_fixed()
    {
        using var temp = new TempDirectory();
        string path = temp.File("agent-trust.json");
        FileAgentTrustStore store = new(path);
        AgentTrustMaterial material = SomeMaterial();
        // First attempt fails the handshake (untrusted CA); the operator fixes trust, so the retry succeeds.
        ScriptedEnrollmentClient client = new(_ => material, () => throw TlsTrustFailure());
        AgentEnrollmentInitializer initializer = Initializer(store, client, path, "zwe_secret");

        await initializer.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => File.Exists(path));
        await initializer.StopAsync(CancellationToken.None);

        AgentTrustMaterial? stored = await store.TryLoadAsync();
        await Assert.That(stored!.AgentId).IsEqualTo(material.AgentId);
        await Assert.That(client.Calls).IsGreaterThanOrEqualTo(2);
    }

    [Test]
    public async Task An_unreachable_control_plane_does_not_crash_startup_and_retries_to_enrolment()
    {
        using var temp = new TempDirectory();
        string path = temp.File("agent-trust.json");
        FileAgentTrustStore store = new(path);
        AgentTrustMaterial material = SomeMaterial();
        // First attempt cannot reach the control plane (still starting); the retry lands once it is up.
        ScriptedEnrollmentClient client = new(_ => material, () => throw Unreachable());
        AgentEnrollmentInitializer initializer = Initializer(store, client, path, "zwe_secret");

        await initializer.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => File.Exists(path));
        await initializer.StopAsync(CancellationToken.None);

        await Assert.That((await store.TryLoadAsync())!.AgentId).IsEqualTo(material.AgentId);
    }

    [Test]
    public async Task A_refusal_during_retry_stops_the_loop_without_crashing()
    {
        using var temp = new TempDirectory();
        string path = temp.File("agent-trust.json");
        RecordingLogger<AgentEnrollmentInitializer> logger = new();
        // Transient first (schedules a retry), then the control plane refuses — the one-time secret is spent.
        ScriptedEnrollmentClient client = new(_ => null, () => throw TlsTrustFailure());
        AgentEnrollmentInitializer initializer = new(
            new FileAgentTrustStore(path),
            client,
            new AgentEnrollmentSignal(),
            Options.Create(new AgentOptions
            {
                TrustFilePath = path,
                EnrollmentSecret = "zwe_secret",
                EnrollmentRetryInitialDelay = TimeSpan.FromMilliseconds(1),
                EnrollmentRetryMaxDelay = TimeSpan.FromMilliseconds(5),
            }),
            TimeProvider.System,
            logger);

        await initializer.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => client.Calls >= 2);
        int callsAfterRefusal = client.Calls;
        await Task.Delay(100);
        await initializer.StopAsync(CancellationToken.None);

        await Assert.That(File.Exists(path)).IsFalse();
        await Assert.That(client.Calls).IsEqualTo(callsAfterRefusal); // the loop stopped on refusal, not busy-looping
        await Assert.That(logger.Entries.Any(e => e.Message.Contains("refused", StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    public async Task Signals_settled_when_already_enrolled()
    {
        using var temp = new TempDirectory();
        string path = temp.File("agent-trust.json");
        FileAgentTrustStore store = new(path);
        await store.SaveAsync(SomeMaterial());
        AgentEnrollmentSignal signal = new();

        await Initializer(store, new StubEnrollmentClient(SomeMaterial()), path, "zwe_secret", signal)
            .StartAsync(CancellationToken.None);

        await Assert.That(signal.IsSettled).IsTrue();
    }

    [Test]
    public async Task Signals_settled_when_no_secret_is_configured()
    {
        using var temp = new TempDirectory();
        string path = temp.File("agent-trust.json");
        AgentEnrollmentSignal signal = new();

        await Initializer(new FileAgentTrustStore(path), new StubEnrollmentClient(SomeMaterial()), path, secret: null, signal)
            .StartAsync(CancellationToken.None);

        await Assert.That(signal.IsSettled).IsTrue();
    }

    [Test]
    public async Task Signals_settled_when_the_secret_is_refused()
    {
        using var temp = new TempDirectory();
        string path = temp.File("agent-trust.json");
        AgentEnrollmentSignal signal = new();

        await Initializer(new FileAgentTrustStore(path), new StubEnrollmentClient(null), path, "zwe_secret", signal)
            .StartAsync(CancellationToken.None);

        await Assert.That(signal.IsSettled).IsTrue();
    }

    [Test]
    public async Task Signals_settled_after_a_background_retry_enrols()
    {
        // The #195 driver: the inline attempt fails transiently (so nothing is signalled yet), then the
        // background retry succeeds — which must settle the signal so the connection step connects.
        using var temp = new TempDirectory();
        string path = temp.File("agent-trust.json");
        FileAgentTrustStore store = new(path);
        AgentEnrollmentSignal signal = new();
        AgentTrustMaterial material = SomeMaterial();
        ScriptedEnrollmentClient client = new(_ => material, () => throw Unreachable());
        AgentEnrollmentInitializer initializer = Initializer(store, client, path, "zwe_secret", signal);

        // The signal must NOT be set while only the transient inline attempt has run.
        await initializer.StartAsync(CancellationToken.None);
        await Assert.That(signal.IsSettled).IsFalse();

        await WaitUntilAsync(() => signal.IsSettled);
        await initializer.StopAsync(CancellationToken.None);

        await Assert.That(signal.IsSettled).IsTrue();
        await Assert.That((await store.TryLoadAsync())!.AgentId).IsEqualTo(material.AgentId);
    }

    private static HttpRequestException TlsTrustFailure() =>
        new(
            HttpRequestError.SecureConnectionError,
            "The SSL connection could not be established.",
            new AuthenticationException("The remote certificate is invalid according to the validation procedure."));

    private static HttpRequestException Unreachable() =>
        new(HttpRequestError.ConnectionError, "Connection refused.", new SocketException());

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (int i = 0; i < 200 && !condition(); i++)
        {
            await Task.Delay(20);
        }
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

    // Runs the queued behaviours in order (throw to simulate a transport failure), then falls back to a default.
    private sealed class ScriptedEnrollmentClient : IEnrollmentClient
    {
        private readonly Func<int, AgentTrustMaterial?> _fallback;
        private readonly Queue<Func<AgentTrustMaterial?>> _behaviours;
        private int _calls;

        public ScriptedEnrollmentClient(
            Func<int, AgentTrustMaterial?> fallback, params Func<AgentTrustMaterial?>[] behaviours)
        {
            _fallback = fallback;
            _behaviours = new Queue<Func<AgentTrustMaterial?>>(behaviours);
        }

        public int Calls => Volatile.Read(ref _calls);

        public Task<AgentTrustMaterial?> EnrollAsync(SecretString enrollmentSecret, CancellationToken cancellationToken = default)
        {
            int call = Interlocked.Increment(ref _calls);
            try
            {
                AgentTrustMaterial? result = _behaviours.Count > 0 ? _behaviours.Dequeue()() : _fallback(call);
                return Task.FromResult(result);
            }
            catch (Exception ex)
            {
                return Task.FromException<AgentTrustMaterial?>(ex);
            }
        }
    }
}
