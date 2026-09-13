using ZWarden.Agent.Docker;

namespace ZWarden.Agent.Tests.Docker;

/// <summary>
/// A configurable <see cref="IContainerRuntime"/> test double. Only <see cref="ProbeHealthAsync"/> is used by
/// the command-processor tests; the rest throw, so an unexpected call in a test is loud rather than silent.
/// </summary>
internal sealed class FakeContainerRuntime : IContainerRuntime
{
    public DockerHealth Health { get; set; } = new(DaemonReachable: true, ApiVersion: "1.53", Detail: null);

    public int ProbeCount { get; private set; }

    public Task<DockerHealth> ProbeHealthAsync(CancellationToken cancellationToken)
    {
        ProbeCount++;
        return Task.FromResult(Health);
    }

    public Task<IReadOnlyList<ManagedContainer>> ListManagedAsync(CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<PortAllocation> AllocateNextPortsAsync(CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<string> CreateAsync(PzContainerSpec spec, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task StartAsync(string containerId, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task StopAsync(string containerId, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task RestartAsync(string containerId, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
}
