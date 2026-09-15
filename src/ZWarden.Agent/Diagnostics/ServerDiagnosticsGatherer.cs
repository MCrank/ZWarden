using Microsoft.Extensions.Options;
using ZWarden.Agent.Configuration;
using ZWarden.Agent.Docker;
using ZWarden.Agent.Health;
using ZWarden.Agent.Rcon;
using ZWarden.Agent.SteamCmd;
using ZWarden.Contracts.Protocol;
using ZWarden.Contracts.Protocol.Messages;
using ZWarden.Domain.Ids;
using static ZWarden.Agent.Diagnostics.DiagnosticFacts;

namespace ZWarden.Agent.Diagnostics;

/// <summary>
/// Gathers the per-server infrastructure diagnostics for a <c>GatherServerDiagnostics</c> Operation (F29): RCON
/// reachability, the game/query port, the Server's data filesystem, and the installed Project Zomboid build. Every
/// check is read-only and <b>fail-soft per domain</b> — a probe fault becomes a Fail/Warn check with a legible
/// detail, never a thrown gather. F29 PR-C adds the content checks (mods, config, compatibility) to this bundle.
/// </summary>
public interface IServerDiagnosticsGatherer
{
    Task<ServerDiagnosticsResult> GatherAsync(ServerId serverId, CancellationToken cancellationToken);
}

/// <inheritdoc />
public sealed class ServerDiagnosticsGatherer : IServerDiagnosticsGatherer
{
    private readonly IRconHealthProbe _rcon;
    private readonly IContainerRuntime _runtime;
    private readonly INetworkReachabilityProbe _network;
    private readonly IServerDiskUsageReader _disk;
    private readonly IServerInstallPaths _installPaths;
    private readonly AgentOptions _options;

    public ServerDiagnosticsGatherer(
        IRconHealthProbe rcon,
        IContainerRuntime runtime,
        INetworkReachabilityProbe network,
        IServerDiskUsageReader disk,
        IServerInstallPaths installPaths,
        IOptions<AgentOptions> options)
    {
        ArgumentNullException.ThrowIfNull(rcon);
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(network);
        ArgumentNullException.ThrowIfNull(disk);
        ArgumentNullException.ThrowIfNull(installPaths);
        ArgumentNullException.ThrowIfNull(options);
        _rcon = rcon;
        _runtime = runtime;
        _network = network;
        _disk = disk;
        _installPaths = installPaths;
        _options = options.Value;
    }

    /// <inheritdoc />
    public async Task<ServerDiagnosticsResult> GatherAsync(ServerId serverId, CancellationToken cancellationToken)
    {
        List<DiagnosticCheckFact> checks =
        [
            await RconAsync(serverId, cancellationToken).ConfigureAwait(false),
            await GamePortAsync(serverId, cancellationToken).ConfigureAwait(false),
            Filesystem(serverId, cancellationToken),
            SteamCmd(serverId, cancellationToken),
        ];
        return new ServerDiagnosticsResult(checks);
    }

    private async Task<DiagnosticCheckFact> RconAsync(ServerId serverId, CancellationToken cancellationToken)
    {
        try
        {
            RconHealthResult result = await _rcon.ProbeAsync(serverId, cancellationToken).ConfigureAwait(false);
            if (result.Authenticated)
            {
                return Fact(DiagnosticDomain.Rcon, ProbeStatus.Pass, "RCON is reachable and authenticated.");
            }

            return Fact(DiagnosticDomain.Rcon, ProbeStatus.Fail, "RCON is not usable.", result.Detail);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Fact(DiagnosticDomain.Rcon, ProbeStatus.Fail, "RCON could not be probed.", ex.Message);
        }
    }

    private async Task<DiagnosticCheckFact> GamePortAsync(ServerId serverId, CancellationToken cancellationToken)
    {
        try
        {
            IReadOnlyList<ObservedContainer> containers = await _runtime.InspectManagedAsync(cancellationToken).ConfigureAwait(false);
            ObservedContainer? container = containers.FirstOrDefault(c => c.ServerId == serverId);
            if (container is null)
            {
                return Fact(DiagnosticDomain.GamePort, ProbeStatus.Skipped, "No running container for this server on this host.");
            }

            PublishedPort port = container.Facts.Ports.FirstOrDefault(p =>
                p.ContainerPort == PortStrideAllocator.BaseGamePort
                && string.Equals(p.Protocol, "udp", StringComparison.OrdinalIgnoreCase)
                && p.HostPort != 0);
            if (port.HostPort == 0)
            {
                return Fact(DiagnosticDomain.GamePort, ProbeStatus.Skipped, "The server publishes no game UDP port.");
            }

            bool? reachable = await _network.IsUdpPortReachableAsync(port.HostPort, cancellationToken).ConfigureAwait(false);
            return reachable switch
            {
                true => Fact(DiagnosticDomain.GamePort, ProbeStatus.Pass, $"The game UDP port {port.HostPort} is reachable."),
                false => Fact(DiagnosticDomain.GamePort, ProbeStatus.Fail, $"The game UDP port {port.HostPort} is unreachable."),
                null => Fact(DiagnosticDomain.GamePort, ProbeStatus.Warn, $"The game UDP port {port.HostPort} reachability is unknown."),
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Fact(DiagnosticDomain.GamePort, ProbeStatus.Warn, "The game port could not be probed.", ex.Message);
        }
    }

    private DiagnosticCheckFact Filesystem(ServerId serverId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            string dataDir = Path.Combine(_options.DataMountRoot, serverId.ToString());
            if (!Directory.Exists(dataDir))
            {
                return Fact(DiagnosticDomain.Filesystem, ProbeStatus.Warn, "The server has no data directory on this host yet.");
            }

            return DiagnosticFacts.Filesystem("Server data", _disk.Read(dataDir));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Fact(DiagnosticDomain.Filesystem, ProbeStatus.Warn, "The server data directory could not be checked.", ex.Message);
        }
    }

    private DiagnosticCheckFact SteamCmd(ServerId serverId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            string? buildId = _installPaths.ReadInstalledBuildId(serverId);
            return buildId is { Length: > 0 }
                ? Fact(DiagnosticDomain.SteamCmd, ProbeStatus.Pass, "A Project Zomboid build is installed.", $"build {buildId}")
                : Fact(DiagnosticDomain.SteamCmd, ProbeStatus.Warn, "No installed Project Zomboid build was found.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Fact(DiagnosticDomain.SteamCmd, ProbeStatus.Warn, "The installed build could not be read.", ex.Message);
        }
    }
}
