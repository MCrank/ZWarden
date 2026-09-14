using ZWarden.Domain.Configuration;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Tenancy;

namespace ZWarden.Domain.Tests.Configuration;

/// <summary>
/// F20b (PR 2): the <see cref="ConfigurationRevision"/> aggregate (<c>cfg-</c>) — a recorded before/after
/// state of one of a Server's four configuration files, captured as parsed values (the canonical snapshot),
/// not bytes (ADR 0011). It carries the drift-baseline fingerprint the Agent re-checks before every write.
/// Tenant-owned and versioned.
/// </summary>
public class ConfigurationRevisionTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 14, 10, 0, 0, TimeSpan.Zero);
    private const string Snapshot = "[[\"PVPMeleeDamage\",\"n:1:i\"],[\"PublicName\",\"s:My Server\"]]";
    private const string Hash = "9f2c8b1a0e5d4c3b2a1908f7e6d5c4b3a2918070f6e5d4c3b2a1908070605041";

    [Test]
    public async Task Configuration_revision_is_tenant_owned_and_versioned()
    {
        await Assert.That(typeof(ITenantOwned).IsAssignableFrom(typeof(ConfigurationRevision))).IsTrue();
        await Assert.That(typeof(IVersioned).IsAssignableFrom(typeof(ConfigurationRevision))).IsTrue();
    }

    [Test]
    public async Task Record_captures_the_server_file_snapshot_and_baseline_hash()
    {
        ServerId server = ServerId.New();
        UserId author = UserId.New();

        ConfigurationRevision revision =
            ConfigurationRevision.Record(server, PzConfigFile.SandboxVars, Snapshot, Hash, Now, author);

        await Assert.That(revision.Id.IsEmpty).IsFalse();
        await Assert.That(revision.ServerId).IsEqualTo(server);
        await Assert.That(revision.File).IsEqualTo(PzConfigFile.SandboxVars);
        await Assert.That(revision.CanonicalSnapshot).IsEqualTo(Snapshot);
        await Assert.That(revision.SnapshotHash).IsEqualTo(Hash);
        await Assert.That(revision.CreatedAt).IsEqualTo(Now);
        await Assert.That(revision.CreatedByUserId).IsEqualTo(author);
    }

    [Test]
    public async Task Record_leaves_the_tenant_unset_for_the_ownership_interceptor()
    {
        ConfigurationRevision revision =
            ConfigurationRevision.Record(ServerId.New(), PzConfigFile.Ini, Snapshot, Hash, Now);

        await Assert.That(revision.TenantId.IsEmpty).IsTrue();
    }

    [Test]
    public async Task Record_allows_an_unattributed_capture()
    {
        // A baseline captured from an existing on-disk file has no acting user.
        ConfigurationRevision revision =
            ConfigurationRevision.Record(ServerId.New(), PzConfigFile.Ini, Snapshot, Hash, Now);

        await Assert.That(revision.CreatedByUserId).IsNull();
    }

    [Test]
    public async Task Record_rejects_an_empty_server()
    {
        await Assert.That(() =>
                ConfigurationRevision.Record(default, PzConfigFile.Ini, Snapshot, Hash, Now))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task Record_rejects_a_blank_snapshot_or_hash()
    {
        await Assert.That(() =>
                ConfigurationRevision.Record(ServerId.New(), PzConfigFile.Ini, "  ", Hash, Now))
            .Throws<ArgumentException>();
        await Assert.That(() =>
                ConfigurationRevision.Record(ServerId.New(), PzConfigFile.Ini, Snapshot, "  ", Now))
            .Throws<ArgumentException>();
    }
}
