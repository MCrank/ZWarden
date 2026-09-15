using Microsoft.Extensions.Options;
using ZWarden.Agent.Configuration;
using ZWarden.Agent.Docker;
using ZWarden.Agent.Health;
using ZWarden.Agent.Mods;
using ZWarden.Agent.Rcon;
using ZWarden.Agent.SteamCmd;
using ZWarden.Contracts.Protocol;
using ZWarden.Contracts.Protocol.Messages;
using ZWarden.Domain.Ids;
using ZWarden.PzConfig;
using ZWarden.PzConfig.Validation;
using static ZWarden.Agent.Diagnostics.DiagnosticFacts;

namespace ZWarden.Agent.Diagnostics;

/// <summary>
/// Gathers the per-server diagnostics for a <c>GatherServerDiagnostics</c> Operation (F29): RCON reachability, the
/// game/query port, the Server's data filesystem, the installed Project Zomboid build, and — the content domains
/// (F29 PR-C) — the Server's mods, configuration, and mod compatibility. Every check is read-only and
/// <b>fail-soft per domain</b> — a probe fault becomes a Fail/Warn check with a legible detail, never a thrown
/// gather, so one bad domain never sinks the bundle.
/// </summary>
public interface IServerDiagnosticsGatherer
{
    Task<ServerDiagnosticsResult> GatherAsync(ServerId serverId, CancellationToken cancellationToken);
}

/// <inheritdoc />
public sealed class ServerDiagnosticsGatherer : IServerDiagnosticsGatherer
{
    // The live INI the F20a/F21 seams read: <DataMountRoot>/<serverId>/Server/servertest.ini (research §4).
    private const string ConfigDirName = "Server";
    private const string ServerName = "servertest";

    private readonly IRconHealthProbe _rcon;
    private readonly IContainerRuntime _runtime;
    private readonly INetworkReachabilityProbe _network;
    private readonly IServerDiskUsageReader _disk;
    private readonly IServerInstallPaths _installPaths;
    private readonly IModDiscovery _mods;
    private readonly IPzConfigParser _configParser;
    private readonly IPzConfigValidator _configValidator;
    private readonly AgentOptions _options;

    public ServerDiagnosticsGatherer(
        IRconHealthProbe rcon,
        IContainerRuntime runtime,
        INetworkReachabilityProbe network,
        IServerDiskUsageReader disk,
        IServerInstallPaths installPaths,
        IModDiscovery mods,
        IPzConfigParser configParser,
        IPzConfigValidator configValidator,
        IOptions<AgentOptions> options)
    {
        ArgumentNullException.ThrowIfNull(rcon);
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(network);
        ArgumentNullException.ThrowIfNull(disk);
        ArgumentNullException.ThrowIfNull(installPaths);
        ArgumentNullException.ThrowIfNull(mods);
        ArgumentNullException.ThrowIfNull(configParser);
        ArgumentNullException.ThrowIfNull(configValidator);
        ArgumentNullException.ThrowIfNull(options);
        _rcon = rcon;
        _runtime = runtime;
        _network = network;
        _disk = disk;
        _installPaths = installPaths;
        _mods = mods;
        _configParser = configParser;
        _configValidator = configValidator;
        _options = options.Value;
    }

    /// <inheritdoc />
    public async Task<ServerDiagnosticsResult> GatherAsync(ServerId serverId, CancellationToken cancellationToken)
    {
        (DiagnosticCheckFact mod, DiagnosticCheckFact compatibility) = await ModsAsync(serverId, cancellationToken).ConfigureAwait(false);
        List<DiagnosticCheckFact> checks =
        [
            await RconAsync(serverId, cancellationToken).ConfigureAwait(false),
            await GamePortAsync(serverId, cancellationToken).ConfigureAwait(false),
            Filesystem(serverId, cancellationToken),
            SteamCmd(serverId, cancellationToken),
            mod,
            compatibility,
            Config(serverId, cancellationToken),
        ];
        return new ServerDiagnosticsResult(checks);
    }

    private async Task<(DiagnosticCheckFact Mod, DiagnosticCheckFact Compatibility)> ModsAsync(
        ServerId serverId, CancellationToken cancellationToken)
    {
        try
        {
            ModDiscoveryResult discovery = await _mods.DiscoverAsync(serverId, cancellationToken).ConfigureAwait(false);
            int missing = discovery.Findings.Count(f => f.Kind == ModCompatKind.EnabledButMissing);
            int referenced = discovery.Findings.Count(f => f.Kind == ModCompatKind.ReferencedNotInstalled);
            int duplicate = discovery.Findings.Count(f => f.Kind == ModCompatKind.DuplicateModId);

            // Mod domain: will the server load the mods it enables? Enabled-but-missing fails the load.
            DiagnosticCheckFact mod = missing > 0
                ? Fact(DiagnosticDomain.Mod, ProbeStatus.Fail, $"{missing} enabled mod(s) are missing from disk.")
                : referenced > 0
                    ? Fact(DiagnosticDomain.Mod, ProbeStatus.Warn, $"{referenced} referenced Workshop item(s) are not installed.")
                    : Fact(DiagnosticDomain.Mod, ProbeStatus.Pass, $"{discovery.EnabledModIds.Count} mod(s) enabled; all present.");

            // Compatibility domain: a mod id provided by more than one installed item is a load conflict.
            DiagnosticCheckFact compatibility = duplicate > 0
                ? Fact(DiagnosticDomain.Compatibility, ProbeStatus.Warn, $"{duplicate} mod id(s) are provided by more than one Workshop item.")
                : Fact(DiagnosticDomain.Compatibility, ProbeStatus.Pass, "No mod load conflicts were detected.");

            return (mod, compatibility);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return (
                Fact(DiagnosticDomain.Mod, ProbeStatus.Warn, "The server's mods could not be checked.", ex.Message),
                Fact(DiagnosticDomain.Compatibility, ProbeStatus.Warn, "Mod compatibility could not be checked.", ex.Message));
        }
    }

    private DiagnosticCheckFact Config(ServerId serverId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            string path = Path.Combine(_options.DataMountRoot, serverId.ToString(), ConfigDirName, $"{ServerName}.ini");
            if (!File.Exists(path))
            {
                return Fact(DiagnosticDomain.Config, ProbeStatus.Warn, "The server has no configuration file yet.");
            }

            PzConfigReadResult read = _configParser.Open(PzConfigKind.Ini, File.ReadAllBytes(path));
            if (!read.Parsed || read.Document is not { } document)
            {
                return Fact(DiagnosticDomain.Config, ProbeStatus.Fail, "The server configuration file does not parse.");
            }

            int errors = _configValidator.Validate(document).Count(d => d.Severity == PzDiagnosticSeverity.Error);
            return errors > 0
                ? Fact(DiagnosticDomain.Config, ProbeStatus.Fail, $"The server configuration has {errors} validation error(s).")
                : Fact(DiagnosticDomain.Config, ProbeStatus.Pass, "The server configuration parses and is schema-valid.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Fact(DiagnosticDomain.Config, ProbeStatus.Warn, "The server configuration could not be checked.", ex.Message);
        }
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
