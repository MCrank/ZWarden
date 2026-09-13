using System.Net;
using System.Net.Http;
using Docker.DotNet;
using Docker.DotNet.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ZWarden.Agent.Configuration;
using ZWarden.Domain.Ids;

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
                        inspected.Ports)));
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
        IReadOnlyList<EngineContainer> all = await _engine.ListAsync(cancellationToken).ConfigureAwait(false);
        List<PortAllocation> inUse = [];
        foreach (EngineContainer container in all)
        {
            if (!_guard.TryResolveOwned(container.Labels, out _))
            {
                continue;
            }

            foreach (PublishedPort port in container.Ports)
            {
                if (port.ContainerPort == PortStrideAllocator.BaseGamePort
                    && string.Equals(port.Protocol, "udp", StringComparison.OrdinalIgnoreCase))
                {
                    inUse.Add(new PortAllocation(port.HostPort, 0));
                }
            }
        }

        return PortStrideAllocator.AllocateNext(inUse);
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
    public async Task<string> ReadServerLogsAsync(ServerId serverId, DateTimeOffset? since, CancellationToken cancellationToken)
    {
        // Reading logs is a read verb, not a mutation, so it does not pass the ownership guard's mutate check —
        // but discovery already scopes to containers this Agent owns, so an unmatched ServerId is nothing to read.
        IReadOnlyList<ManagedContainer> managed = await ListManagedAsync(cancellationToken).ConfigureAwait(false);
        foreach (ManagedContainer container in managed)
        {
            if (container.ServerId == serverId)
            {
                return await _engine.ReadLogsAsync(container.DockerId, since, cancellationToken).ConfigureAwait(false);
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

    [LoggerMessage(Level = LogLevel.Error, Message = "The Docker verb '{Verb}' on {ContainerId} was denied (HTTP {Status}).")]
    private partial void LogVerbDenied(string verb, string containerId, int status);

    [LoggerMessage(Level = LogLevel.Error, Message = "Creating a container for {ServerId} failed ({Failure}, HTTP {Status}).")]
    private partial void LogCreateFailed(string failure, ServerId serverId, int status);
}
