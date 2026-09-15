using Microsoft.Extensions.Options;
using ZWarden.Agent.Configuration;
using ZWarden.Agent.Docker;
using ZWarden.Agent.Health;
using ZWarden.Contracts.Protocol;
using ZWarden.Contracts.Protocol.Messages;
using static ZWarden.Agent.Diagnostics.DiagnosticFacts;

namespace ZWarden.Agent.Diagnostics;

/// <summary>
/// Gathers the host-level infrastructure diagnostics for a <c>GatherHostDiagnostics</c> Operation (F29): Docker
/// daemon connectivity and the host filesystem under the Agent's <c>DataMountRoot</c>. Read-only, and
/// <b>fail-soft per domain</b> — a probe fault becomes a Fail/Warn check with a legible detail, never a thrown
/// gather, so one bad domain never sinks the bundle. The verdicts are the Agent's; ZWarden.Web maps them onto the
/// engine's report.
/// </summary>
public interface IHostDiagnosticsGatherer
{
    Task<HostDiagnosticsResult> GatherAsync(CancellationToken cancellationToken);
}

/// <inheritdoc />
public sealed class HostDiagnosticsGatherer : IHostDiagnosticsGatherer
{
    private readonly IContainerRuntime _runtime;
    private readonly IServerDiskUsageReader _disk;
    private readonly AgentOptions _options;

    public HostDiagnosticsGatherer(IContainerRuntime runtime, IServerDiskUsageReader disk, IOptions<AgentOptions> options)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(disk);
        ArgumentNullException.ThrowIfNull(options);
        _runtime = runtime;
        _disk = disk;
        _options = options.Value;
    }

    /// <inheritdoc />
    public async Task<HostDiagnosticsResult> GatherAsync(CancellationToken cancellationToken)
    {
        List<DiagnosticCheckFact> checks =
        [
            await DockerAsync(cancellationToken).ConfigureAwait(false),
            Filesystem(cancellationToken),
        ];
        return new HostDiagnosticsResult(checks);
    }

    private async Task<DiagnosticCheckFact> DockerAsync(CancellationToken cancellationToken)
    {
        try
        {
            DockerHealth health = await _runtime.ProbeHealthAsync(cancellationToken).ConfigureAwait(false);
            return health.DaemonReachable
                ? Fact(DiagnosticDomain.Docker, ProbeStatus.Pass, $"Docker is reachable (API {health.ApiVersion ?? "unknown"}).")
                : Fact(DiagnosticDomain.Docker, ProbeStatus.Fail, "The Docker daemon is unreachable.", health.Detail);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Fact(DiagnosticDomain.Docker, ProbeStatus.Fail, "The Docker daemon could not be probed.", ex.Message);
        }
    }

    private DiagnosticCheckFact Filesystem(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            if (!Directory.Exists(_options.DataMountRoot))
            {
                return Fact(DiagnosticDomain.Filesystem, ProbeStatus.Fail, "The Agent data-mount root is missing.");
            }

            return DiagnosticFacts.Filesystem("Agent data volume", _disk.Read(_options.DataMountRoot));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Fact(DiagnosticDomain.Filesystem, ProbeStatus.Warn, "The Agent data volume could not be checked.", ex.Message);
        }
    }
}
