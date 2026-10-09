using ZWarden.Contracts.Protocol.Messages;
using ZWarden.Domain.Ids;

namespace ZWarden.Agent.Docker;

/// <summary>
/// #368: lists the canonical PZ containers on this Host that are stamped with an id this Agent doesn't own, so they
/// ride the state snapshot to the Owner. When a machine is wiped and enrolled again, its old containers carry the
/// previous Agent's id; the Owner can then replace that Host from the new one's card. This only reads the container
/// list. Nothing here makes a foreign container operable (<see cref="ContainerOwnershipGuard"/> still refuses it).
/// </summary>
public sealed class ForeignContainerScanner
{
    private const int ShortIdLength = 12;

    private readonly IDockerEngine _engine;
    private readonly ContainerOwnershipGuard _guard;

    /// <summary>Creates the scanner over the engine and the ownership guard.</summary>
    public ForeignContainerScanner(IDockerEngine engine, ContainerOwnershipGuard guard)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(guard);
        _engine = engine;
        _guard = guard;
    }

    /// <summary>The foreign canonical containers, at most <see cref="AgentStateSnapshot.MaxForeignContainers"/>.</summary>
    public async Task<IReadOnlyList<ForeignContainer>> ScanAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<EngineContainer> all = await _engine.ListAsync(cancellationToken).ConfigureAwait(false);
        List<ForeignContainer> foreign = [];
        foreach (EngineContainer container in all)
        {
            if (foreign.Count == AgentStateSnapshot.MaxForeignContainers)
            {
                break;
            }

            if (_guard.TryResolveForeign(container.Labels, out ServerId serverId, out AgentId labelled))
            {
                foreign.Add(new ForeignContainer(
                    Truncate(container.Id, ShortIdLength),
                    serverId,
                    labelled,
                    Truncate(container.State, ForeignContainer.MaxStateLength)));
            }
        }

        return foreign;
    }

    private static string Truncate(string value, int length) => value.Length <= length ? value : value[..length];
}
