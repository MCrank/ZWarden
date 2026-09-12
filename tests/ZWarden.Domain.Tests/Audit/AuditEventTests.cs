using ZWarden.Domain;
using ZWarden.Domain.Audit;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Tenancy;

namespace ZWarden.Domain.Tests.Audit;

/// <summary>
/// F6 S1: the <see cref="AuditEvent"/> record (<c>aud-</c>) is tenant-owned and <b>append-only</b> — it
/// carries only non-secret data, has no concurrency token, and exposes no mutation (ADR 0019). Offline tier.
/// </summary>
public class AuditEventTests
{
    [Test]
    public async Task Audit_event_is_tenant_owned()
    {
        await Assert.That(typeof(ITenantOwned).IsAssignableFrom(typeof(AuditEvent))).IsTrue();
    }

    [Test]
    public async Task Audit_event_is_append_only_with_no_version_token()
    {
        // Append-only: no IVersioned concurrency token (a mutable version would contradict append-only),
        // and no writable public property setter (every field is init-only).
        await Assert.That(typeof(IVersioned).IsAssignableFrom(typeof(AuditEvent))).IsFalse();

        bool anyPublicSetter = typeof(AuditEvent)
            .GetProperties()
            .Any(p => p.SetMethod is { IsPublic: true } setter
                && !setter.ReturnParameter.GetRequiredCustomModifiers()
                    .Any(m => m.FullName == "System.Runtime.CompilerServices.IsExternalInit"));
        await Assert.That(anyPublicSetter).IsFalse();
    }

    [Test]
    public async Task Create_sets_the_recorded_fields()
    {
        UserId actor = UserId.New();
        ServerId server = ServerId.New();
        DateTimeOffset when = new(2026, 9, 12, 8, 30, 0, TimeSpan.Zero);

        AuditEvent audit = AuditEvent.Create(
            action: "Authentication.SignInSucceeded",
            outcome: AuditOutcome.Succeeded,
            occurredAt: when,
            actorUserId: actor,
            serverId: server,
            correlationId: "corr-123",
            detail: "password");

        await Assert.That(audit.Action).IsEqualTo("Authentication.SignInSucceeded");
        await Assert.That(audit.Outcome).IsEqualTo(AuditOutcome.Succeeded);
        await Assert.That(audit.OccurredAt).IsEqualTo(when);
        await Assert.That(audit.ActorUserId).IsEqualTo(actor);
        await Assert.That(audit.ServerId).IsEqualTo(server);
        await Assert.That(audit.CorrelationId).IsEqualTo("corr-123");
        await Assert.That(audit.Detail).IsEqualTo("password");
        await Assert.That(audit.Id.IsEmpty).IsFalse();
    }

    [Test]
    public async Task Create_defaults_optional_context_to_null()
    {
        AuditEvent audit = AuditEvent.Create("Role.Created", AuditOutcome.Succeeded, DateTimeOffset.UnixEpoch);

        await Assert.That(audit.ActorUserId).IsNull();
        await Assert.That(audit.ServerId).IsNull();
        await Assert.That(audit.CorrelationId).IsNull();
        await Assert.That(audit.Detail).IsNull();
    }

    [Test]
    public async Task Create_rejects_a_blank_action()
    {
        await Assert.That(() => AuditEvent.Create(" ", AuditOutcome.Succeeded, DateTimeOffset.UnixEpoch))
            .Throws<ArgumentException>();
    }
}
