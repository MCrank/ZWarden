using ZWarden.Contracts.Protocol;
using ZWarden.Contracts.Protocol.Messages;
using ZWarden.Domain.Ids;

namespace ZWarden.Contracts.Tests.Protocol;

/// <summary>
/// F21: the Workshop/Mod discovery wire surface. <see cref="DiscoverMods"/> is a payload-free leaf of the closed
/// <see cref="AgentCommand"/> vocabulary carrying a <c>mods.discover</c> discriminator (the target Server rides the
/// envelope, the Operation is its <c>OperationId</c> — the <see cref="ListPlayers"/> shape). The completion carries
/// an optional, additive <see cref="ModDiscoveryResult"/> (ADR 0020), so <see cref="ProtocolVersion.Current"/>
/// stays 1. Discovered ids and names are <b>untrusted</b> PZ/Workshop output (trust-boundaries.md §8), round-tripped
/// verbatim.
/// </summary>
public class ModMessagesTests
{
    private static readonly DateTimeOffset At = new(2026, 9, 14, 9, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task DiscoverMods_round_trips_with_the_target_server_on_the_envelope()
    {
        ServerId server = ServerId.New();
        AgentCommand command = new DiscoverMods();

        Envelope<IProtocolMessage> back = ProtocolJson.Deserialize(ProtocolJson.Serialize(
            Envelope.Create(command, At, agentId: AgentId.New(), serverId: server, operationId: OperationId.New())));

        await Assert.That(back.ServerId).IsEqualTo(server);
        await Assert.That(back.Payload).IsEqualTo((IProtocolMessage)command);
    }

    [Test]
    public async Task DiscoverMods_declares_its_registered_discriminator()
    {
        ProtocolMessageAttribute? attribute =
            (ProtocolMessageAttribute?)Attribute.GetCustomAttribute(typeof(DiscoverMods), typeof(ProtocolMessageAttribute));

        await Assert.That(attribute).IsNotNull();
        await Assert.That(attribute!.Discriminator).IsEqualTo("mods.discover");
        await Assert.That(ProtocolJson.MessageTypes.ContainsKey("mods.discover")).IsTrue();
    }

    [Test]
    public async Task OperationCompleted_round_trips_a_mod_discovery_result()
    {
        ModDiscoveryResult result = new(
            InstalledItems:
            [
                new DiscoveredWorkshopItem("2392709985", [new DiscoveredMod("Brita_2", "Brita's Weapon Pack")]),
                new DiscoveredWorkshopItem("2335368829", [new DiscoveredMod("ModA", null), new DiscoveredMod("ModB", "Mod B")]),
            ],
            ConfiguredWorkshopIds: ["2392709985", "2335368829", "999"],
            EnabledModIds: ["Brita_2", "Missing"],
            Findings:
            [
                new ModCompatFinding(ModCompatKind.ReferencedNotInstalled, "999", null),
                new ModCompatFinding(ModCompatKind.EnabledButMissing, "Missing", "no installed mod provides this id"),
            ]);

        Envelope<OperationCompleted> original = Envelope.Create(
            new OperationCompleted(OperationOutcome.Succeeded, Mods: result),
            At,
            serverId: ServerId.New(),
            operationId: OperationId.New());

        Envelope<OperationCompleted> back = ProtocolJson.Deserialize<OperationCompleted>(ProtocolJson.Serialize(original));

        ModDiscoveryResult mods = back.Payload.Mods!;
        await Assert.That(mods.InstalledItems.Count).IsEqualTo(2);
        await Assert.That(mods.InstalledItems[0].WorkshopId).IsEqualTo("2392709985");
        await Assert.That(mods.InstalledItems[0].Mods[0].ModId).IsEqualTo("Brita_2");
        await Assert.That(mods.InstalledItems[0].Mods[0].Name).IsEqualTo("Brita's Weapon Pack");
        await Assert.That(mods.InstalledItems[1].Mods[0].Name).IsNull();
        string[] configuredIds = ["2392709985", "2335368829", "999"];
        string[] enabledIds = ["Brita_2", "Missing"];
        await Assert.That(mods.ConfiguredWorkshopIds).IsEquivalentTo(configuredIds);
        await Assert.That(mods.EnabledModIds).IsEquivalentTo(enabledIds);
        await Assert.That(mods.Findings.Count).IsEqualTo(2);
        await Assert.That(mods.Findings[0].Kind).IsEqualTo(ModCompatKind.ReferencedNotInstalled);
        await Assert.That(mods.Findings[0].Subject).IsEqualTo("999");
        await Assert.That(mods.Findings[1].Detail).IsEqualTo("no installed mod provides this id");
    }

    [Test]
    public async Task OperationCompleted_without_a_mod_result_stays_null()
    {
        Envelope<OperationCompleted> original = Envelope.Create(
            new OperationCompleted(OperationOutcome.Succeeded), At, operationId: OperationId.New());

        Envelope<OperationCompleted> back = ProtocolJson.Deserialize<OperationCompleted>(ProtocolJson.Serialize(original));

        await Assert.That(back.Payload.Mods).IsNull();
    }

    [Test]
    public async Task The_change_is_additive_so_the_protocol_version_stays_one()
    {
        await Assert.That(ProtocolVersionRange.Supported.Current).IsEqualTo(1);
    }
}
