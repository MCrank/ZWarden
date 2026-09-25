using ZWarden.Contracts.Protocol;
using ZWarden.Contracts.Protocol.Messages;
using ZWarden.Domain.Ids;

namespace ZWarden.Contracts.Tests.Protocol;

/// <summary>
/// F16 PR-B: the runtime-metrics wire surface. <see cref="ServerMetricsReport"/> is an additive
/// <see cref="AgentEvent"/> leaf carrying one <see cref="ServerMetricsSample"/> per Server; it round-trips
/// through the one canonical <see cref="ProtocolJson"/>; the #257 fleet facts (players, start time, build) are
/// trailing nullable members, so an older sample without them still reads.
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
    public async Task MetricsReport_round_trips_the_fleet_facts()
    {
        DateTimeOffset started = At.AddHours(-3);
        DateTimeOffset counted = At.AddMinutes(-2);
        ServerMetricsReport report = new(
        [
            new ServerMetricsSample(ServerId.New(), 10, 1, 2, null, null, 4, At, counted, started, "19876543", "42.20.4"),
        ]);

        Envelope<ServerMetricsReport> back = ProtocolJson.Deserialize<ServerMetricsReport>(
            ProtocolJson.Serialize(Envelope.Create(report, At)));

        ServerMetricsSample sample = back.Payload.Samples[0];
        await Assert.That(sample.PlayerCount).IsEqualTo(4);
        await Assert.That(sample.PlayerCountSampledAt).IsEqualTo(counted);
        await Assert.That(sample.StartedAt).IsEqualTo(started);
        await Assert.That(sample.InstalledBuildId).IsEqualTo("19876543");
        await Assert.That(sample.GameVersion).IsEqualTo("42.20.4");
    }

    [Test]
    public async Task A_sample_without_the_fleet_facts_still_reads_as_nulls()
    {
        // An Agent built before #257 omits the trailing members entirely; the additive change must still read it
        // (ADR 0020 — no protocol bump).
        string json = ProtocolJson.Serialize(Envelope.Create(
            new ServerMetricsReport([new ServerMetricsSample(ServerId.New(), 1, 1, 1, null, null, null, At)]), At));
        System.Text.Json.Nodes.JsonNode node = System.Text.Json.Nodes.JsonNode.Parse(json)!;
        System.Text.Json.Nodes.JsonObject sampleNode = node["payload"]!["samples"]![0]!.AsObject();
        sampleNode.Remove("playerCountSampledAt");
        sampleNode.Remove("startedAt");
        sampleNode.Remove("installedBuildId");
        sampleNode.Remove("gameVersion");

        Envelope<ServerMetricsReport> back = ProtocolJson.Deserialize<ServerMetricsReport>(node.ToJsonString());

        ServerMetricsSample sample = back.Payload.Samples[0];
        await Assert.That(sample.PlayerCountSampledAt).IsNull();
        await Assert.That(sample.StartedAt).IsNull();
        await Assert.That(sample.InstalledBuildId).IsNull();
        await Assert.That(sample.GameVersion).IsNull();
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
