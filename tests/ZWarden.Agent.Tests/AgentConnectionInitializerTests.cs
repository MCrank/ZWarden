using Microsoft.Extensions.Options;
using ZWarden.Agent.Configuration;
using ZWarden.Agent.ControlPlane;
using ZWarden.Agent.Health;
using ZWarden.Agent.Trust;
using ZWarden.Contracts.Protocol;
using ZWarden.Contracts.Protocol.Messages;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Security;

namespace ZWarden.Agent.Tests;

/// <summary>
/// F10 S5: the connection startup step opens the control-plane connection and heartbeats when the Agent is
/// enrolled, and does nothing (the host still runs) when it is un-enrolled. The SignalR wire itself is proven
/// end-to-end by the Web hub integration test; here the orchestration is tested against a fake connection.
/// </summary>
public class AgentConnectionInitializerTests
{
    private static AgentConnectionInitializer Initializer(
        IAgentTrustStore store,
        IAgentControlPlaneConnection connection,
        TimeSpan heartbeatInterval,
        AgentEnrollmentSignal? signal = null)
        => new(
            store,
            connection,
            new StubHealthState(),
            signal ?? new AgentEnrollmentSignal(),
            Options.Create(new AgentOptions { HeartbeatInterval = heartbeatInterval }),
            TimeProvider.System,
            new RecordingLogger<AgentConnectionInitializer>());

    [Test]
    public async Task Connects_and_heartbeats_when_enrolled()
    {
        FakeControlPlaneConnection connection = new();
        StubTrustStore store = new(new AgentTrustMaterial(AgentId.New(), new SecretString("zwa_credential"), null));
        AgentConnectionInitializer initializer = Initializer(store, connection, TimeSpan.FromMilliseconds(20));

        await initializer.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => connection.Started && connection.Heartbeats >= 1);
        await initializer.StopAsync(CancellationToken.None);

        await Assert.That(connection.Started).IsTrue();
        await Assert.That(connection.Heartbeats).IsGreaterThanOrEqualTo(1);
        await Assert.That(connection.Stopped).IsTrue();
    }

    [Test]
    public async Task Does_not_connect_when_un_enrolled()
    {
        FakeControlPlaneConnection connection = new();
        StubTrustStore store = new(material: null);
        AgentConnectionInitializer initializer = Initializer(store, connection, TimeSpan.FromMilliseconds(20));

        await initializer.StartAsync(CancellationToken.None);
        await Task.Delay(100);
        await initializer.StopAsync(CancellationToken.None);

        await Assert.That(connection.Started).IsFalse();
        await Assert.That(connection.Heartbeats).IsEqualTo(0);
    }

    [Test]
    public async Task Connects_after_a_late_enrollment_without_a_restart()
    {
        // Starts un-enrolled (the background retry is still working): no trust at startup. When enrollment
        // later succeeds — trust appears and the signal settles — the connection must open without a restart (#195).
        FakeControlPlaneConnection connection = new();
        MutableTrustStore store = new(material: null);
        AgentEnrollmentSignal signal = new();
        AgentConnectionInitializer initializer = Initializer(store, connection, TimeSpan.FromMilliseconds(20), signal);

        await initializer.StartAsync(CancellationToken.None);
        await Task.Delay(50); // it should be waiting, not connected, while un-enrolled
        await Assert.That(connection.Started).IsFalse();

        // The background enrollment lands: trust is written, then the step is signalled.
        store.Set(new AgentTrustMaterial(AgentId.New(), new SecretString("zwa_credential"), null));
        signal.MarkSettled();

        await WaitUntilAsync(() => connection.Started && connection.Heartbeats >= 1);
        await initializer.StopAsync(CancellationToken.None);

        await Assert.That(connection.Started).IsTrue();
        await Assert.That(connection.Heartbeats).IsGreaterThanOrEqualTo(1);
        await Assert.That(connection.Stopped).IsTrue();
    }

    [Test]
    public async Task Does_not_connect_when_enrollment_settles_without_trust()
    {
        // Enrollment reached a terminal outcome that produced no trust (no secret configured, or the secret
        // was refused). The step must stop waiting and not connect — the host stays up.
        FakeControlPlaneConnection connection = new();
        MutableTrustStore store = new(material: null);
        AgentEnrollmentSignal signal = new();
        AgentConnectionInitializer initializer = Initializer(store, connection, TimeSpan.FromMilliseconds(20), signal);

        await initializer.StartAsync(CancellationToken.None);
        signal.MarkSettled(); // settled, but trust is still null
        await Task.Delay(100);
        await initializer.StopAsync(CancellationToken.None);

        await Assert.That(connection.Started).IsFalse();
        await Assert.That(connection.Heartbeats).IsEqualTo(0);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (int i = 0; i < 100 && !condition(); i++)
        {
            await Task.Delay(20);
        }
    }

    private sealed class FakeControlPlaneConnection : IAgentControlPlaneConnection
    {
        private int _heartbeats;

        public bool Started { get; private set; }

        public bool Stopped { get; private set; }

        public int Heartbeats => Volatile.Read(ref _heartbeats);

        public Task<ProtocolNegotiationResult> StartAsync(CancellationToken cancellationToken = default)
        {
            Started = true;
            return Task.FromResult(ProtocolCompatibility.Negotiate(ProtocolVersion.Current, ProtocolVersionRange.Supported));
        }

        public Task SendHeartbeatAsync(AgentHealthStatus health, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _heartbeats);
            return Task.CompletedTask;
        }

        public Task SendServerStateChangedAsync(
            ServerId serverId, ServerRunState runState, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task SendHealthChangedAsync(
            ServerId serverId,
            ServerHealth health,
            string reason,
            HealthBreakdown breakdown,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task SendMetricsReportAsync(
            IReadOnlyList<ServerMetricsSample> samples, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task SendHostCapacityAsync(HostCapacityReport report, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            Stopped = true;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class StubTrustStore(AgentTrustMaterial? material) : IAgentTrustStore
    {
        public Task<AgentTrustMaterial?> TryLoadAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(material);

        public Task SaveAsync(AgentTrustMaterial material, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    // A trust store whose material can appear later, modelling a late (background-retry) enrollment writing it.
    private sealed class MutableTrustStore(AgentTrustMaterial? material) : IAgentTrustStore
    {
        private AgentTrustMaterial? _material = material;

        public void Set(AgentTrustMaterial value) => Volatile.Write(ref _material, value);

        public Task<AgentTrustMaterial?> TryLoadAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(Volatile.Read(ref _material));

        public Task SaveAsync(AgentTrustMaterial value, CancellationToken cancellationToken = default)
        {
            Set(value);
            return Task.CompletedTask;
        }
    }

    private sealed class StubHealthState : IAgentHealthState
    {
        public AgentHealthStatus Current => AgentHealthStatus.Healthy;

        public string Reason => "test";

        public void Report(AgentHealthStatus status, string reason)
        {
        }
    }
}
