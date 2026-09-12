using Microsoft.Extensions.Options;
using ZWarden.Agent.Configuration;
using ZWarden.Agent.ControlPlane;
using ZWarden.Agent.Health;
using ZWarden.Agent.Trust;
using ZWarden.Contracts.Protocol;
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
        TimeSpan heartbeatInterval)
        => new(
            store,
            connection,
            new StubHealthState(),
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

    private sealed class StubHealthState : IAgentHealthState
    {
        public AgentHealthStatus Current => AgentHealthStatus.Healthy;

        public string Reason => "test";

        public void Report(AgentHealthStatus status, string reason)
        {
        }
    }
}
