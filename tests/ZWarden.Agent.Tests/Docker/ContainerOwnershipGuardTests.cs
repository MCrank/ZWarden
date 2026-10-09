using ZWarden.Agent.Docker;
using ZWarden.Agent.Identity;
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

    // #365: once enrolled, the Agent stamps its enrolled id; containers stamped with its local id stay owned.
    private static (ContainerOwnershipGuard Guard, AgentId Local, AgentId Enrolled) EnrolledGuard()
    {
        AgentId local = AgentId.New();
        AgentId enrolled = AgentId.New();
        AgentIdentityHolder holder = new();
        holder.Set(local);
        holder.MarkEnrolled(enrolled);
        return (new ContainerOwnershipGuard(holder), local, enrolled);
    }

    [Test]
    public async Task A_container_stamped_with_the_enrolled_id_is_allowed()
    {
        var (guard, _, enrolled) = EnrolledGuard();

        await Assert.That(guard.EnsureOwnedByThisAgent(ContainerId, LabelsOwnedBy(enrolled))).IsEqualTo(Server);
        await Assert.That(guard.TryResolveOwned(LabelsOwnedBy(enrolled), out ServerId resolved)).IsTrue();
        await Assert.That(resolved).IsEqualTo(Server);
    }

    [Test]
    public async Task A_legacy_container_stamped_with_the_local_id_is_still_allowed_after_enrollment()
    {
        var (guard, local, _) = EnrolledGuard();

        await Assert.That(guard.EnsureOwnedByThisAgent(ContainerId, LabelsOwnedBy(local))).IsEqualTo(Server);
        await Assert.That(guard.TryResolveOwned(LabelsOwnedBy(local), out ServerId resolved)).IsTrue();
        await Assert.That(resolved).IsEqualTo(Server);
    }

    [Test]
    public async Task A_container_stamped_with_any_other_id_is_still_refused_after_enrollment()
    {
        var (guard, _, _) = EnrolledGuard();
        Dictionary<string, string> foreign = LabelsOwnedBy(AgentId.New());

        await Assert.ThrowsAsync<ForeignContainerException>(() =>
            Task.FromResult(guard.EnsureOwnedByThisAgent(ContainerId, foreign)));
        await Assert.That(guard.TryResolveOwned(foreign, out _)).IsFalse();
    }

    [Test]
    public async Task A_re_enrolled_agent_does_not_own_the_previous_agents_containers()
    {
        // The machine's agent_state was wiped and it enrolled again: both ids are new. The previous Agent's
        // containers stay refused (bringing them back under management is #368, an explicit operator action).
        var (_, previousLocal, previousEnrolled) = EnrolledGuard();
        var (current, _, _) = EnrolledGuard();

        await Assert.That(current.TryResolveOwned(LabelsOwnedBy(previousEnrolled), out _)).IsFalse();
        await Assert.That(current.TryResolveOwned(LabelsOwnedBy(previousLocal), out _)).IsFalse();
    }

    // #368: discovery reports canonical containers stamped with an id this Agent doesn't own, so the Owner can see
    // them and replace the Host that made them. Reporting is all the Agent does with them.
    [Test]
    public async Task A_foreign_canonical_container_resolves_its_server_and_labelled_owner()
    {
        AgentId previous = AgentId.New();

        bool foreign = Guard().TryResolveForeign(LabelsOwnedBy(previous), out ServerId server, out AgentId owner);

        await Assert.That(foreign).IsTrue();
        await Assert.That(server).IsEqualTo(Server);
        await Assert.That(owner).IsEqualTo(previous);
    }

    [Test]
    public async Task An_owned_or_non_canonical_container_is_not_foreign()
    {
        Dictionary<string, string> unlabeled = new(StringComparer.Ordinal) { ["com.example"] = "x" };

        await Assert.That(Guard().TryResolveForeign(LabelsOwnedBy(Self), out _, out _)).IsFalse();
        await Assert.That(Guard().TryResolveForeign(unlabeled, out _, out _)).IsFalse();
        await Assert.That(Guard().TryResolveForeign(null, out _, out _)).IsFalse();
    }

    [Test]
    public async Task An_inherited_id_is_owned_and_no_longer_foreign()
    {
        AgentId previous = AgentId.New();
        AgentIdentityHolder holder = new();
        holder.Set(AgentId.New());
        holder.SetInherited([previous]);
        ContainerOwnershipGuard guard = new(holder);

        await Assert.That(guard.EnsureOwnedByThisAgent(ContainerId, LabelsOwnedBy(previous))).IsEqualTo(Server);
        await Assert.That(guard.TryResolveForeign(LabelsOwnedBy(previous), out _, out _)).IsFalse();
    }
}
