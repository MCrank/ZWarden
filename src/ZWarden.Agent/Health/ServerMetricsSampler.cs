using Docker.DotNet;
using Microsoft.Extensions.Options;
using ZWarden.Agent.Configuration;
using ZWarden.Agent.Docker;
using ZWarden.Agent.Players;
using ZWarden.Agent.SteamCmd;
using ZWarden.Contracts.Protocol.Messages;

namespace ZWarden.Agent.Health;

/// <summary>
/// The default <see cref="IServerMetricsSampler"/> (F16). For each owned container it reads CPU/memory from
/// <c>docker stats</c> (only when running — a stopped container has no live stats and reports zeros) via the pure
/// <see cref="ContainerStatsCalculator"/>, and disk from the Server's bind-mount directory under
/// <see cref="AgentOptions.DataMountRoot"/>. The fleet facts (#257) ride along: the last RCON player count from
/// <see cref="IServerPlayerCounts"/> and the container's start time from inspect (both running only), and the build
/// id from the install volume's Steam manifest. The stats/inspect id comes from the owned-container list, so no
/// foreign container is ever sampled.
/// </summary>
public sealed class ServerMetricsSampler : IServerMetricsSampler
{
    private readonly IContainerRuntime _runtime;
    private readonly IDockerEngine _engine;
    private readonly IServerDiskUsageReader _disk;
    private readonly IServerPlayerCounts _players;
    private readonly IServerInstallPaths _installPaths;
    private readonly AgentOptions _options;
    private readonly TimeProvider _clock;

    public ServerMetricsSampler(
        IContainerRuntime runtime,
        IDockerEngine engine,
        IServerDiskUsageReader disk,
        IServerPlayerCounts players,
        IServerInstallPaths installPaths,
        IOptions<AgentOptions> options,
        TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(disk);
        ArgumentNullException.ThrowIfNull(players);
        ArgumentNullException.ThrowIfNull(installPaths);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(clock);
        _runtime = runtime;
        _engine = engine;
        _disk = disk;
        _players = players;
        _installPaths = installPaths;
        _options = options.Value;
        _clock = clock;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ServerMetricsSample>> SampleAllAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<ManagedContainer> managed = await _runtime.ListManagedAsync(cancellationToken).ConfigureAwait(false);
        DateTimeOffset now = _clock.GetUtcNow();

        List<ServerMetricsSample> samples = [];
        foreach (ManagedContainer container in managed)
        {
            bool running = IsRunning(container);
            (double cpu, long memUsed, long memLimit) = running
                ? await ReadCpuMemoryAsync(container, cancellationToken).ConfigureAwait(false)
                : (0, 0, 0); // A stopped container has no live resource usage.
            DateTimeOffset? startedAt = running
                ? await ReadStartedAtAsync(container, cancellationToken).ConfigureAwait(false)
                : null;
            PlayerCountReading? players = running ? _players.GetLatest(container.ServerId) : null;
            DiskUsage disk = _disk.Read(Path.Combine(_options.DataMountRoot, container.ServerId.ToString()));

            samples.Add(new ServerMetricsSample(
                container.ServerId,
                cpu,
                memUsed,
                memLimit,
                disk.UsedBytes,
                disk.CapacityBytes,
                players?.Count,
                now,
                players?.SampledAt,
                startedAt,
                _installPaths.ReadInstalledBuildId(container.ServerId)));
        }

        return samples;
    }

    private static bool IsRunning(ManagedContainer container) =>
        string.Equals(container.State, "running", StringComparison.OrdinalIgnoreCase);

    private async Task<(double Cpu, long MemoryUsed, long MemoryLimit)> ReadCpuMemoryAsync(
        ManagedContainer container, CancellationToken cancellationToken)
    {
        try
        {
            ContainerStatsSnapshot stats = await _engine.StatsAsync(container.DockerId, cancellationToken)
                .ConfigureAwait(false);
            return (
                ContainerStatsCalculator.CpuPercent(stats),
                ContainerStatsCalculator.MemoryUsedBytes(stats),
                ContainerStatsCalculator.MemoryLimitBytes(stats));
        }
        catch (DockerApiException)
        {
            // The container stopped or vanished between the list and the stats read: report zeros this cycle.
            return (0, 0, 0);
        }
    }

    private async Task<DateTimeOffset?> ReadStartedAtAsync(ManagedContainer container, CancellationToken cancellationToken)
    {
        try
        {
            EngineContainer inspected = await _engine.InspectAsync(container.DockerId, cancellationToken)
                .ConfigureAwait(false);
            return inspected.StartedAt;
        }
        catch (DockerApiException)
        {
            // The container vanished between the list and the inspect: no uptime this cycle.
            return null;
        }
    }
}
