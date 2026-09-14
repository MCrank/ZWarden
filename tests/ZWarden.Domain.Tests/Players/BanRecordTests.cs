using ZWarden.Domain.Ids;
using ZWarden.Domain.Players;
using ZWarden.Domain.Tenancy;

namespace ZWarden.Domain.Tests.Players;

/// <summary>
/// F19: the <see cref="BanRecord"/> aggregate (<c>ban-</c>) — ZWarden's advisory ban registry (ADR 0027). A ban
/// is issued Active with its issuer and can be lifted once; lifting is idempotent so a repeated unban never
/// errors. Tenant-owned.
/// </summary>
public class BanRecordTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 14, 10, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task Ban_record_is_tenant_owned()
    {
        await Assert.That(typeof(ITenantOwned).IsAssignableFrom(typeof(BanRecord))).IsTrue();
    }

    [Test]
    public async Task Issue_creates_an_active_ban_with_its_issuer()
    {
        ServerId server = ServerId.New();
        UserId issuer = UserId.New();

        BanRecord record = BanRecord.Issue(server, "Mallory", "cheating", issuer, Now);

        await Assert.That(record.Id.IsEmpty).IsFalse();
        await Assert.That(record.ServerId).IsEqualTo(server);
        await Assert.That(record.Username).IsEqualTo("Mallory");
        await Assert.That(record.Reason).IsEqualTo("cheating");
        await Assert.That(record.IssuedByUserId).IsEqualTo(issuer);
        await Assert.That(record.IssuedAt).IsEqualTo(Now);
        await Assert.That(record.Status).IsEqualTo(BanStatus.Active);
        await Assert.That(record.LiftedByUserId).IsNull();
        await Assert.That(record.LiftedAt).IsNull();
    }

    [Test]
    public async Task Issue_rejects_a_blank_username()
    {
        await Assert.That(() => BanRecord.Issue(ServerId.New(), "  ", null, UserId.New(), Now))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task Lift_marks_the_record_lifted_with_its_actor()
    {
        UserId lifter = UserId.New();
        BanRecord record = BanRecord.Issue(ServerId.New(), "Mallory", null, UserId.New(), Now);

        record.Lift(lifter, Now.AddHours(1));

        await Assert.That(record.Status).IsEqualTo(BanStatus.Lifted);
        await Assert.That(record.LiftedByUserId).IsEqualTo(lifter);
        await Assert.That(record.LiftedAt).IsEqualTo(Now.AddHours(1));
    }

    [Test]
    public async Task Lift_is_idempotent()
    {
        UserId firstLifter = UserId.New();
        BanRecord record = BanRecord.Issue(ServerId.New(), "Mallory", null, UserId.New(), Now);

        record.Lift(firstLifter, Now.AddHours(1));
        record.Lift(UserId.New(), Now.AddHours(2));

        await Assert.That(record.Status).IsEqualTo(BanStatus.Lifted);
        await Assert.That(record.LiftedByUserId).IsEqualTo(firstLifter);
        await Assert.That(record.LiftedAt).IsEqualTo(Now.AddHours(1));
    }
}
