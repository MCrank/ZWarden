using ZWarden.Domain;
using ZWarden.Domain.Agents;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Tenancy;

namespace ZWarden.Domain.Tests.Agents;

/// <summary>
/// F9 S1 (PR 1): the trusted <see cref="Agent"/> record (<c>agt-</c>) an enrollment exchange creates
/// (ADR 0007). It holds trust state only — the current per-Agent credential <b>hash</b> (never the raw
/// secret, decision 2) and an enabled flag. Trust is <b>enabled AND credential present</b>, fail-closed;
/// the actual secret match is the verifier's job (Infrastructure). Pure invariants here.
/// </summary>
public class AgentTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 12, 10, 0, 0, TimeSpan.Zero);
    private const string Hash = "5e884898da28047151d0e56f8dc6292773603d0d6aabbdd62a11ef721d1542d8";
    private const string NewHash = "6b3a55e0261b0304143f805a24924d0c1c44524821305f31d9277843b8a10f4e";

    private static Agent Enroll() => Agent.Enroll(Hash, EnrollmentId.New(), Now);

    [Test]
    public async Task Agent_is_tenant_owned_and_versioned()
    {
        await Assert.That(typeof(ITenantOwned).IsAssignableFrom(typeof(Agent))).IsTrue();
        await Assert.That(typeof(IVersioned).IsAssignableFrom(typeof(Agent))).IsTrue();
    }

    [Test]
    public async Task Enroll_creates_an_enabled_trusted_agent()
    {
        EnrollmentId via = EnrollmentId.New();
        Agent agent = Agent.Enroll(Hash, via, Now, "host-alpha");

        await Assert.That(agent.Id.IsEmpty).IsFalse();
        await Assert.That(agent.IsEnabled).IsTrue();
        await Assert.That(agent.CredentialHash).IsEqualTo(Hash);
        await Assert.That(agent.EnrolledVia).IsEqualTo(via);
        await Assert.That(agent.EnrolledAt).IsEqualTo(Now);
        await Assert.That(agent.CredentialRotatedAt).IsEqualTo(Now);
        await Assert.That(agent.Label).IsEqualTo("host-alpha");
        await Assert.That(agent.IsTrusted).IsTrue();
    }

    [Test]
    public async Task Enroll_rejects_a_blank_credential_hash()
    {
        await Assert.That(() => Agent.Enroll(" ", EnrollmentId.New(), Now))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task RotateCredential_replaces_the_hash_and_stamps_the_time()
    {
        Agent agent = Enroll();

        agent.RotateCredential(NewHash, Now.AddDays(1));

        await Assert.That(agent.CredentialHash).IsEqualTo(NewHash);
        await Assert.That(agent.CredentialRotatedAt).IsEqualTo(Now.AddDays(1));
        await Assert.That(agent.IsTrusted).IsTrue();
    }

    [Test]
    public async Task RotateCredential_rejects_a_blank_hash()
    {
        Agent agent = Enroll();
        await Assert.That(() => agent.RotateCredential("", Now.AddDays(1)))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task RevokeCredential_clears_the_hash_and_untrusts()
    {
        Agent agent = Enroll();

        agent.RevokeCredential(Now.AddHours(1));

        await Assert.That(agent.CredentialRotatedAt).IsEqualTo(Now.AddHours(1));
        await Assert.That(agent.CredentialHash).IsEqualTo(string.Empty);
        await Assert.That(agent.IsTrusted).IsFalse();
    }

    [Test]
    public async Task Disable_untrusts_even_while_the_credential_stands()
    {
        Agent agent = Enroll();

        agent.Disable();

        await Assert.That(agent.IsEnabled).IsFalse();
        await Assert.That(agent.CredentialHash).IsEqualTo(Hash);
        await Assert.That(agent.IsTrusted).IsFalse();
    }

    [Test]
    public async Task Enable_restores_trust()
    {
        Agent agent = Enroll();
        agent.Disable();

        agent.Enable();

        await Assert.That(agent.IsEnabled).IsTrue();
        await Assert.That(agent.IsTrusted).IsTrue();
    }

    // F10 S1: observed connection state on the trust anchor (decision 2 — a persisted last-seen +
    // connection state alongside the in-memory registry). It is observed state only and never touches
    // trust (enabled + credential hash).

    [Test]
    public async Task A_freshly_enrolled_agent_is_disconnected_and_never_seen()
    {
        Agent agent = Enroll();

        await Assert.That(agent.ConnectionState).IsEqualTo(AgentConnectionState.Disconnected);
        await Assert.That(agent.LastSeenAt).IsNull();
        await Assert.That(agent.LastProtocolVersion).IsNull();
    }

    [Test]
    public async Task MarkConnected_records_the_connection_time_and_protocol_version()
    {
        Agent agent = Enroll();

        agent.MarkConnected(protocolVersion: 1, Now.AddMinutes(5));

        await Assert.That(agent.ConnectionState).IsEqualTo(AgentConnectionState.Connected);
        await Assert.That(agent.LastSeenAt).IsEqualTo(Now.AddMinutes(5));
        await Assert.That(agent.LastProtocolVersion).IsEqualTo(1);
        // Trust is untouched by connection observation.
        await Assert.That(agent.IsTrusted).IsTrue();
        await Assert.That(agent.CredentialHash).IsEqualTo(Hash);
    }

    [Test]
    public async Task MarkHeartbeat_advances_last_seen_without_changing_state_or_trust()
    {
        Agent agent = Enroll();
        agent.MarkConnected(protocolVersion: 1, Now.AddMinutes(5));

        agent.MarkHeartbeat(Now.AddMinutes(6));

        await Assert.That(agent.LastSeenAt).IsEqualTo(Now.AddMinutes(6));
        await Assert.That(agent.ConnectionState).IsEqualTo(AgentConnectionState.Connected);
        await Assert.That(agent.LastProtocolVersion).IsEqualTo(1);
        await Assert.That(agent.IsTrusted).IsTrue();
    }

    [Test]
    public async Task MarkDisconnected_sets_disconnected_and_stamps_the_time_keeping_last_protocol_version()
    {
        Agent agent = Enroll();
        agent.MarkConnected(protocolVersion: 1, Now.AddMinutes(5));

        agent.MarkDisconnected(Now.AddMinutes(7));

        await Assert.That(agent.ConnectionState).IsEqualTo(AgentConnectionState.Disconnected);
        await Assert.That(agent.LastSeenAt).IsEqualTo(Now.AddMinutes(7));
        await Assert.That(agent.LastProtocolVersion).IsEqualTo(1);
        // A dropped connection does not untrust the Agent — trust is revoke/disable's job.
        await Assert.That(agent.IsTrusted).IsTrue();
    }
}
