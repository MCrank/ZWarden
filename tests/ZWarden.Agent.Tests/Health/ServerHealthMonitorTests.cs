using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using ZWarden.Agent.Configuration;
using ZWarden.Agent.ControlPlane;
using ZWarden.Agent.Health;
using ZWarden.Agent.Trust;
using ZWarden.Contracts.Protocol;
using ZWarden.Contracts.Protocol.Messages;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Security;

namespace ZWarden.Agent.Tests.Health;

/// <summary>
/// F16 PR-A: the periodic monitor reports only health/run-state <b>transitions</b>. A newly-seen Server is
/// reported once; an unchanged Server is silent on subsequent sweeps; an un-enrolled Agent monitors nothing.
/// </summary>
public class ServerHealthMonitorTests
{
    private static readonly ServerId Server = ServerId.New();

    private static ServerObservation Observation(ServerRunState runState, ServerHealth health) =>
        new(Server, runState, health, Breakdown(), health.ToString());

    private static HealthBreakdown Breakdown() => new(
        new ProbeCheck(ProbeStatus.Pass), new ProbeCheck(ProbeStatus.Pass),
        new ProbeCheck(ProbeStatus.Pass), new ProbeCheck(ProbeStatus.Skipped));

    private static ServerHealthMonitor Monitor(
        IServerHealthObserver observer, IAgentControlPlaneConnection connection, bool enrolled)
    {
        AgentTrustMaterial? material = enrolled
            ? new AgentTrustMaterial(AgentId.New(), new SecretString("zwa_cred"), null)
            : null;
        return new ServerHealthMonitor(
            new StubTrustStore(material),
            observer,
            connection,
            Options.Create(new AgentOptions { HealthReportInterval = TimeSpan.FromMilliseconds(20) }),
            TimeProvider.System,
            new RecordingLogger<ServerHealthMonitor>());
    }

    [Test]
    public async Task A_newly_seen_server_is_reported_once()
    {
        RecordingConnection connection = new();
        ScriptedObserver observer = new(
            [Observation(ServerRunState.Running, ServerHealth.Healthy)]);
        ServerHealthMonitor monitor = Monitor(observer, connection, enrolled: true);

        await monitor.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => !connection.HealthChanges.IsEmpty && !connection.StateChanges.IsEmpty);
        await monitor.StopAsync(CancellationToken.None);

        await Assert.That(connection.StateChanges).Contains(ServerRunState.Running);
        await Assert.That(connection.HealthChanges).Contains(ServerHealth.Healthy);
    }

    [Test]
    public async Task An_unchanged_server_is_not_reported_again()
    {
        RecordingConnection connection = new();
        // Every sweep returns the same observation; only the first should emit.
        ScriptedObserver observer = new(alwaysReturn: Observation(ServerRunState.Running, ServerHealth.Healthy));
        ServerHealthMonitor monitor = Monitor(observer, connection, enrolled: true);

        await monitor.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => observer.Calls >= 4); // several sweeps have run
        await monitor.StopAsync(CancellationToken.None);

        await Assert.That(connection.HealthChanges.Count).IsEqualTo(1);
        await Assert.That(connection.StateChanges.Count).IsEqualTo(1);
    }

    [Test]
    public async Task A_health_change_is_reported_on_a_later_sweep()
    {
        RecordingConnection connection = new();
        ScriptedObserver observer = new(
            [Observation(ServerRunState.Running, ServerHealth.Healthy)],
            [Observation(ServerRunState.Running, ServerHealth.Degraded)]);
        ServerHealthMonitor monitor = Monitor(observer, connection, enrolled: true);

        await monitor.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => connection.HealthChanges.Contains(ServerHealth.Degraded));
        await monitor.StopAsync(CancellationToken.None);

        // Health flipped Healthy -> Degraded (two health reports); run-state never changed (one report).
        await Assert.That(connection.HealthChanges).Contains(ServerHealth.Healthy);
        await Assert.That(connection.HealthChanges).Contains(ServerHealth.Degraded);
        await Assert.That(connection.StateChanges.Count).IsEqualTo(1);
    }

    [Test]
    public async Task An_unenrolled_agent_monitors_nothing()
    {
        RecordingConnection connection = new();
        ScriptedObserver observer = new(alwaysReturn: Observation(ServerRunState.Running, ServerHealth.Healthy));
        ServerHealthMonitor monitor = Monitor(observer, connection, enrolled: false);

        await monitor.StartAsync(CancellationToken.None);
        await Task.Delay(100);
        await monitor.StopAsync(CancellationToken.None);

        await Assert.That(observer.Calls).IsEqualTo(0);
        await Assert.That(connection.HealthChanges).IsEmpty();
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (int i = 0; i < 200 && !condition(); i++)
        {
            await Task.Delay(20);
        }
    }

    private sealed class ScriptedObserver : IServerHealthObserver
    {
        private readonly ConcurrentQueue<IReadOnlyList<ServerObservation>> _sweeps = new();
        private readonly IReadOnlyList<ServerObservation>? _always;
        private int _calls;

        public ScriptedObserver(params IReadOnlyList<ServerObservation>[] sweeps)
        {
            foreach (IReadOnlyList<ServerObservation> sweep in sweeps)
            {
                _sweeps.Enqueue(sweep);
            }
        }

        public ScriptedObserver(ServerObservation alwaysReturn) => _always = [alwaysReturn];

        public int Calls => Volatile.Read(ref _calls);

        public Task<IReadOnlyList<ServerObservation>> ObserveAllAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            if (_always is not null)
            {
                return Task.FromResult(_always);
            }

            // Replay scripted sweeps; after the last, keep returning it (steady state).
            if (_sweeps.TryDequeue(out IReadOnlyList<ServerObservation>? next))
            {
                _sweeps.Enqueue(next);
                return Task.FromResult(next);
            }

            return Task.FromResult<IReadOnlyList<ServerObservation>>([]);
        }
    }

    private sealed class RecordingConnection : IAgentControlPlaneConnection
    {
        public ConcurrentBag<ServerRunState> StateChanges { get; } = [];

        public ConcurrentBag<ServerHealth> HealthChanges { get; } = [];

        public Task SendServerStateChangedAsync(
            ServerId serverId, ServerRunState runState, CancellationToken cancellationToken = default)
        {
            StateChanges.Add(runState);
            return Task.CompletedTask;
        }

        public Task SendHealthChangedAsync(
            ServerId serverId, ServerHealth health, string reason, HealthBreakdown breakdown,
            CancellationToken cancellationToken = default)
        {
            HealthChanges.Add(health);
            return Task.CompletedTask;
        }

        public Task SendMetricsReportAsync(
            IReadOnlyList<ServerMetricsSample> samples, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task SendHostCapacityAsync(HostCapacityReport report, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<ProtocolNegotiationResult> StartAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(ProtocolCompatibility.Negotiate(ProtocolVersion.Current, ProtocolVersionRange.Supported));

        public Task SendHeartbeatAsync(AgentHealthStatus health, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class StubTrustStore(AgentTrustMaterial? material) : IAgentTrustStore
    {
        public Task<AgentTrustMaterial?> TryLoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(material);

        public Task SaveAsync(AgentTrustMaterial material, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
