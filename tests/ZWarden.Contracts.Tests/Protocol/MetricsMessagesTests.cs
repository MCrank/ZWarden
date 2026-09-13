using ZWarden.Contracts.Protocol;
using ZWarden.Contracts.Protocol.Messages;
using ZWarden.Domain.Ids;

namespace ZWarden.Contracts.Tests.Protocol;

/// <summary>
/// F16 PR-B: the runtime-metrics wire surface. <see cref="ServerMetricsReport"/> is an additive
/// <see cref="AgentEvent"/> leaf carrying one <see cref="ServerMetricsSample"/> per Server; it round-trips
/// through the one canonical <see cref="ProtocolJson"/> with the player count nullable (null in v1.0).
/// </summary>
public class MetricsMessagesTests
{
    private static readonly DateTimeOffset At = new(2026, 9, 13, 10, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task MetricsReport_round_trips_its_samples()
    {
        ServerId server = ServerId.New();
        ServerMetricsReport report = new(
        [
            new ServerMetricsSample(server, 42.5, 3_000_000_000, 4_000_000_000, 12_000_000_000, 50_000_000_000, null, At),
        ]);
        Envelope<ServerMetricsReport> original = Envelope.Create(report, At, agentId: AgentId.New());

        Envelope<ServerMetricsReport> back = ProtocolJson.Deserialize<ServerMetricsReport>(ProtocolJson.Serialize(original));

        // Records with a list member don't get value equality on the list, so compare the samples element-wise.
        await Assert.That(back.Payload.Samples).IsEquivalentTo(original.Payload.Samples);
        await Assert.That(back.Payload.Samples[0].CpuPercent).IsEqualTo(42.5);
        await Assert.That(back.Payload.Samples[0].PlayerCount).IsNull();
    }

    [Test]
    public async Task MetricsReport_declares_its_discriminator_and_stays_additive()
    {
        string json = ProtocolJson.Serialize(Envelope.Create(new ServerMetricsReport([]), At));

        await Assert.That(json).Contains("server.metrics-report");
        await Assert.That(ProtocolJson.MessageTypes.ContainsKey("server.metrics-report")).IsTrue();
        await Assert.That(ProtocolVersionRange.Supported.Current).IsEqualTo(1);
    }
}
