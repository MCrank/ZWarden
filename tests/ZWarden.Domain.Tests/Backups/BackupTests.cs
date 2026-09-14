using ZWarden.Domain.Backups;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Tenancy;

namespace ZWarden.Domain.Tests.Backups;

/// <summary>
/// F24: the <see cref="Backup"/> aggregate (<c>bkp-</c>) — a verifiable, tenant-owned record of a world-data
/// archive the Agent wrote host-side (ADR 0028). Write-once: its facts are fixed when the Agent reports them, so
/// there is no mutator. <see cref="Backup.Record"/> validates that a backup always has a locator and a checksum
/// and a non-negative size — a backup with no verifiable archive is never recorded — and leaves the tenant unset
/// for the ownership interceptor to stamp (ADR 0016).
/// </summary>
public class BackupTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 14, 10, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task Backup_is_tenant_owned()
    {
        await Assert.That(typeof(ITenantOwned).IsAssignableFrom(typeof(Backup))).IsTrue();
    }

    [Test]
    public async Task Record_captures_the_reported_archive_facts()
    {
        ServerId server = ServerId.New();
        AgentId agent = AgentId.New();

        Backup backup = Backup.Record(
            server, agent, "world-20260914-100000.tar.gz", 4096, "abc123", BackupReason.Manual, Now);

        await Assert.That(backup.Id.IsEmpty).IsFalse();
        await Assert.That(backup.TenantId).IsEqualTo(default(TenantId));
        await Assert.That(backup.ServerId).IsEqualTo(server);
        await Assert.That(backup.AgentId).IsEqualTo(agent);
        await Assert.That(backup.ArchiveName).IsEqualTo("world-20260914-100000.tar.gz");
        await Assert.That(backup.SizeBytes).IsEqualTo(4096L);
        await Assert.That(backup.Sha256).IsEqualTo("abc123");
        await Assert.That(backup.Reason).IsEqualTo(BackupReason.Manual);
        await Assert.That(backup.CreatedAt).IsEqualTo(Now);
        await Assert.That(backup.ExpiresAt).IsNull();
    }

    [Test]
    public async Task Record_carries_an_optional_expiry_hint()
    {
        Backup backup = Backup.Record(
            ServerId.New(), AgentId.New(), "w.tar.gz", 1, "aa", BackupReason.PreOperation, Now, Now.AddDays(7));

        await Assert.That(backup.Reason).IsEqualTo(BackupReason.PreOperation);
        await Assert.That(backup.ExpiresAt).IsEqualTo(Now.AddDays(7));
    }

    [Test]
    public async Task Record_rejects_a_blank_archive_name()
    {
        await Assert.That(() => Backup.Record(ServerId.New(), AgentId.New(), "  ", 1, "aa", BackupReason.Manual, Now))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task Record_rejects_a_blank_checksum()
    {
        await Assert.That(() => Backup.Record(ServerId.New(), AgentId.New(), "w.tar.gz", 1, "  ", BackupReason.Manual, Now))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task Record_rejects_a_negative_size()
    {
        await Assert.That(() => Backup.Record(ServerId.New(), AgentId.New(), "w.tar.gz", -1, "aa", BackupReason.Manual, Now))
            .Throws<ArgumentOutOfRangeException>();
    }
}
