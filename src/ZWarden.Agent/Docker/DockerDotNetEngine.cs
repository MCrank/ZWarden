using Docker.DotNet;
using Docker.DotNet.Models;

namespace ZWarden.Agent.Docker;

/// <summary>
/// The real <see cref="IDockerEngine"/> over a <see cref="IDockerClient"/> from Docker.DotNet.Enhanced. It is
/// deliberately mechanical — it maps each allowlisted verb to one client call and projects responses into the
/// undecorated <see cref="EngineContainer"/> — so all ZWarden policy stays in <see cref="ContainerRuntime"/>.
/// The client is constructed without an API-version override, so it negotiates via <c>/_ping</c> and never
/// pins a <c>/v1.xx</c> prefix (ADR 0008). This type is exercised in the integration tier, not offline.
/// </summary>
public sealed class DockerDotNetEngine : IDockerEngine
{
    private static readonly IReadOnlyDictionary<string, string> NoLabels =
        new Dictionary<string, string>(StringComparer.Ordinal);

    private readonly IDockerClient _client;

    /// <summary>Creates the engine over an already-configured Docker client.</summary>
    public DockerDotNetEngine(IDockerClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        _client = client;
    }

    /// <inheritdoc />
    public async Task<string> PingApiVersionAsync(CancellationToken cancellationToken)
    {
        VersionResponse version = await _client.System.GetVersionAsync(cancellationToken).ConfigureAwait(false);
        return version.APIVersion;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<EngineContainer>> ListAsync(CancellationToken cancellationToken)
    {
        IList<ContainerListResponse> containers = await _client.Containers
            .ListContainersAsync(new ContainersListParameters { All = true }, cancellationToken)
            .ConfigureAwait(false);

        List<EngineContainer> mapped = [];
        foreach (ContainerListResponse c in containers)
        {
            List<PublishedPort> ports = [];
            if (c.Ports is not null)
            {
                foreach (PortSummary p in c.Ports)
                {
                    ports.Add(new PublishedPort(p.PublicPort.GetValueOrDefault(), p.PrivatePort, p.Type ?? string.Empty));
                }
            }

            mapped.Add(new EngineContainer(c.ID, ToReadOnly(c.Labels), c.State ?? string.Empty, ports));
        }

        return mapped;
    }

    /// <inheritdoc />
    public async Task<EngineContainer> InspectAsync(string containerId, CancellationToken cancellationToken)
    {
        ContainerInspectResponse r = await _client.Containers
            .InspectContainerAsync(containerId, cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyDictionary<string, string> labels = ToReadOnly(r.Config?.Labels);
        string state = r.State?.Status ?? string.Empty;
        return new EngineContainer(r.ID, labels, state, []);
    }

    private static IReadOnlyDictionary<string, string> ToReadOnly(IDictionary<string, string>? labels) =>
        labels is null ? NoLabels : new Dictionary<string, string>(labels, StringComparer.Ordinal);

    /// <inheritdoc />
    public async Task<string> CreateAsync(CreateContainerParameters parameters, CancellationToken cancellationToken)
    {
        CreateContainerResponse response = await _client.Containers
            .CreateContainerAsync(parameters, cancellationToken)
            .ConfigureAwait(false);
        return response.ID;
    }

    /// <inheritdoc />
    public Task StartAsync(string containerId, CancellationToken cancellationToken) =>
        _client.Containers.StartContainerAsync(containerId, new ContainerStartParameters(), cancellationToken);

    /// <inheritdoc />
    public Task StopAsync(string containerId, int waitBeforeKillSeconds, CancellationToken cancellationToken) =>
        _client.Containers.StopContainerAsync(
            containerId,
            new ContainerStopParameters { WaitBeforeKillSeconds = (uint)waitBeforeKillSeconds },
            cancellationToken);

    /// <inheritdoc />
    public Task RestartAsync(string containerId, int waitBeforeKillSeconds, CancellationToken cancellationToken) =>
        _client.Containers.RestartContainerAsync(
            containerId,
            new ContainerRestartParameters { WaitBeforeKillSeconds = (uint)waitBeforeKillSeconds },
            cancellationToken);
}
