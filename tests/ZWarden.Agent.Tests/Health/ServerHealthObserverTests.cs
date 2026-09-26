using Microsoft.Extensions.Options;
using ZWarden.Agent.Configuration;
using ZWarden.Agent.Docker;
using ZWarden.Agent.Health;
using ZWarden.Contracts.Protocol;
using ZWarden.Domain.Ids;

namespace ZWarden.Agent.Tests.Health;

/// <summary>
/// F16 PR-A / #199: the observer turns each owned container's inspect facts and a best-effort network probe into a
/// rolled-up <see cref="ServerObservation"/>. It probes the network only for a running container that has an IP on
/// the ZWarden network, targeting that IP's game UDP port (not the Agent's loopback); a stopped container, or one
/// with no address on the network, is never probed.
/// </summary>
public class ServerHealthObserverTests
{
    private static readonly ServerId Server = ServerId.New();
    private const string Network = "zwarden-pz";
    private const string ContainerIp = "172.22.0.3";

    private static IOptions<AgentOptions> Options() =>
        Microsoft.Extensions.Options.Options.Create(new AgentOptions { NetworkName = Network });

    private static Dictionary<string, string> OnNetwork(string ip) =>
        new(StringComparer.Ordinal) { [Network] = ip };

    [Test]
    public async Task A_running_container_with_a_reachable_game_port_is_healthy_and_was_probed_at_the_container_ip()
    {
        FakeInspectRuntime runtime = new(new ObservedContainer(
            Server, new ContainerHealthFacts("running", "healthy", 0, false, [], OnNetwork(ContainerIp))));
        RecordingProbe probe = new(reachable: true);
        ServerHealthObserver observer = new(runtime, probe, Options());

        IReadOnlyList<ServerObservation> observed = await observer.ObserveAllAsync(CancellationToken.None);

        await Assert.That(observed.Count).IsEqualTo(1);
        await Assert.That(observed[0].Health).IsEqualTo(ServerHealth.Healthy);
        await Assert.That(observed[0].RunState).IsEqualTo(ServerRunState.Running);
        // #199: probed the container's own ZWarden-network IP at the internal game port, not the Agent's loopback.
        await Assert.That(probe.ProbedHost).IsEqualTo(ContainerIp);
        await Assert.That(probe.ProbedPort).IsEqualTo(PortStrideAllocator.BaseGamePort);
    }

    [Test]
    public async Task A_running_container_with_an_unreachable_game_port_is_degraded()
    {
        FakeInspectRuntime runtime = new(new ObservedContainer(
            Server, new ContainerHealthFacts("running", "healthy", 0, false, [], OnNetwork(ContainerIp))));
        ServerHealthObserver observer = new(runtime, new RecordingProbe(reachable: false), Options());

        IReadOnlyList<ServerObservation> observed = await observer.ObserveAllAsync(CancellationToken.None);

        await Assert.That(observed[0].Health).IsEqualTo(ServerHealth.Degraded);
    }

    [Test]
    public async Task A_running_container_with_no_address_on_the_network_is_not_probed_and_stays_healthy()
    {
        // #199 regression guard: a running server whose address the Agent cannot resolve must not be probed —
        // an unknown (null) network result rolls up Healthy, never a false Degraded.
        FakeInspectRuntime runtime = new(new ObservedContainer(
            Server, new ContainerHealthFacts("running", "healthy", 0, false, [], NetworkAddresses: null)));
        RecordingProbe probe = new(reachable: false);
        ServerHealthObserver observer = new(runtime, probe, Options());

        IReadOnlyList<ServerObservation> observed = await observer.ObserveAllAsync(CancellationToken.None);

        await Assert.That(observed[0].Health).IsEqualTo(ServerHealth.Healthy);
        await Assert.That(probe.WasCalled).IsFalse();
    }

    [Test]
    public async Task A_stopped_container_is_not_network_probed()
    {
        FakeInspectRuntime runtime = new(new ObservedContainer(
            Server, new ContainerHealthFacts("exited", null, 0, false, [])));
        RecordingProbe probe = new(reachable: true);
        ServerHealthObserver observer = new(runtime, probe, Options());

        IReadOnlyList<ServerObservation> observed = await observer.ObserveAllAsync(CancellationToken.None);

        await Assert.That(observed[0].Health).IsEqualTo(ServerHealth.Stopped);
        await Assert.That(probe.WasCalled).IsFalse();
    }

    private sealed class FakeInspectRuntime(params ObservedContainer[] observed) : IContainerRuntime
    {
        public Task<HostMemory> ReadHostMemoryAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<ObservedContainer>> InspectManagedAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ObservedContainer>>(observed);

        public Task<DockerHealth> ProbeHealthAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<ManagedContainer>> ListManagedAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<PortAllocation> ClaimRequestedPortsAsync(int gamePort, ServerId forServer, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ServerContainer?> InspectServerAsync(ServerId serverId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task RemoveAsync(ServerId serverId, CancellationToken cancellationToken) => throw new NotSupportedException();

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

        public string? ProbedHost { get; private set; }

        public int? ProbedPort { get; private set; }

        public Task<bool?> IsUdpPortReachableAsync(string host, int port, CancellationToken cancellationToken)
        {
            WasCalled = true;
            ProbedHost = host;
            ProbedPort = port;
            return Task.FromResult(reachable);
        }
    }
}
