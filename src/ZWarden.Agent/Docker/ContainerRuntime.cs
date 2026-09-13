using System.Net;
using System.Net.Http;
using Docker.DotNet;
using Docker.DotNet.Models;
using Microsoft.Extensions.Logging;
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
    private readonly ILogger<ContainerRuntime> _logger;

    /// <summary>Creates the runtime over an engine, the ownership guard and the create-template factory.</summary>
    public ContainerRuntime(
        IDockerEngine engine,
        ContainerOwnershipGuard guard,
        PzContainerFactory factory,
        ILogger<ContainerRuntime> logger)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(guard);
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(logger);
        _engine = engine;
        _guard = guard;
        _factory = factory;
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
        await RunVerbAsync("stop", containerId, () => _engine.StopAsync(containerId, cancellationToken)).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task RestartAsync(string containerId, CancellationToken cancellationToken)
    {
        await EnsureOwnedAsync(containerId, cancellationToken).ConfigureAwait(false);
        await RunVerbAsync("restart", containerId, () => _engine.RestartAsync(containerId, cancellationToken)).ConfigureAwait(false);
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

    [LoggerMessage(Level = LogLevel.Error, Message = "The Docker verb '{Verb}' on {ContainerId} was denied (HTTP {Status}).")]
    private partial void LogVerbDenied(string verb, string containerId, int status);

    [LoggerMessage(Level = LogLevel.Error, Message = "Creating a container for {ServerId} failed ({Failure}, HTTP {Status}).")]
    private partial void LogCreateFailed(string failure, ServerId serverId, int status);
}
