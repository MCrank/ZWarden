using ZWarden.Domain.Agents;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Tenancy;

namespace ZWarden.Domain.Tests.Agents;

/// <summary>
/// #368: a <see cref="HostReplacement"/> (<c>hr-</c>) records that the Owner replaced a wiped Host (the predecessor)
/// with the Host its machine enrolled as next (the successor). The successor's Agent owns containers stamped with every
/// predecessor id recorded against it; ZWarden, not the Agent, holds that list.
/// </summary>
public class HostReplacementTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 10, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task A_replacement_is_tenant_owned()
    {
        await Assert.That(typeof(ITenantOwned).IsAssignableFrom(typeof(HostReplacement))).IsTrue();
    }

    [Test]
    public async Task Record_captures_who_replaced_what_and_when()
    {
        AgentId successor = AgentId.New();
        AgentId predecessor = AgentId.New();
        UserId owner = UserId.New();

        HostReplacement replacement = HostReplacement.Record(successor, predecessor, owner, Now);

        await Assert.That(replacement.Id.ToString()).StartsWith("hr-");
        await Assert.That(replacement.SuccessorId).IsEqualTo(successor);
        await Assert.That(replacement.PredecessorId).IsEqualTo(predecessor);
        await Assert.That(replacement.ReplacedBy).IsEqualTo(owner);
        await Assert.That(replacement.ReplacedAt).IsEqualTo(Now);
    }

    [Test]
    public async Task A_host_cannot_replace_itself()
    {
        AgentId host = AgentId.New();

        await Assert.That(() => HostReplacement.Record(host, host, UserId.New(), Now)).Throws<ArgumentException>();
    }

    [Test]
    public async Task When_the_successor_is_replaced_in_turn_the_inherited_id_moves_on()
    {
        AgentId a = AgentId.New();
        AgentId b = AgentId.New();
        AgentId c = AgentId.New();
        HostReplacement aByB = HostReplacement.Record(b, a, UserId.New(), Now);

        aByB.PassTo(c);

        await Assert.That(aByB.SuccessorId).IsEqualTo(c);
        await Assert.That(aByB.PredecessorId).IsEqualTo(a);
    }
}
