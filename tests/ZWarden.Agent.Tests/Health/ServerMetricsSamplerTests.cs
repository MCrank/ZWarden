using Microsoft.Extensions.Options;
using ZWarden.Agent.Configuration;
using ZWarden.Agent.Docker;
using ZWarden.Agent.Health;
using ZWarden.Agent.Players;
using ZWarden.Agent.SteamCmd;
using ZWarden.Agent.Tests.Docker;
using ZWarden.Contracts.Protocol.Messages;
using ZWarden.Domain.Ids;

namespace ZWarden.Agent.Tests.Health;

/// <summary>
/// F16 PR-B: the metrics sampler reads CPU/memory from docker stats for running containers (via the pure
/// calculator) and disk from the bind-mount. A stopped container samples as zeros without touching stats. #257 adds
/// the fleet facts: the last RCON player count, the container start time (running only) and the manifest build id.
/// </summary>
public class ServerMetricsSamplerTests
{
    private static readonly ServerId RunningServer = ServerId.New();
    private static readonly ServerId StoppedServer = ServerId.New();

    private static readonly DateTimeOffset Now = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);

    private static ServerMetricsSampler Build(
        FakeDockerEngine engine, StubPlayerCounts? players = null, string? buildId = null)
    {
        FakeListRuntime runtime = new(
        [
            new ManagedContainer("run1", RunningServer, "running"),
            new ManagedContainer("stop1", StoppedServer, "exited"),
        ]);
        return new ServerMetricsSampler(
            runtime,
            engine,
            new StubDiskReader(new DiskUsage(1_000, 50_000)),
            players ?? new StubPlayerCounts(),
            new StubInstallPaths(buildId),
            Options.Create(new AgentOptions { DataMountRoot = Path.GetTempPath() }),
            TimeProvider.System);
    }

    [Test]
    public async Task A_running_container_reports_cpu_memory_and_disk()
    {
        FakeDockerEngine engine = new()
        {
            // 20/100 * 4 CPUs * 100 = 80%; mem used = 3000 - 1000 = 2000; limit 4000.
            StatsResult = new ContainerStatsSnapshot(120, 100, 1_100, 1_000, 4, 3_000, 1_000, 4_000),
            InspectResult = Inspected(startedAt: null),
        };
        ServerMetricsSampler sampler = Build(engine);

        IReadOnlyList<ServerMetricsSample> samples = await sampler.SampleAllAsync(CancellationToken.None);
        ServerMetricsSample running = samples.First(s => s.ServerId == RunningServer);

        await Assert.That(running.CpuPercent).IsEqualTo(80d).Within(0.0001);
        await Assert.That(running.MemoryUsedBytes).IsEqualTo(2_000);
        await Assert.That(running.MemoryLimitBytes).IsEqualTo(4_000);
        await Assert.That(running.DiskUsedBytes).IsEqualTo(1_000);
        await Assert.That(running.DiskCapacityBytes).IsEqualTo(50_000);
        await Assert.That(running.PlayerCount).IsNull();
    }

    [Test]
    public async Task A_stopped_container_reports_zeros_and_does_not_read_stats()
    {
        // No StatsResult arranged; if the sampler called stats on the stopped container the fake would still
        // return default(snapshot) => zeros, so assert via the running path that stopped stays zero regardless.
        FakeDockerEngine engine = new()
        {
            StatsResult = new ContainerStatsSnapshot(120, 100, 1_100, 1_000, 4, 3_000, 1_000, 4_000),
            InspectResult = Inspected(startedAt: null),
        };
        ServerMetricsSampler sampler = Build(engine);

        IReadOnlyList<ServerMetricsSample> samples = await sampler.SampleAllAsync(CancellationToken.None);
        ServerMetricsSample stopped = samples.First(s => s.ServerId == StoppedServer);

        await Assert.That(stopped.CpuPercent).IsEqualTo(0d);
        await Assert.That(stopped.MemoryUsedBytes).IsEqualTo(0);
        await Assert.That(stopped.MemoryLimitBytes).IsEqualTo(0);
        // Disk is still read for a stopped server (its files exist on disk).
        await Assert.That(stopped.DiskUsedBytes).IsEqualTo(1_000);
    }

    [Test]
    public async Task A_running_container_carries_its_player_count_start_time_and_build()
    {
        DateTimeOffset started = Now.AddHours(-2);
        DateTimeOffset counted = Now.AddMinutes(-1);
        FakeDockerEngine engine = new()
        {
            StatsResult = new ContainerStatsSnapshot(120, 100, 1_100, 1_000, 4, 3_000, 1_000, 4_000),
            InspectResult = Inspected(started),
        };
        StubPlayerCounts players = new();
        players.Readings[RunningServer] = new PlayerCountReading(7, counted);
        ServerMetricsSampler sampler = Build(engine, players, buildId: "19876543");

        IReadOnlyList<ServerMetricsSample> samples = await sampler.SampleAllAsync(CancellationToken.None);
        ServerMetricsSample running = samples.First(s => s.ServerId == RunningServer);

        await Assert.That(running.PlayerCount).IsEqualTo(7);
        await Assert.That(running.PlayerCountSampledAt).IsEqualTo(counted);
        await Assert.That(running.StartedAt).IsEqualTo(started);
        await Assert.That(running.InstalledBuildId).IsEqualTo("19876543");
    }

    [Test]
    public async Task A_stopped_container_has_no_players_or_start_time_but_still_reports_its_build()
    {
        FakeDockerEngine engine = new()
        {
            StatsResult = new ContainerStatsSnapshot(120, 100, 1_100, 1_000, 4, 3_000, 1_000, 4_000),
            InspectResult = Inspected(Now.AddHours(-2)),
        };
        StubPlayerCounts players = new();
        players.Readings[StoppedServer] = new PlayerCountReading(2, Now);
        ServerMetricsSampler sampler = Build(engine, players, buildId: "19876543");

        IReadOnlyList<ServerMetricsSample> samples = await sampler.SampleAllAsync(CancellationToken.None);
        ServerMetricsSample stopped = samples.First(s => s.ServerId == StoppedServer);

        await Assert.That(stopped.PlayerCount).IsNull();
        await Assert.That(stopped.PlayerCountSampledAt).IsNull();
        await Assert.That(stopped.StartedAt).IsNull();
        await Assert.That(stopped.InstalledBuildId).IsEqualTo("19876543");
    }

    private static EngineContainer Inspected(DateTimeOffset? startedAt) =>
        new("run1", new Dictionary<string, string>(), "running", [], StartedAt: startedAt);

    private sealed class StubPlayerCounts : IServerPlayerCounts
    {
        public Dictionary<ServerId, PlayerCountReading> Readings { get; } = [];

        public PlayerCountReading? GetLatest(ServerId serverId) =>
            Readings.TryGetValue(serverId, out PlayerCountReading reading) ? reading : null;
    }

    private sealed class StubInstallPaths(string? buildId) : IServerInstallPaths
    {
        public void WriteUpdateRequest(ServerId serverId, OperationId operationId) => throw new NotSupportedException();

        public string? ReadInstalledBuildId(ServerId serverId) => buildId;

        public string GetWorkshopContentRoot(ServerId serverId) => throw new NotSupportedException();
    }

    private sealed class FakeListRuntime(IReadOnlyList<ManagedContainer> managed) : IContainerRuntime
    {
        public Task<IReadOnlyList<ManagedContainer>> ListManagedAsync(CancellationToken cancellationToken) =>
            Task.FromResult(managed);

        public Task<DockerHealth> ProbeHealthAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<ObservedContainer>> InspectManagedAsync(CancellationToken cancellationToken) =>
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

    private sealed class StubDiskReader(DiskUsage usage) : IServerDiskUsageReader
    {
        public DiskUsage Read(string dataDirectory) => usage;
    }
}
