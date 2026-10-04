using Microsoft.Extensions.Options;
using ZWarden.Agent.Configuration;
using ZWarden.Agent.Docker;
using ZWarden.Contracts.Protocol.Messages;

namespace ZWarden.Agent.Health;

/// <summary>
/// Composes the host's memory budget for the new-server wizard (#230): the Docker host's total RAM and the limits
/// already committed to this Agent's containers (from <see cref="IContainerRuntime.ReadHostMemoryAsync"/>), plus the
/// Agent's own sizing policy — the per-container overhead, the default heap, and the RAM reserved for the host. It also
/// carries the host's live vitals for the Hosts card meters (#170, <see cref="IHostVitalsReader"/>).
/// </summary>
public interface IHostCapacityReader
{
    /// <summary>Reads the current budget. Throws what the Docker read throws; the caller skips that cycle.</summary>
    Task<HostCapacityReport> ReadAsync(CancellationToken cancellationToken);
}

/// <inheritdoc />
public sealed class HostCapacityReader : IHostCapacityReader
{
    private readonly IContainerRuntime _runtime;
    private readonly IHostVitalsReader _vitals;
    private readonly AgentOptions _options;

    public HostCapacityReader(IContainerRuntime runtime, IHostVitalsReader vitals, IOptions<AgentOptions> options)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(vitals);
        ArgumentNullException.ThrowIfNull(options);
        _runtime = runtime;
        _vitals = vitals;
        _options = options.Value;
    }

    /// <inheritdoc />
    public async Task<HostCapacityReport> ReadAsync(CancellationToken cancellationToken)
    {
        HostMemory memory = await _runtime.ReadHostMemoryAsync(cancellationToken).ConfigureAwait(false);
        HostVitals vitals = _vitals.Read();
        return new HostCapacityReport(
            memory.TotalBytes,
            memory.CommittedBytes,
            _options.MemoryOverheadBytes,
            _options.DefaultHeapSizeBytes,
            _options.HostMemoryReserveBytes,
            vitals.CpuPercent,
            vitals.MemoryUsedBytes,
            vitals.DiskFreeBytes,
            vitals.DiskTotalBytes,
            vitals.CpuCores,
            vitals.LoadAverage1,
            vitals.LoadAverage5,
            vitals.LoadAverage15);
    }
}
