using Microsoft.Extensions.Options;
using ZWarden.Agent.Configuration;
using ZWarden.Agent.Docker;

namespace ZWarden.Agent.Health;

/// <summary>
/// The default <see cref="IServerHealthObserver"/> (F16). For each owned container it turns the inspect facts and
/// a best-effort network probe into a <see cref="ServerObservation"/> via the pure
/// <see cref="ServerHealthEvaluator"/>. The network probe runs only on a running container, and only against the
/// published <b>game</b> UDP port (the direct/query port strides off it, F13); a Server with no discoverable game
/// port, or one that is not running, is evaluated with the network probe skipped.
/// </summary>
public sealed class ServerHealthObserver : IServerHealthObserver
{
    private readonly IContainerRuntime _runtime;
    private readonly INetworkReachabilityProbe _network;
    private readonly string _networkName;

    public ServerHealthObserver(IContainerRuntime runtime, INetworkReachabilityProbe network, IOptions<AgentOptions> options)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(network);
        ArgumentNullException.ThrowIfNull(options);
        _runtime = runtime;
        _network = network;
        _networkName = options.Value.NetworkName;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ServerObservation>> ObserveAllAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<ObservedContainer> observed = await _runtime.InspectManagedAsync(cancellationToken)
            .ConfigureAwait(false);

        List<ServerObservation> result = [];
        foreach (ObservedContainer container in observed)
        {
            bool running = string.Equals(container.Facts.State, "running", StringComparison.OrdinalIgnoreCase);
            bool? portsReachable = running
                ? await ProbeGamePortAsync(container.Facts, cancellationToken).ConfigureAwait(false)
                : null;

            HealthEvaluation evaluation = ServerHealthEvaluator.Evaluate(new HealthProbeFacts(
                container.Facts.State,
                container.Facts.HealthStatus,
                container.Facts.ExitCode,
                container.Facts.OomKilled,
                portsReachable));

            result.Add(new ServerObservation(
                container.ServerId, evaluation.RunState, evaluation.Health, evaluation.Breakdown, evaluation.Reason));
        }

        return result;
    }

    private async Task<bool?> ProbeGamePortAsync(ContainerHealthFacts facts, CancellationToken cancellationToken)
    {
        // #199: probe the game port at the container's own IP on the shared ZWarden network, not the Agent's
        // loopback. When the Agent is containerized (the reference compose distribution) the published host port is
        // not on the Agent's loopback, so a loopback probe always read "unreachable" and pinned a running server to
        // Degraded. The port is the container-internal game port; the container listens on it regardless of the
        // host-side stride. No address on the network (not running / not attached) → skip (null → not degraded).
        if (facts.NetworkAddresses is not { } addresses
            || !addresses.TryGetValue(_networkName, out string? address)
            || string.IsNullOrEmpty(address))
        {
            return null;
        }

        return await _network.IsUdpPortReachableAsync(address, PortStrideAllocator.BaseGamePort, cancellationToken)
            .ConfigureAwait(false);
    }
}
