using ZWarden.Agent.Docker;
using ZWarden.Domain.Ids;

namespace ZWarden.Agent.Tests.Backups;

/// <summary>A container runtime that only answers <see cref="IContainerRuntime.ListManagedAsync"/> with a preset list —
/// enough for the backup and restore runners, which only ask whether a Server's container is running.</summary>
internal sealed class StubContainerRuntime(params ManagedContainer[] managed) : IContainerRuntime
{
    public Task<IReadOnlyList<ManagedContainer>> ListManagedAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ManagedContainer>>(managed);

    public Task<DockerHealth> ProbeHealthAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<HostMemory> ReadHostMemoryAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<IReadOnlyList<ObservedContainer>> InspectManagedAsync(CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<PortAllocation> ClaimRequestedPortsAsync(int gamePort, ServerId forServer, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<ServerContainer?> InspectServerAsync(ServerId serverId, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task RemoveAsync(ServerId serverId, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<PortAllocation> AllocateNextPortsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<string> CreateAsync(PzContainerSpec spec, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task StartAsync(string containerId, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task StopAsync(string containerId, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task RestartAsync(string containerId, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task StartAsync(ServerId serverId, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task StopAsync(ServerId serverId, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task RestartAsync(ServerId serverId, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<string> ReadServerLogsAsync(ServerId serverId, DateTimeOffset? since, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<string?> ResolveNetworkAddressAsync(ServerId serverId, string networkName, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task FollowServerLogsAsync(ServerId serverId, int tailLines, Func<ContainerLogFrame, CancellationToken, ValueTask> onFrame, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
}
