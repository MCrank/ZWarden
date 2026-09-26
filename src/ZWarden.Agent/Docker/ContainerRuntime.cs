using System.Net;
using System.Net.Http;
using Docker.DotNet;
using Docker.DotNet.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ZWarden.Agent.Configuration;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Servers;

namespace ZWarden.Agent.Docker;

/// <summary>
/// The default <see cref="IContainerRuntime"/> (F13). It layers ZWarden's policy — canonical recognition,
/// ownership enforcement and the closed create template — over the mechanical <see cref="IDockerEngine"/>, so
/// this is where every trust decision lives and where the tests exercise it against a fake engine. It is
/// correct with the socket unproxied (ADR 0008): the enforcement here is the real control, not the proxy.
/// </summary>
public sealed partial class ContainerRuntime : IContainerRuntime
{
    private readonly IDockerEngine _engine;
    private readonly ContainerOwnershipGuard _guard;
    private readonly PzContainerFactory _factory;
    private readonly int _stopTimeoutSeconds;
    private readonly ILogger<ContainerRuntime> _logger;

    /// <summary>Creates the runtime over an engine, the ownership guard, the create-template factory and the
    /// Agent options (for the safe stop timeout, F15).</summary>
    public ContainerRuntime(
        IDockerEngine engine,
        ContainerOwnershipGuard guard,
        PzContainerFactory factory,
        IOptions<AgentOptions> options,
        ILogger<ContainerRuntime> logger)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(guard);
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _engine = engine;
        _guard = guard;
        _factory = factory;
        _stopTimeoutSeconds = options.Value.StopTimeoutSeconds;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<DockerHealth> ProbeHealthAsync(CancellationToken cancellationToken)
    {
        try
        {
            string apiVersion = await _engine.PingApiVersionAsync(cancellationToken).ConfigureAwait(false);
            return new DockerHealth(DaemonReachable: true, apiVersion, Detail: null);
        }
        catch (Exception ex) when (ex is DockerApiException or HttpRequestException or TimeoutException or IOException)
        {
            LogDaemonUnreachable(ex.Message);
            return new DockerHealth(DaemonReachable: false, ApiVersion: null, Detail: "The Docker daemon is not reachable.");
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ManagedContainer>> ListManagedAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<EngineContainer> all = await _engine.ListAsync(cancellationToken).ConfigureAwait(false);
        List<ManagedContainer> managed = [];
        foreach (EngineContainer container in all)
        {
            if (_guard.TryResolveOwned(container.Labels, out ServerId serverId))
            {
                managed.Add(new ManagedContainer(container.Id, serverId, container.State));
            }
        }

        return managed;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ObservedContainer>> InspectManagedAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<ManagedContainer> managed = await ListManagedAsync(cancellationToken).ConfigureAwait(false);
        List<ObservedContainer> observed = [];
        foreach (ManagedContainer container in managed)
        {
            try
            {
                EngineContainer inspected = await _engine.InspectAsync(container.DockerId, cancellationToken)
                    .ConfigureAwait(false);
                observed.Add(new ObservedContainer(
                    container.ServerId,
                    new ContainerHealthFacts(
                        inspected.State,
                        inspected.HealthStatus,
                        inspected.ExitCode,
                        inspected.OomKilled,
                        inspected.Ports,
                        inspected.NetworkAddresses)));
            }
            catch (DockerApiException ex)
            {
                // The container vanished (or the daemon refused) between the list and this inspect — skip it; the
                // next sweep re-observes. One missing container must not fail health reporting for the rest.
                LogInspectSkipped(container.ServerId, ex.Message);
            }
        }

        return observed;
    }

    /// <inheritdoc />
    public async Task<PortAllocation> AllocateNextPortsAsync(CancellationToken cancellationToken)
    {
        HashSet<ushort> occupied = await OccupiedUdpHostPortsAsync(excluding: null, cancellationToken).ConfigureAwait(false);
        return PortStrideAllocator.AllocateNext(occupied);
    }

    /// <inheritdoc />
    public async Task<PortAllocation> ClaimRequestedPortsAsync(int gamePort, ServerId forServer, CancellationToken cancellationToken)
    {
        if (HostPortRules.ValidateGamePort(gamePort) is { } invalid)
        {
            throw new PortUnavailableException(invalid);
        }

        PortAllocation pair = PortStrideAllocator.ForGamePort((ushort)gamePort);
        HashSet<ushort> occupied = await OccupiedUdpHostPortsAsync(forServer, cancellationToken).ConfigureAwait(false);
        foreach (ushort port in (ReadOnlySpan<ushort>)[pair.GamePort, pair.DirectPort])
        {
            if (occupied.Contains(port))
            {
                throw new PortUnavailableException(
                    $"Host port {port}/udp is already published by another container on this host. Choose a different game port.");
            }
        }

        return pair;
    }

    // Every UDP host port published — or reserved by a stopped container's creation-time bindings — by ANY container
    // on the daemon, not just the ones this Agent owns (#229): a foreign container or another Agent's server holds a
    // port just as firmly. The list API only reports a running container's ports, so non-running ones are inspected
    // for their HostConfig.PortBindings. The Server named by `excluding` (a Recreate's own, about-to-be-removed
    // container) is left out.
    private async Task<HashSet<ushort>> OccupiedUdpHostPortsAsync(ServerId? excluding, CancellationToken cancellationToken)
    {
        IReadOnlyList<EngineContainer> all = await _engine.ListAsync(cancellationToken).ConfigureAwait(false);
        HashSet<ushort> occupied = [];
        foreach (EngineContainer container in all)
        {
            if (excluding is { } self && _guard.TryResolveOwned(container.Labels, out ServerId owner) && owner == self)
            {
                continue;
            }

            AddUdpHostPorts(occupied, container.Ports);
            if (string.Equals(container.State, "running", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                EngineContainer inspected = await _engine.InspectAsync(container.Id, cancellationToken).ConfigureAwait(false);
                AddUdpHostPorts(occupied, inspected.ConfiguredPorts ?? []);
            }
            catch (DockerApiException ex)
            {
                // Vanished between the list and the inspect — nothing left to hold a port.
                LogInspectSkippedContainer(container.Id, ex.Message);
            }
        }

        return occupied;
    }

    private static void AddUdpHostPorts(HashSet<ushort> occupied, IReadOnlyList<PublishedPort> ports)
    {
        foreach (PublishedPort port in ports)
        {
            if (port.HostPort != 0 && string.Equals(port.Protocol, "udp", StringComparison.OrdinalIgnoreCase))
            {
                occupied.Add(port.HostPort);
            }
        }
    }

    /// <inheritdoc />
    public async Task<string> CreateAsync(PzContainerSpec spec, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(spec);
        CreateContainerParameters parameters = _factory.Build(spec);

        try
        {
            return await _engine.CreateAsync(parameters, cancellationToken).ConfigureAwait(false);
        }
        catch (DockerApiException ex)
        {
            ContainerCreateFailure failure = DockerFailureInterpreter.InterpretCreate(ex.StatusCode, ex.ResponseBody);
            LogCreateFailed(failure.ToString(), spec.ServerId, (int)ex.StatusCode);
            throw new ContainerCreateException(failure, DescribeCreateFailure(failure, spec, ex.StatusCode), ex);
        }
    }

    /// <inheritdoc />
    public async Task StartAsync(string containerId, CancellationToken cancellationToken)
    {
        await EnsureOwnedAsync(containerId, cancellationToken).ConfigureAwait(false);
        await RunVerbAsync("start", containerId, () => _engine.StartAsync(containerId, cancellationToken)).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task StopAsync(string containerId, CancellationToken cancellationToken)
    {
        await EnsureOwnedAsync(containerId, cancellationToken).ConfigureAwait(false);
        // A stop timeout above the image's save grace, so the entrypoint's SIGTERM handler completes the FIFO
        // save→quit before Docker SIGKILLs the container (F15) — never a bare SIGTERM to the JVM.
        await RunVerbAsync("stop", containerId, () => _engine.StopAsync(containerId, _stopTimeoutSeconds, cancellationToken)).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task RestartAsync(string containerId, CancellationToken cancellationToken)
    {
        await EnsureOwnedAsync(containerId, cancellationToken).ConfigureAwait(false);
        await RunVerbAsync("restart", containerId, () => _engine.RestartAsync(containerId, _stopTimeoutSeconds, cancellationToken)).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task StartAsync(ServerId serverId, CancellationToken cancellationToken)
        => ResolveThenAsync(serverId, StartAsync, cancellationToken);

    /// <inheritdoc />
    public Task StopAsync(ServerId serverId, CancellationToken cancellationToken)
        => ResolveThenAsync(serverId, StopAsync, cancellationToken);

    /// <inheritdoc />
    public Task RestartAsync(ServerId serverId, CancellationToken cancellationToken)
        => ResolveThenAsync(serverId, RestartAsync, cancellationToken);

    /// <inheritdoc />
    public async Task<ServerContainer?> InspectServerAsync(ServerId serverId, CancellationToken cancellationToken)
    {
        ManagedContainer? match = await FindOwnedAsync(serverId, cancellationToken).ConfigureAwait(false);
        if (match is null)
        {
            return null;
        }

        EngineContainer inspected = await _engine.InspectAsync(match.DockerId, cancellationToken).ConfigureAwait(false);
        return new ServerContainer(
            inspected.Id,
            inspected.State,
            PairOf(inspected.ConfiguredPorts ?? []),
            inspected.BindMounts ?? new Dictionary<string, string>(StringComparer.Ordinal),
            PzContainerFactory.ReadJvmHeap(inspected.Environment));
    }

    /// <inheritdoc />
    public async Task<HostMemory> ReadHostMemoryAsync(CancellationToken cancellationToken)
    {
        long total = await _engine.TotalMemoryBytesAsync(cancellationToken).ConfigureAwait(false);
        long committed = 0;
        foreach (ManagedContainer container in await ListManagedAsync(cancellationToken).ConfigureAwait(false))
        {
            try
            {
                EngineContainer inspected = await _engine.InspectAsync(container.DockerId, cancellationToken).ConfigureAwait(false);
                committed += Math.Max(0, inspected.MemoryLimitBytes ?? 0);
            }
            catch (DockerApiException ex)
            {
                // Vanished between the list and the inspect — it no longer holds memory; the next report re-reads.
                LogInspectSkipped(container.ServerId, ex.Message);
            }
        }

        return new HostMemory(total, committed);
    }

    /// <inheritdoc />
    public async Task RemoveAsync(ServerId serverId, CancellationToken cancellationToken)
    {
        ManagedContainer? match = await FindOwnedAsync(serverId, cancellationToken).ConfigureAwait(false);
        if (match is null)
        {
            LogNoContainerForServer(serverId);
            throw new ContainerNotFoundException(serverId);
        }

        // Re-assert ownership on the authoritative inspect, then require it stopped and named by its ServerId. The
        // delete is never forced, so Docker itself also refuses a running container; and it goes by NAME, the only
        // DELETE shape the socket proxy admits (ADR 0045).
        EngineContainer inspected = await _engine.InspectAsync(match.DockerId, cancellationToken).ConfigureAwait(false);
        try
        {
            _guard.EnsureOwnedByThisAgent(match.DockerId, inspected.Labels);
        }
        catch (ForeignContainerException ex)
        {
            LogForeignRefused(match.DockerId, ex.Reason);
            throw;
        }

        string name = serverId.ToString();
        if (inspected.State is "running" or "restarting" or "paused")
        {
            throw new InvalidOperationException($"The container for server {serverId} must be stopped before it is removed.");
        }

        if (!string.Equals(inspected.Name, name, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"The container for server {serverId} is not named by its ServerId ('{inspected.Name}'), so it cannot be removed safely.");
        }

        await RunVerbAsync("remove", name, () => _engine.RemoveAsync(name, cancellationToken)).ConfigureAwait(false);
    }

    // The host pair bound to the container-internal 16261/16262 udp, or null when either is missing.
    private static PortAllocation? PairOf(IReadOnlyList<PublishedPort> ports)
    {
        ushort? game = null;
        ushort? direct = null;
        foreach (PublishedPort port in ports)
        {
            if (!string.Equals(port.Protocol, "udp", StringComparison.OrdinalIgnoreCase) || port.HostPort == 0)
            {
                continue;
            }

            if (port.ContainerPort == PortStrideAllocator.BaseGamePort)
            {
                game = port.HostPort;
            }
            else if (port.ContainerPort == PortStrideAllocator.BaseDirectPort)
            {
                direct = port.HostPort;
            }
        }

        return game is { } g && direct is { } d ? new PortAllocation(g, d) : null;
    }

    private async Task<ManagedContainer?> FindOwnedAsync(ServerId serverId, CancellationToken cancellationToken)
    {
        IReadOnlyList<ManagedContainer> managed = await ListManagedAsync(cancellationToken).ConfigureAwait(false);
        foreach (ManagedContainer container in managed)
        {
            if (container.ServerId == serverId)
            {
                return container;
            }
        }

        return null;
    }

    /// <inheritdoc />
    public async Task<string> ReadServerLogsAsync(ServerId serverId, DateTimeOffset? since, CancellationToken cancellationToken)
    {
        // Reading logs is a read verb, not a mutation, so it does not pass the ownership guard's mutate check —
        // but discovery already scopes to containers this Agent owns, so an unmatched ServerId is nothing to read.
        IReadOnlyList<ManagedContainer> managed = await ListManagedAsync(cancellationToken).ConfigureAwait(false);
        foreach (ManagedContainer container in managed)
        {
            if (container.ServerId == serverId)
            {
                return await _engine.ReadLogsAsync(container.DockerId, since, until: null, cancellationToken).ConfigureAwait(false);
            }
        }

        LogNoContainerForServer(serverId);
        throw new ContainerNotFoundException(serverId);
    }

    /// <inheritdoc />
    public async Task FollowServerLogsAsync(
        ServerId serverId,
        int tailLines,
        Func<ContainerLogFrame, CancellationToken, ValueTask> onFrame,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(onFrame);

        // Following logs is a read verb, like ReadServerLogsAsync — it does not pass the mutate ownership guard,
        // but discovery already scopes to containers this Agent owns, so an unmatched ServerId has nothing to follow.
        IReadOnlyList<ManagedContainer> managed = await ListManagedAsync(cancellationToken).ConfigureAwait(false);
        foreach (ManagedContainer container in managed)
        {
            if (container.ServerId == serverId)
            {
                await _engine.FollowLogsAsync(container.DockerId, tailLines, onFrame, cancellationToken).ConfigureAwait(false);
                return;
            }
        }

        LogNoContainerForServer(serverId);
        throw new ContainerNotFoundException(serverId);
    }

    /// <inheritdoc />
    public async Task<string?> ResolveNetworkAddressAsync(
        ServerId serverId, string networkName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(networkName);

        // Discovery already scopes to owned containers, so an unmatched ServerId simply has no address to read.
        IReadOnlyList<ManagedContainer> managed = await ListManagedAsync(cancellationToken).ConfigureAwait(false);
        foreach (ManagedContainer container in managed)
        {
            if (container.ServerId != serverId)
            {
                continue;
            }

            EngineContainer inspected = await _engine.InspectAsync(container.DockerId, cancellationToken)
                .ConfigureAwait(false);
            if (inspected.NetworkAddresses is { } addresses
                && addresses.TryGetValue(networkName, out string? address)
                && !string.IsNullOrEmpty(address))
            {
                return address;
            }

            return null; // owned, but no address on that network (e.g. not running)
        }

        return null; // no owned container for this Server
    }

    // Resolve the canonical container this Agent owns for the Server, then run the container-id verb (which
    // re-asserts ownership before acting). Discovery already scopes to owned containers, so an unmatched
    // ServerId means there is nothing owned to act on — a ContainerNotFoundException, not a foreign refusal.
    private async Task ResolveThenAsync(
        ServerId serverId,
        Func<string, CancellationToken, Task> verb,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<ManagedContainer> managed = await ListManagedAsync(cancellationToken).ConfigureAwait(false);
        ManagedContainer? match = null;
        foreach (ManagedContainer container in managed)
        {
            if (container.ServerId == serverId)
            {
                match = container;
                break;
            }
        }

        if (match is null)
        {
            LogNoContainerForServer(serverId);
            throw new ContainerNotFoundException(serverId);
        }

        await verb(match.DockerId, cancellationToken).ConfigureAwait(false);
    }

    // Resolve the container, then run it past the ownership guard before any verb is issued. A refusal is
    // logged at the boundary and rethrown — a foreign container is never mutated (trust-boundaries.md §4).
    private async Task<ServerId> EnsureOwnedAsync(string containerId, CancellationToken cancellationToken)
    {
        EngineContainer container = await _engine.InspectAsync(containerId, cancellationToken).ConfigureAwait(false);
        try
        {
            return _guard.EnsureOwnedByThisAgent(containerId, container.Labels);
        }
        catch (ForeignContainerException ex)
        {
            LogForeignRefused(containerId, ex.Reason);
            throw;
        }
    }

    private async Task RunVerbAsync(string verb, string containerId, Func<Task> action)
    {
        try
        {
            await action().ConfigureAwait(false);
        }
        catch (DockerApiException ex) when (ex.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.MethodNotAllowed)
        {
            // A denied verb through the socket proxy (ADR 0008 §3.5): 403 = path miss, 405 = no method rule.
            LogVerbDenied(verb, containerId, (int)ex.StatusCode);
            throw;
        }
    }

    private static string DescribeCreateFailure(ContainerCreateFailure failure, PzContainerSpec spec, HttpStatusCode status) =>
        failure switch
        {
            ContainerCreateFailure.ImageNotProvisioned =>
                $"The canonical PZ image '{spec.ImageReference}' is not present on the host, and the socket allowlist forbids pulling it. "
                + "Pre-provision it (docker pull / compose pull) and retry.",
            _ => $"Creating the container failed: the Docker daemon returned HTTP {(int)status}.",
        };

    [LoggerMessage(Level = LogLevel.Warning, Message = "The Docker daemon is not reachable: {Detail}")]
    private partial void LogDaemonUnreachable(string detail);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Refused to operate on container {ContainerId}: {Reason}.")]
    private partial void LogForeignRefused(string containerId, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "No canonical container owned by this Agent for server {ServerId}.")]
    private partial void LogNoContainerForServer(ServerId serverId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Skipped health inspect for server {ServerId}: {Detail}")]
    private partial void LogInspectSkipped(ServerId serverId, string detail);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Skipped port inspect for container {ContainerId}: {Detail}")]
    private partial void LogInspectSkippedContainer(string containerId, string detail);

    [LoggerMessage(Level = LogLevel.Error, Message = "The Docker verb '{Verb}' on {ContainerId} was denied (HTTP {Status}).")]
    private partial void LogVerbDenied(string verb, string containerId, int status);

    [LoggerMessage(Level = LogLevel.Error, Message = "Creating a container for {ServerId} failed ({Failure}, HTTP {Status}).")]
    private partial void LogCreateFailed(string failure, ServerId serverId, int status);
}
