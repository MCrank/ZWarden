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

    /// <summary>When set, <see cref="ProbeHealthAsync"/> throws it instead of returning <see cref="Health"/>.</summary>
    public Exception? ProbeException { get; set; }

    public Task<DockerHealth> ProbeHealthAsync(CancellationToken cancellationToken)
    {
        ProbeCount++;
        if (ProbeException is not null)
        {
            throw ProbeException;
        }

        return Task.FromResult(Health);
    }

    public Task<IReadOnlyList<ManagedContainer>> ListManagedAsync(CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    /// <summary>The containers <see cref="InspectManagedAsync"/> returns (empty by default).</summary>
    public IReadOnlyList<ObservedContainer> Observed { get; set; } = [];

    public Task<IReadOnlyList<ObservedContainer>> InspectManagedAsync(CancellationToken cancellationToken) =>
        Task.FromResult(Observed);

    /// <summary>The ordered provisioning/recreate verbs this fake saw (#229), for sequencing assertions: <c>allocate</c>,
    /// <c>claim:&lt;port&gt;</c>, <c>inspect</c>, <c>create:&lt;game&gt;</c>, <c>start:&lt;id&gt;</c>, <c>stop</c>, <c>remove</c>.</summary>
    public List<string> Calls { get; } = [];

    /// <summary>Every spec <see cref="CreateAsync"/> was asked to build, in order.</summary>
    public List<PzContainerSpec> CreatedSpecs { get; } = [];

    /// <summary>When set, decides per spec whether <see cref="CreateAsync"/> throws (return the exception) or succeeds.</summary>
    public Func<PzContainerSpec, Exception?>? CreateFailure { get; set; }

    /// <summary>When set, decides per container id whether <see cref="StartAsync(string, CancellationToken)"/> throws.</summary>
    public Func<string, Exception?>? StartFailure { get; set; }

    public int RemoveCount { get; private set; }

    public Task<PortAllocation> AllocateNextPortsAsync(CancellationToken cancellationToken)
    {
        Calls.Add("allocate");
        return Task.FromResult(NextPorts);
    }

    /// <summary>When set, <see cref="ClaimRequestedPortsAsync"/> throws it (e.g. <see cref="PortUnavailableException"/>).</summary>
    public Exception? ClaimException { get; set; }

    public int? ClaimedGamePort { get; private set; }

    public Task<PortAllocation> ClaimRequestedPortsAsync(int gamePort, ServerId forServer, CancellationToken cancellationToken)
    {
        ClaimedGamePort = gamePort;
        Calls.Add($"claim:{gamePort}");
        return ClaimException is not null
            ? Task.FromException<PortAllocation>(ClaimException)
            : Task.FromResult(PortStrideAllocator.ForGamePort((ushort)gamePort));
    }

    /// <summary>What <see cref="InspectServerAsync"/> returns (<c>null</c> = no owned container).</summary>
    public ServerContainer? ServerContainer { get; set; }

    public Task<ServerContainer?> InspectServerAsync(ServerId serverId, CancellationToken cancellationToken)
    {
        Calls.Add("inspect");
        return Task.FromResult(ServerContainer);
    }

    /// <summary>When set, <see cref="RemoveAsync(ServerId, CancellationToken)"/> throws it.</summary>
    public Exception? RemoveException { get; set; }

    public ServerId? RemovedServerId { get; private set; }

    public Task RemoveAsync(ServerId serverId, CancellationToken cancellationToken)
    {
        Calls.Add("remove");
        if (RemoveException is not null)
        {
            throw RemoveException;
        }

        RemoveCount++;
        RemovedServerId = serverId;
        return Task.CompletedTask;
    }

    public Task<string> CreateAsync(PzContainerSpec spec, CancellationToken cancellationToken)
    {
        CreateCount++;
        LastSpec = spec;
        CreatedSpecs.Add(spec);
        Calls.Add($"create:{spec.Ports.GamePort}");
        if (CreateException is not null)
        {
            throw CreateException;
        }

        if (CreateFailure?.Invoke(spec) is { } failure)
        {
            throw failure;
        }

        return Task.FromResult(CreatedContainerId);
    }

    public Task StartAsync(string containerId, CancellationToken cancellationToken)
    {
        Calls.Add($"start:{containerId}");
        if (StartFailure?.Invoke(containerId) is { } failure)
        {
            throw failure;
        }

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
        Calls.Add("stop");
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

    /// <summary>Frames <see cref="FollowServerLogsAsync"/> replays through its callback before (optionally) blocking.</summary>
    public List<ContainerLogFrame> FollowFrames { get; } = [];

    /// <summary>When set, <see cref="FollowServerLogsAsync"/> blocks after replaying frames until the token cancels
    /// — modelling a live, long-lived follow so a subscription's teardown can be exercised.</summary>
    public bool FollowBlocksUntilCancelled { get; set; }

    /// <summary>When set, <see cref="FollowServerLogsAsync"/> throws it (e.g. <see cref="ContainerNotFoundException"/>).</summary>
    public Exception? FollowException { get; set; }

    /// <summary>The Server the last <see cref="FollowServerLogsAsync"/> was asked to follow.</summary>
    public ServerId? FollowedServerId { get; private set; }

    /// <summary>The <c>tailLines</c> the last <see cref="FollowServerLogsAsync"/> was asked for.</summary>
    public int? FollowTailLines { get; private set; }

    private int _followCount;

    /// <summary>How many times <see cref="FollowServerLogsAsync"/> has been invoked (dedupe assertions).</summary>
    public int FollowCount => Volatile.Read(ref _followCount);

    public async Task FollowServerLogsAsync(
        ServerId serverId,
        int tailLines,
        Func<ContainerLogFrame, CancellationToken, ValueTask> onFrame,
        CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _followCount);
        FollowedServerId = serverId;
        FollowTailLines = tailLines;
        if (FollowException is not null)
        {
            throw FollowException;
        }

        foreach (ContainerLogFrame frame in FollowFrames)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await onFrame(frame, cancellationToken).ConfigureAwait(false);
        }

        if (FollowBlocksUntilCancelled)
        {
            try
            {
                await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // The subscription was torn down — the expected end of a live follow.
            }
        }
    }

    /// <summary>The address <see cref="ResolveNetworkAddressAsync"/> returns; <c>null</c> models no owned
    /// container or no address on the network.</summary>
    public string? NetworkAddress { get; set; }

    public string? LastResolvedNetworkName { get; private set; }

    public Task<string?> ResolveNetworkAddressAsync(ServerId serverId, string networkName, CancellationToken cancellationToken)
    {
        LastResolvedNetworkName = networkName;
        return Task.FromResult(NetworkAddress);
    }
}
