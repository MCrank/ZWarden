using ZWarden.Agent.Diagnostics;
using ZWarden.Agent.Health;
using ZWarden.Contracts.Protocol;
using ZWarden.Domain.Ids;

namespace ZWarden.Agent.Tests;

/// <summary>
/// F8 diagnostics hook: a snapshot reports the Agent's identity, the protocol version it speaks, its
/// current health and a non-negative uptime.
/// </summary>
public class AgentDiagnosticsTests
{
    [Test]
    public async Task Capture_reports_identity_protocol_version_and_health()
    {
        AgentId id = AgentId.New();
        var health = new AgentHealthState(new RecordingLogger<AgentHealthState>());
        health.Report(AgentHealthStatus.Degraded, "warming up");

        var diagnostics = new AgentDiagnostics(new FixedAgentIdentity(id), health, TimeProvider.System);

        AgentDiagnosticsSnapshot snapshot = diagnostics.Capture();

        await Assert.That(snapshot.AgentId).IsEqualTo(id);
        await Assert.That(snapshot.ProtocolVersion).IsEqualTo(ProtocolVersion.Current);
        await Assert.That(snapshot.Health).IsEqualTo(AgentHealthStatus.Degraded);
        await Assert.That(snapshot.HealthReason).IsEqualTo("warming up");
        await Assert.That(snapshot.Uptime >= TimeSpan.Zero).IsTrue();
    }
}
