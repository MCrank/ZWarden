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

    // What no host-side check can prove (#231): the path from the internet to the published port.
    private const string InternetCaveat =
        "Reachability from the internet (router port-forward, NAT, firewall) cannot be verified from the host.";

    private readonly IRconHealthProbe _rcon;
    private readonly IContainerRuntime _runtime;
    private readonly ISteamQueryProbe _steamQuery;
    private readonly IServerDiskUsageReader _disk;
    private readonly IServerInstallPaths _installPaths;
    private readonly IModDiscovery _mods;
    private readonly IPzConfigParser _configParser;
    private readonly IPzConfigValidator _configValidator;
    private readonly AgentOptions _options;

    public ServerDiagnosticsGatherer(
        IRconHealthProbe rcon,
        IContainerRuntime runtime,
        ISteamQueryProbe steamQuery,
        IServerDiskUsageReader disk,
        IServerInstallPaths installPaths,
        IModDiscovery mods,
        IPzConfigParser configParser,
        IPzConfigValidator configValidator,
        IOptions<AgentOptions> options)
    {
        ArgumentNullException.ThrowIfNull(rcon);
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(steamQuery);
        ArgumentNullException.ThrowIfNull(disk);
        ArgumentNullException.ThrowIfNull(installPaths);
        ArgumentNullException.ThrowIfNull(mods);
        ArgumentNullException.ThrowIfNull(configParser);
        ArgumentNullException.ThrowIfNull(configValidator);
        ArgumentNullException.ThrowIfNull(options);
        _rcon = rcon;
        _runtime = runtime;
        _steamQuery = steamQuery;
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

            // #231: two facts instead of one inconclusive probe. A bare UDP probe can only ever prove a port closed,
            // so it was Warn on every healthy server. First: is the game port published on the host, and where?
            int gamePort = PortStrideAllocator.BaseGamePort;
            if (!string.Equals(container.Facts.State, "running", StringComparison.OrdinalIgnoreCase))
            {
                return Fact(DiagnosticDomain.GamePort, ProbeStatus.Fail,
                    $"The server's container is {container.Facts.State}, so its game port is closed.");
            }

            PublishedPort? published = container.Facts.Ports
                .Where(p => p.ContainerPort == gamePort && string.Equals(p.Protocol, "udp", StringComparison.OrdinalIgnoreCase))
                .Select(p => (PublishedPort?)p)
                .FirstOrDefault();
            if (published is not { } binding)
            {
                return Fact(DiagnosticDomain.GamePort, ProbeStatus.Fail,
                    $"The game port {gamePort}/udp is not published on the host, so players cannot connect.");
            }

            string where = $"Published on host port {binding.HostPort}/udp (container {gamePort})";

            // Second: is PZ listening? Ask it over the ZWarden network (#199: the container's own IP, not the Agent's
            // loopback) with a Steam A2S_INFO query, which B42 answers on the game port.
            if (container.Facts.NetworkAddresses is not { } addresses
                || !addresses.TryGetValue(_options.NetworkName, out string? address)
                || string.IsNullOrEmpty(address))
            {
                return Fact(DiagnosticDomain.GamePort, ProbeStatus.Warn,
                    $"{where}; whether the server is listening could not be checked (no address on the ZWarden network).",
                    InternetCaveat);
            }

            SteamQueryResult query = await _steamQuery.QueryInfoAsync(address, gamePort, cancellationToken).ConfigureAwait(false);
            return query switch
            {
                { Status: SteamQueryStatus.Answered, Info: { } info } => Fact(DiagnosticDomain.GamePort, ProbeStatus.Pass,
                    $"{where} and answering Steam queries as \"{info.Name}\" ({info.Map}, {info.Players}/{info.MaxPlayers} players).",
                    InternetCaveat),
                { Status: SteamQueryStatus.Refused } => Fact(DiagnosticDomain.GamePort, ProbeStatus.Fail,
                    $"{where}, but nothing is listening on it inside the container.", InternetCaveat),
                _ => Fact(DiagnosticDomain.GamePort, ProbeStatus.Pass,
                    $"{where}. The server did not answer a Steam query but did not refuse the port either "
                    + "(it may still be starting, or run without Steam).",
                    InternetCaveat),
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
