using ZWarden.Agent.Docker;

namespace ZWarden.Agent.Tests.Docker;

/// <summary>
/// A configurable <see cref="IContainerRuntime"/> test double for the command-processor tests: it records the
/// provisioning calls (allocate → create → start) and can be primed to fail the create. The list/stop/restart
/// verbs the processor never calls still throw, so an unexpected call is loud rather than silent.
/// </summary>
internal sealed class FakeContainerRuntime : IContainerRuntime
{
    public DockerHealth Health { get; set; } = new(DaemonReachable: true, ApiVersion: "1.53", Detail: null);

    public int ProbeCount { get; private set; }

    /// <summary>The ports <see cref="AllocateNextPortsAsync"/> hands out.</summary>
    public PortAllocation NextPorts { get; set; } = new(16261, 16262);

    /// <summary>The container id <see cref="CreateAsync"/> returns on success.</summary>
    public string CreatedContainerId { get; set; } = "container-abc";

    /// <summary>When set, <see cref="CreateAsync"/> throws it instead of succeeding.</summary>
    public ContainerCreateException? CreateException { get; set; }

    public int CreateCount { get; private set; }

    public PzContainerSpec? LastSpec { get; private set; }

    public string? StartedContainerId { get; private set; }

    public Task<DockerHealth> ProbeHealthAsync(CancellationToken cancellationToken)
    {
        ProbeCount++;
        return Task.FromResult(Health);
    }

    public Task<IReadOnlyList<ManagedContainer>> ListManagedAsync(CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<PortAllocation> AllocateNextPortsAsync(CancellationToken cancellationToken) =>
        Task.FromResult(NextPorts);

    public Task<string> CreateAsync(PzContainerSpec spec, CancellationToken cancellationToken)
    {
        CreateCount++;
        LastSpec = spec;
        if (CreateException is not null)
        {
            throw CreateException;
        }

        return Task.FromResult(CreatedContainerId);
    }

    public Task StartAsync(string containerId, CancellationToken cancellationToken)
    {
        StartedContainerId = containerId;
        return Task.CompletedTask;
    }

    public Task StopAsync(string containerId, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task RestartAsync(string containerId, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
}
