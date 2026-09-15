using ZWarden.Contracts.Protocol;
using ZWarden.Contracts.Protocol.Messages;
using ZWarden.Domain.Ids;

namespace ZWarden.Contracts.Tests.Protocol;

/// <summary>
/// F29 PR-B: the diagnostics-gather wire surface. <see cref="GatherHostDiagnostics"/> is a payload-free host-level
/// leaf and <see cref="GatherServerDiagnostics"/> a payload-free per-server leaf (the Server rides the envelope),
/// each declaring a unique <c>diagnostics.*</c> discriminator; the completion carries optional
/// <see cref="HostDiagnosticsResult"/>/<see cref="ServerDiagnosticsResult"/> bundles that are additive (ADR 0020)
/// and round-trip through the one canonical <see cref="ProtocolJson"/>. The change is additive, so
/// <see cref="ProtocolVersion.Current"/> stays 1.
/// </summary>
public class GatherDiagnosticsMessagesTests
{
    private static readonly DateTimeOffset At = new(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task GatherHostDiagnostics_round_trips_host_level_without_a_server()
    {
        AgentCommand command = new GatherHostDiagnostics();
        Envelope<AgentCommand> original = Envelope.Create(
            command, At, agentId: AgentId.New(), operationId: OperationId.New());

        Envelope<IProtocolMessage> back = ProtocolJson.Deserialize(ProtocolJson.Serialize(original));

        await Assert.That(back.ServerId).IsNull();
        await Assert.That(back.Payload).IsEqualTo((IProtocolMessage)command);
    }

    [Test]
    public async Task GatherServerDiagnostics_round_trips_with_the_target_server_on_the_envelope()
    {
        ServerId server = ServerId.New();
        AgentCommand command = new GatherServerDiagnostics();
        Envelope<AgentCommand> original = Envelope.Create(
            command, At, agentId: AgentId.New(), serverId: server, operationId: OperationId.New());

        Envelope<IProtocolMessage> back = ProtocolJson.Deserialize(ProtocolJson.Serialize(original));

        await Assert.That(back.ServerId).IsEqualTo(server);
        await Assert.That(back.Payload).IsEqualTo((IProtocolMessage)command);
    }

    [Test]
    public async Task Both_gather_commands_declare_their_diagnostics_discriminators()
    {
        await Assert.That(Discriminator(typeof(GatherHostDiagnostics))).IsEqualTo("diagnostics.gather-host");
        await Assert.That(Discriminator(typeof(GatherServerDiagnostics))).IsEqualTo("diagnostics.gather-server");
        await Assert.That(ProtocolJson.MessageTypes.ContainsKey("diagnostics.gather-host")).IsTrue();
        await Assert.That(ProtocolJson.MessageTypes.ContainsKey("diagnostics.gather-server")).IsTrue();
    }

    [Test]
    public async Task OperationCompleted_round_trips_a_host_diagnostics_bundle()
    {
        HostDiagnosticsResult host = new(
        [
            new DiagnosticCheckFact(DiagnosticDomain.Docker, ProbeStatus.Pass, "Docker reachable.", null),
            new DiagnosticCheckFact(DiagnosticDomain.Filesystem, ProbeStatus.Warn, "Disk low.", "3% free"),
        ]);
        Envelope<OperationCompleted> original = Envelope.Create(
            new OperationCompleted(OperationOutcome.Succeeded, HostDiagnostics: host),
            At, agentId: AgentId.New(), operationId: OperationId.New());

        Envelope<OperationCompleted> back = ProtocolJson.Deserialize<OperationCompleted>(ProtocolJson.Serialize(original));

        // Record equality would compare the IReadOnlyList by reference, so assert the fields that matter.
        await Assert.That(back.Payload.HostDiagnostics!.Checks.Count).IsEqualTo(2);
        await Assert.That(back.Payload.HostDiagnostics!.Checks[0].Domain).IsEqualTo(DiagnosticDomain.Docker);
        await Assert.That(back.Payload.ServerDiagnostics).IsNull();
    }

    [Test]
    public async Task OperationCompleted_round_trips_a_server_diagnostics_bundle_with_untrusted_detail()
    {
        ServerDiagnosticsResult server = new(
        [
            new DiagnosticCheckFact(DiagnosticDomain.Rcon, ProbeStatus.Fail, "RCON unreachable.", "connection refused"),
            new DiagnosticCheckFact(DiagnosticDomain.GamePort, ProbeStatus.Pass, "Game port reachable.", null),
        ]);
        Envelope<OperationCompleted> original = Envelope.Create(
            new OperationCompleted(OperationOutcome.Succeeded, ServerDiagnostics: server),
            At, serverId: ServerId.New(), operationId: OperationId.New());

        Envelope<OperationCompleted> back = ProtocolJson.Deserialize<OperationCompleted>(ProtocolJson.Serialize(original));

        await Assert.That(back.Payload.ServerDiagnostics!.Checks[0].Status).IsEqualTo(ProbeStatus.Fail);
        await Assert.That(back.Payload.ServerDiagnostics!.Checks[0].Detail).IsEqualTo("connection refused");
        await Assert.That(back.Payload.HostDiagnostics).IsNull();
    }

    [Test]
    public async Task OperationCompleted_without_a_diagnostics_bundle_stays_null()
    {
        Envelope<OperationCompleted> original = Envelope.Create(
            new OperationCompleted(OperationOutcome.Succeeded), At, operationId: OperationId.New());

        Envelope<OperationCompleted> back = ProtocolJson.Deserialize<OperationCompleted>(ProtocolJson.Serialize(original));

        await Assert.That(back.Payload.HostDiagnostics).IsNull();
        await Assert.That(back.Payload.ServerDiagnostics).IsNull();
    }

    [Test]
    public async Task The_change_is_additive_so_the_protocol_version_stays_one()
    {
        await Assert.That(ProtocolVersionRange.Supported.Current).IsEqualTo(1);
    }

    private static string? Discriminator(Type type) =>
        ((ProtocolMessageAttribute?)Attribute.GetCustomAttribute(type, typeof(ProtocolMessageAttribute)))?.Discriminator;
}
