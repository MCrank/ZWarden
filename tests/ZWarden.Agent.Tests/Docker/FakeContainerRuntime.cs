using ZWarden.Agent.Docker;
using ZWarden.Domain.Ids;

namespace ZWarden.Agent.Tests.Docker;

/// <summary>
/// A configurable <see cref="IContainerRuntime"/> test double for the command-processor tests: it records the
/// provisioning calls (allocate → create → start) and the ServerId-addressed lifecycle verbs (F15), and can be
/// primed to fail the create or a lifecycle verb. The container-id list/inspect verbs the processor never
/// calls still throw, so an unexpected call is loud rather than silent.
/// </summary>
internal sealed class FakeContainerRuntime : IContainerRuntime
{
    /// <summary>When set, the ServerId-addressed lifecycle verbs throw it (e.g. <see cref="ContainerNotFoundException"/>).</summary>
    public Exception? LifecycleException { get; set; }

    public ServerId? StartedServerId { get; private set; }

    public ServerId? StoppedServerId { get; private set; }

    public ServerId? RestartedServerId { get; private set; }

    public int StartServerCount { get; private set; }

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

    public Task<IReadOnlyList<ObservedContainer>> InspectManagedAsync(CancellationToken cancellationToken) =>
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

    public Task StartAsync(ServerId serverId, CancellationToken cancellationToken)
    {
        if (LifecycleException is not null)
        {
            throw LifecycleException;
        }

        StartServerCount++;
        StartedServerId = serverId;
        return Task.CompletedTask;
    }

    public Task StopAsync(ServerId serverId, CancellationToken cancellationToken)
    {
        if (LifecycleException is not null)
        {
            throw LifecycleException;
        }

        StoppedServerId = serverId;
        return Task.CompletedTask;
    }

    public Task RestartAsync(ServerId serverId, CancellationToken cancellationToken)
    {
        if (LifecycleException is not null)
        {
            throw LifecycleException;
        }

        RestartedServerId = serverId;
        return Task.CompletedTask;
    }

    // The command processor delegates a SteamCMD update to IServerUpdateRunner, so it never reads logs directly;
    // a call here means a test wired it wrong, so be loud.
    public Task<string> ReadServerLogsAsync(ServerId serverId, DateTimeOffset? since, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
}
