using Docker.DotNet;
using Microsoft.Extensions.Options;
using ZWarden.Agent.Configuration;
using ZWarden.Agent.Docker;
using ZWarden.Contracts.Protocol.Messages;

namespace ZWarden.Agent.Health;

/// <summary>
/// The default <see cref="IServerMetricsSampler"/> (F16). For each owned container it reads CPU/memory from
/// <c>docker stats</c> (only when running — a stopped container has no live stats and reports zeros) via the pure
/// <see cref="ContainerStatsCalculator"/>, and disk from the Server's bind-mount directory under
/// <see cref="AgentOptions.DataMountRoot"/>. The stats id comes from the owned-container list, so no foreign
/// container is ever sampled.
/// </summary>
public sealed class ServerMetricsSampler : IServerMetricsSampler
{
    private readonly IContainerRuntime _runtime;
    private readonly IDockerEngine _engine;
    private readonly IServerDiskUsageReader _disk;
    private readonly AgentOptions _options;
    private readonly TimeProvider _clock;

    public ServerMetricsSampler(
        IContainerRuntime runtime,
        IDockerEngine engine,
        IServerDiskUsageReader disk,
        IOptions<AgentOptions> options,
        TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(disk);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(clock);
        _runtime = runtime;
        _engine = engine;
        _disk = disk;
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
            (double cpu, long memUsed, long memLimit) = await ReadCpuMemoryAsync(container, cancellationToken)
                .ConfigureAwait(false);
            DiskUsage disk = _disk.Read(Path.Combine(_options.DataMountRoot, container.ServerId.ToString()));

            samples.Add(new ServerMetricsSample(
                container.ServerId,
                cpu,
                memUsed,
                memLimit,
                disk.UsedBytes,
                disk.CapacityBytes,
                PlayerCount: null, // RCON is F18; no player count in v1.0.
                now));
        }

        return samples;
    }

    private async Task<(double Cpu, long MemoryUsed, long MemoryLimit)> ReadCpuMemoryAsync(
        ManagedContainer container, CancellationToken cancellationToken)
    {
        if (!string.Equals(container.State, "running", StringComparison.OrdinalIgnoreCase))
        {
            return (0, 0, 0); // A stopped container has no live resource usage.
        }

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
}
