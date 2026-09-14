using ZWarden.Agent.Docker;
using ZWarden.Agent.Health;
using ZWarden.Contracts.Protocol;
using ZWarden.Domain.Ids;

namespace ZWarden.Agent.Tests.Health;

/// <summary>
/// F16 PR-A: the observer turns each owned container's inspect facts and a best-effort network probe into a
/// rolled-up <see cref="ServerObservation"/>. It probes the network only for a running container, and only its
/// published game UDP port; a stopped container is never probed.
/// </summary>
public class ServerHealthObserverTests
{
    private static readonly ServerId Server = ServerId.New();

    [Test]
    public async Task A_running_container_with_a_reachable_game_port_is_healthy_and_was_probed()
    {
        PublishedPort game = new(HostPort: 27015, ContainerPort: PortStrideAllocator.BaseGamePort, Protocol: "udp");
        FakeInspectRuntime runtime = new(new ObservedContainer(
            Server, new ContainerHealthFacts("running", "healthy", 0, false, [game])));
        RecordingProbe probe = new(reachable: true);
        ServerHealthObserver observer = new(runtime, probe);

        IReadOnlyList<ServerObservation> observed = await observer.ObserveAllAsync(CancellationToken.None);

        await Assert.That(observed.Count).IsEqualTo(1);
        await Assert.That(observed[0].Health).IsEqualTo(ServerHealth.Healthy);
        await Assert.That(observed[0].RunState).IsEqualTo(ServerRunState.Running);
        await Assert.That(probe.ProbedPort).IsEqualTo(27015);
    }

    [Test]
    public async Task A_running_container_with_an_unreachable_game_port_is_degraded()
    {
        PublishedPort game = new(HostPort: 27015, ContainerPort: PortStrideAllocator.BaseGamePort, Protocol: "udp");
        FakeInspectRuntime runtime = new(new ObservedContainer(
            Server, new ContainerHealthFacts("running", "healthy", 0, false, [game])));
        ServerHealthObserver observer = new(runtime, new RecordingProbe(reachable: false));

        IReadOnlyList<ServerObservation> observed = await observer.ObserveAllAsync(CancellationToken.None);

        await Assert.That(observed[0].Health).IsEqualTo(ServerHealth.Degraded);
    }

    [Test]
    public async Task A_stopped_container_is_not_network_probed()
    {
        FakeInspectRuntime runtime = new(new ObservedContainer(
            Server, new ContainerHealthFacts("exited", null, 0, false, [])));
        RecordingProbe probe = new(reachable: true);
        ServerHealthObserver observer = new(runtime, probe);

        IReadOnlyList<ServerObservation> observed = await observer.ObserveAllAsync(CancellationToken.None);

        await Assert.That(observed[0].Health).IsEqualTo(ServerHealth.Stopped);
        await Assert.That(probe.WasCalled).IsFalse();
    }

    private sealed class FakeInspectRuntime(params ObservedContainer[] observed) : IContainerRuntime
    {
        public Task<IReadOnlyList<ObservedContainer>> InspectManagedAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ObservedContainer>>(observed);

        public Task<DockerHealth> ProbeHealthAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<ManagedContainer>> ListManagedAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<PortAllocation> AllocateNextPortsAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<string> CreateAsync(PzContainerSpec spec, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task StartAsync(string containerId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task StopAsync(string containerId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task RestartAsync(string containerId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task StartAsync(ServerId serverId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task StopAsync(ServerId serverId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task RestartAsync(ServerId serverId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<string> ReadServerLogsAsync(ServerId serverId, DateTimeOffset? since, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<string?> ResolveNetworkAddressAsync(ServerId serverId, string networkName, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task FollowServerLogsAsync(ServerId serverId, int tailLines, Func<ContainerLogFrame, CancellationToken, ValueTask> onFrame, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class RecordingProbe(bool? reachable) : INetworkReachabilityProbe
    {
        public bool WasCalled { get; private set; }

        public int? ProbedPort { get; private set; }

        public Task<bool?> IsUdpPortReachableAsync(int port, CancellationToken cancellationToken)
        {
            WasCalled = true;
            ProbedPort = port;
            return Task.FromResult(reachable);
        }
    }
}
