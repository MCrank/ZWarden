using ZWarden.Contracts.Protocol.Messages;

namespace ZWarden.Agent.Health;

/// <summary>
/// Samples the runtime metrics of every owned Server (F16): CPU and memory from <c>docker stats</c> (the eleventh
/// allowlist entry, ADR 0008 as amended by F16), disk from the host bind-mount. Player count is always
/// <c>null</c> in v1.0 (it needs RCON — F18). Read-only throughout; one Server that cannot be sampled degrades to
/// zeros/nulls rather than failing the sweep.
/// </summary>
public interface IServerMetricsSampler
{
    /// <summary>Takes one metrics sample for every owned Server.</summary>
    Task<IReadOnlyList<ServerMetricsSample>> SampleAllAsync(CancellationToken cancellationToken);
}
