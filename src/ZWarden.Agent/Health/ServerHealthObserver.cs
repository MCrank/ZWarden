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

    public ServerHealthObserver(IContainerRuntime runtime, INetworkReachabilityProbe network)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(network);
        _runtime = runtime;
        _network = network;
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
        foreach (PublishedPort port in facts.Ports)
        {
            if (port.ContainerPort == PortStrideAllocator.BaseGamePort
                && string.Equals(port.Protocol, "udp", StringComparison.OrdinalIgnoreCase)
                && port.HostPort != 0)
            {
                return await _network.IsUdpPortReachableAsync(port.HostPort, cancellationToken).ConfigureAwait(false);
            }
        }

        return null;
    }
}
