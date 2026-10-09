using ZWarden.Agent.Docker;
using ZWarden.Contracts.Protocol.Messages;
using ZWarden.Domain.Ids;

namespace ZWarden.Agent.Tests.Docker;

/// <summary>
/// #368: after a machine is wiped and enrolled again, its PZ containers carry the previous Agent's id. The scanner
/// lists them for the snapshot, so the Owner sees them on the new Host's card. It only reads the container list;
/// owned and non-canonical containers never appear.
/// </summary>
public class ForeignContainerScannerTests
{
    private static readonly AgentId Self = AgentId.New();

    private static ForeignContainerScanner Scanner(FakeDockerEngine engine) =>
        new(engine, new ContainerOwnershipGuard(new FixedAgentIdentity(Self)));

    private static EngineContainer Container(string id, AgentId owner, ServerId server, string state = "running") =>
        new(
            id,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [CanonicalLabels.Managed] = CanonicalLabels.ManagedValue,
                [CanonicalLabels.Runtime] = CanonicalLabels.RuntimeValue,
                [CanonicalLabels.SchemaVersion] = CanonicalLabels.SchemaVersionValue,
                [CanonicalLabels.ServerId] = server.ToString(),
                [CanonicalLabels.AgentId] = owner.ToString(),
            },
            state,
            []);

    [Test]
    public async Task Only_canonical_containers_stamped_with_another_id_are_listed()
    {
        AgentId previous = AgentId.New();
        ServerId orphan = ServerId.New();
        FakeDockerEngine engine = new();
        engine.Listed.Add(Container("0123456789abcdef0123", previous, orphan, "exited"));
        engine.Listed.Add(Container("owned000000000000000", Self, ServerId.New()));
        engine.Listed.Add(new EngineContainer("stranger", new Dictionary<string, string>(), "running", []));

        IReadOnlyList<ForeignContainer> foreign = await Scanner(engine).ScanAsync(CancellationToken.None);

        await Assert.That(foreign).Count().IsEqualTo(1);
        await Assert.That(foreign[0]).IsEqualTo(new ForeignContainer("0123456789ab", orphan, previous, "exited"));
    }

    [Test]
    public async Task The_list_is_capped()
    {
        FakeDockerEngine engine = new();
        for (int i = 0; i < AgentStateSnapshot.MaxForeignContainers + 1; i++)
        {
            engine.Listed.Add(Container($"c{i:D19}", AgentId.New(), ServerId.New()));
        }

        IReadOnlyList<ForeignContainer> foreign = await Scanner(engine).ScanAsync(CancellationToken.None);

        await Assert.That(foreign).Count().IsEqualTo(AgentStateSnapshot.MaxForeignContainers);
    }

    [Test]
    public async Task An_overlong_state_is_truncated()
    {
        FakeDockerEngine engine = new();
        engine.Listed.Add(Container("abc", AgentId.New(), ServerId.New(), new string('x', 100)));

        IReadOnlyList<ForeignContainer> foreign = await Scanner(engine).ScanAsync(CancellationToken.None);

        await Assert.That(foreign[0].ContainerId).IsEqualTo("abc");
        await Assert.That(foreign[0].State.Length).IsEqualTo(ForeignContainer.MaxStateLength);
    }
}
