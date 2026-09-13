using ZWarden.Agent.Docker;
using ZWarden.Domain.Ids;

namespace ZWarden.Agent.Tests.Docker;

/// <summary>
/// F13 S2: allowed-container enforcement (trust-boundaries.md §4). The guard is fail-closed and runs before
/// any Docker verb: a canonical container owned by this Agent is operable; a foreign-owned, mislabelled or
/// unrecognised one is refused with <see cref="ForeignContainerException"/>. This is the actual control that
/// stands in for the container-level authorization no socket proxy can perform (ADR 0008).
/// </summary>
public class ContainerOwnershipGuardTests
{
    private static readonly AgentId Self = AgentId.New();
    private static readonly ServerId Server = ServerId.New();
    private const string ContainerId = "abc123def456";

    private static ContainerOwnershipGuard Guard() =>
        new(new FixedAgentIdentity(Self));

    private static Dictionary<string, string> LabelsOwnedBy(AgentId owner) => new(StringComparer.Ordinal)
    {
        [CanonicalLabels.Managed] = CanonicalLabels.ManagedValue,
        [CanonicalLabels.Runtime] = CanonicalLabels.RuntimeValue,
        [CanonicalLabels.SchemaVersion] = CanonicalLabels.SchemaVersionValue,
        [CanonicalLabels.ServerId] = Server.ToString(),
        [CanonicalLabels.AgentId] = owner.ToString(),
    };

    [Test]
    public async Task A_canonical_container_owned_by_this_agent_is_allowed_and_yields_its_server_id()
    {
        ServerId resolved = Guard().EnsureOwnedByThisAgent(ContainerId, LabelsOwnedBy(Self));

        await Assert.That(resolved).IsEqualTo(Server);
    }

    [Test]
    public async Task A_container_owned_by_a_different_agent_is_refused()
    {
        Dictionary<string, string> foreign = LabelsOwnedBy(AgentId.New());

        var ex = await Assert.ThrowsAsync<ForeignContainerException>(() =>
            Task.FromResult(Guard().EnsureOwnedByThisAgent(ContainerId, foreign)));

        await Assert.That(ex!.ContainerId).IsEqualTo(ContainerId);
    }

    [Test]
    public async Task A_non_canonical_container_is_refused()
    {
        Dictionary<string, string> unlabeled = new(StringComparer.Ordinal) { ["com.example"] = "x" };

        await Assert.ThrowsAsync<ForeignContainerException>(() =>
            Task.FromResult(Guard().EnsureOwnedByThisAgent(ContainerId, unlabeled)));
    }

    [Test]
    public async Task Null_labels_are_refused()
    {
        await Assert.ThrowsAsync<ForeignContainerException>(() =>
            Task.FromResult(Guard().EnsureOwnedByThisAgent(ContainerId, null)));
    }

    [Test]
    public async Task A_blank_container_id_is_rejected()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            Task.FromResult(Guard().EnsureOwnedByThisAgent("  ", LabelsOwnedBy(Self))));
    }
}
