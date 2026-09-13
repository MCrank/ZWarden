using ZWarden.Contracts.Protocol;
using ZWarden.Contracts.Protocol.Messages;
using ZWarden.Domain.Ids;

namespace ZWarden.Agent.Health;

/// <summary>One Server's fully-rolled-up observation (F16): the wire run-state and health, the probe breakdown,
/// and the reason. Produced by <see cref="IServerHealthObserver"/> from Docker inspect + the network probe +
/// <see cref="ServerHealthEvaluator"/>.</summary>
/// <param name="ServerId">The Server observed.</param>
/// <param name="RunState">Its observed run-state.</param>
/// <param name="Health">Its rolled-up health.</param>
/// <param name="Breakdown">The four probe verdicts behind the rollup.</param>
/// <param name="Reason">A short, non-secret summary.</param>
public sealed record ServerObservation(
    ServerId ServerId,
    ServerRunState RunState,
    ServerHealth Health,
    HealthBreakdown Breakdown,
    string Reason);

/// <summary>
/// Observes the health of every Server this Agent owns (F16): it inspects each owned container, runs the network
/// probe on a running one, and rolls the facts up with <see cref="ServerHealthEvaluator"/>. It is the single
/// source of health truth on the Agent — the connection's post-connect snapshot and the periodic
/// <c>ServerHealthMonitor</c> both read from it, so they never disagree.
/// </summary>
public interface IServerHealthObserver
{
    /// <summary>Observes and rolls up the health of every owned Server.</summary>
    Task<IReadOnlyList<ServerObservation>> ObserveAllAsync(CancellationToken cancellationToken);
}
