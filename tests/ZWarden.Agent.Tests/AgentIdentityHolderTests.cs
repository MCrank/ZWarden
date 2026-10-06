using ZWarden.Agent.Identity;
using ZWarden.Domain.Ids;

namespace ZWarden.Agent.Tests;

/// <summary>
/// #365: the Agent has one operational id. Before enrollment it is the local (F8) id; once enrolled it is the
/// AgentId the control plane assigned (shown on the Hosts card). The Agent still owns containers stamped with its
/// local id, because Docker labels can't be changed in place.
/// </summary>
public class AgentIdentityHolderTests
{
    [Test]
    public async Task Before_enrollment_the_operational_id_is_the_local_id()
    {
        AgentId local = AgentId.New();
        AgentIdentityHolder holder = new();
        holder.Set(local);

        await Assert.That(holder.AgentId).IsEqualTo(local);
        await Assert.That(holder.LocalId).IsEqualTo(local);
        await Assert.That(holder.Owns(local)).IsTrue();
    }

    [Test]
    public async Task Once_enrolled_the_operational_id_is_the_enrolled_id()
    {
        AgentId local = AgentId.New();
        AgentId enrolled = AgentId.New();
        AgentIdentityHolder holder = new();
        holder.Set(local);

        holder.MarkEnrolled(enrolled);

        await Assert.That(holder.AgentId).IsEqualTo(enrolled);
        await Assert.That(holder.LocalId).IsEqualTo(local);
    }

    [Test]
    public async Task Once_enrolled_it_owns_both_ids_and_nothing_else()
    {
        AgentId local = AgentId.New();
        AgentId enrolled = AgentId.New();
        AgentIdentityHolder holder = new();
        holder.Set(local);
        holder.MarkEnrolled(enrolled);

        await Assert.That(holder.Owns(enrolled)).IsTrue();
        await Assert.That(holder.Owns(local)).IsTrue();
        await Assert.That(holder.Owns(AgentId.New())).IsFalse();
    }

    [Test]
    public async Task Enrollment_recorded_before_the_local_id_resolves_is_kept()
    {
        AgentId local = AgentId.New();
        AgentId enrolled = AgentId.New();
        AgentIdentityHolder holder = new();

        holder.MarkEnrolled(enrolled);
        holder.Set(local);

        await Assert.That(holder.AgentId).IsEqualTo(enrolled);
        await Assert.That(holder.Owns(local)).IsTrue();
    }

    [Test]
    public async Task Reading_before_the_local_id_resolves_throws()
    {
        AgentIdentityHolder holder = new();

        await Assert.That(() => holder.AgentId).Throws<InvalidOperationException>();
        await Assert.That(() => holder.LocalId).Throws<InvalidOperationException>();
    }
}
