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
    public async Task DiscoveredMod_round_trips_its_optional_workshop_metadata()
    {
        // #110: mod.info's version/dependency/compat fields (research §6 — pzversion, versionMin, version, require,
        // incompatible, tags) ride the same discovery result as additive-optional data, escaped only at render.
        DiscoveredMod mod = new(
            "Authentic Z - Current", "Authentic Z",
            Version: "42", PzVersion: "41", VersionMin: "41.78",
            Requires: ["RV_Interior_MP"],
            Incompatible: ["\\AuthenticZLite", "\\AuthenticZBackpacks+"],
            Tags: ["Realistic", "Overhaul"]);

        ModDiscoveryResult result = new(
            InstalledItems: [new DiscoveredWorkshopItem("2857548524", [mod])],
            ConfiguredWorkshopIds: ["2857548524"],
            EnabledModIds: ["Authentic Z - Current"],
            Findings: []);

        Envelope<OperationCompleted> back = ProtocolJson.Deserialize<OperationCompleted>(ProtocolJson.Serialize(
            Envelope.Create(new OperationCompleted(OperationOutcome.Succeeded, Mods: result), At, operationId: OperationId.New())));

        DiscoveredMod round = back.Payload.Mods!.InstalledItems[0].Mods[0];
        await Assert.That(round.Version).IsEqualTo("42");
        await Assert.That(round.PzVersion).IsEqualTo("41");
        await Assert.That(round.VersionMin).IsEqualTo("41.78");
        string[] requires = ["RV_Interior_MP"];
        string[] incompatible = ["\\AuthenticZLite", "\\AuthenticZBackpacks+"];
        string[] tags = ["Realistic", "Overhaul"];
        await Assert.That(round.Requires).IsEquivalentTo(requires);
        await Assert.That(round.Incompatible).IsEquivalentTo(incompatible);
        await Assert.That(round.Tags).IsEquivalentTo(tags);
    }

    [Test]
    public async Task A_mod_with_no_metadata_defaults_its_lists_empty_and_scalars_null()
    {
        DiscoveredMod mod = new("Plain", null);

        await Assert.That(mod.Version).IsNull();
        await Assert.That(mod.PzVersion).IsNull();
        await Assert.That(mod.VersionMin).IsNull();
        await Assert.That(mod.Requires).IsNotNull();
        await Assert.That(mod.Requires).IsEmpty();
        await Assert.That(mod.Incompatible).IsEmpty();
        await Assert.That(mod.Tags).IsEmpty();
    }

    [Test]
    public async Task OperationCompleted_round_trips_the_new_dependency_and_compat_findings()
    {
        ModDiscoveryResult result = new(
            InstalledItems: [],
            ConfiguredWorkshopIds: [],
            EnabledModIds: [],
            Findings:
            [
                new ModCompatFinding(ModCompatKind.RequiresMissing, "NeedsThis", "required by AuthenticZ but not installed"),
                new ModCompatFinding(ModCompatKind.IncompatiblePresent, "AuthenticZLite", "declared incompatible with AuthenticZ"),
            ]);

        Envelope<OperationCompleted> back = ProtocolJson.Deserialize<OperationCompleted>(ProtocolJson.Serialize(
            Envelope.Create(new OperationCompleted(OperationOutcome.Succeeded, Mods: result), At, operationId: OperationId.New())));

        IReadOnlyList<ModCompatFinding> findings = back.Payload.Mods!.Findings;
        await Assert.That(findings[0].Kind).IsEqualTo(ModCompatKind.RequiresMissing);
        await Assert.That(findings[1].Kind).IsEqualTo(ModCompatKind.IncompatiblePresent);
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
