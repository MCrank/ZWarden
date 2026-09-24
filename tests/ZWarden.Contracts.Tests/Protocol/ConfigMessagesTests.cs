using ZWarden.Contracts.Protocol;
using ZWarden.Contracts.Protocol.Messages;
using ZWarden.Domain.Configuration;
using ZWarden.Domain.Ids;

namespace ZWarden.Contracts.Tests.Protocol;

/// <summary>
/// F20b PR-3: the configuration-apply wire surface. <see cref="ConfigApply"/> is a payload-carrying leaf of the
/// closed <see cref="AgentCommand"/> vocabulary (like the F19 player commands): it names which of a Server's four
/// files to write (<see cref="PzConfigFile"/>), the drift baseline the Agent re-checks before writing (ADR 0011),
/// and the surgical value edits to apply. It declares a unique <c>configuration.*</c> discriminator; the
/// completion carries an optional <see cref="ConfigApplyResult"/> (the recorded revision's canonical snapshot +
/// hash) that is additive (ADR 0020) and round-trips through the one canonical <see cref="ProtocolJson"/>.
/// </summary>
public class ConfigMessagesTests
{
    private static readonly DateTimeOffset At = new(2026, 9, 14, 8, 30, 0, TimeSpan.Zero);

    [Test]
    public async Task ConfigApply_round_trips_its_file_baseline_and_edits()
    {
        ServerId server = ServerId.New();
        AgentCommand command = new ConfigApply(
            PzConfigFile.SandboxVars,
            BaselineHash: "abc123",
            Edits:
            [
                new ConfigValueEdit("Zombies", ConfigValueKind.Number, "3"),
                new ConfigValueEdit("Map.AllowMiniMap", ConfigValueKind.Bool, "true"),
                new ConfigValueEdit("PublicName", ConfigValueKind.Text, "My Server"),
            ]);
        Envelope<AgentCommand> original = Envelope.Create(
            command, At, agentId: AgentId.New(), serverId: server, operationId: OperationId.New());

        Envelope<IProtocolMessage> back = ProtocolJson.Deserialize(ProtocolJson.Serialize(original));

        await Assert.That(back.ServerId).IsEqualTo(server);
        ConfigApply applied = (ConfigApply)back.Payload;
        await Assert.That(applied.File).IsEqualTo(PzConfigFile.SandboxVars);
        await Assert.That(applied.BaselineHash).IsEqualTo("abc123");
        await Assert.That(applied.Edits.Count).IsEqualTo(3);
        await Assert.That(applied.Edits[0]).IsEqualTo(new ConfigValueEdit("Zombies", ConfigValueKind.Number, "3"));
        await Assert.That(applied.Edits[2].Value).IsEqualTo("My Server");
    }

    [Test]
    public async Task ConfigApply_allows_a_null_baseline_for_the_first_write()
    {
        AgentCommand command = new ConfigApply(PzConfigFile.Ini, BaselineHash: null, Edits: []);
        Envelope<AgentCommand> original = Envelope.Create(command, At, operationId: OperationId.New());

        Envelope<IProtocolMessage> back = ProtocolJson.Deserialize(ProtocolJson.Serialize(original));

        ConfigApply applied = (ConfigApply)back.Payload;
        await Assert.That(applied.BaselineHash).IsNull();
        await Assert.That(applied.Edits).IsEmpty();
    }

    [Test]
    public async Task ConfigApply_declares_the_configuration_discriminator()
    {
        ProtocolMessageAttribute? attribute = (ProtocolMessageAttribute?)Attribute.GetCustomAttribute(
            typeof(ConfigApply), typeof(ProtocolMessageAttribute));

        await Assert.That(attribute).IsNotNull();
        await Assert.That(attribute!.Discriminator).IsEqualTo("configuration.apply");
        await Assert.That(ProtocolJson.MessageTypes.ContainsKey("configuration.apply")).IsTrue();
    }

    [Test]
    public async Task OperationCompleted_round_trips_a_config_apply_result()
    {
        Envelope<OperationCompleted> original = Envelope.Create(
            new OperationCompleted(
                OperationOutcome.Succeeded,
                Config: new ConfigApplyResult(PzConfigFile.SandboxVars, "deadbeef", "[[\"Zombies\",\"n:3:i\"]]", 1)),
            At,
            serverId: ServerId.New(),
            operationId: OperationId.New());

        Envelope<OperationCompleted> back = ProtocolJson.Deserialize<OperationCompleted>(ProtocolJson.Serialize(original));

        await Assert.That(back.Payload).IsEqualTo(original.Payload);
        await Assert.That(back.Payload.Config!.File).IsEqualTo(PzConfigFile.SandboxVars);
        await Assert.That(back.Payload.Config!.SnapshotHash).IsEqualTo("deadbeef");
        await Assert.That(back.Payload.Config!.ChangedCount).IsEqualTo(1);
        await Assert.That(back.Payload.Update).IsNull();
    }

    [Test]
    public async Task OperationCompleted_without_a_config_result_stays_null()
    {
        Envelope<OperationCompleted> original = Envelope.Create(
            new OperationCompleted(OperationOutcome.Failed, "configuration on disk changed outside ZWarden"),
            At,
            operationId: OperationId.New());

        Envelope<OperationCompleted> back = ProtocolJson.Deserialize<OperationCompleted>(ProtocolJson.Serialize(original));

        await Assert.That(back.Payload.Config).IsNull();
    }

    [Test]
    public async Task OperationCompleted_round_trips_a_config_live_reload_outcome()
    {
        // #225: the Agent reports whether an INI write was made live with reloadoptions, and why not when it was not.
        Envelope<OperationCompleted> original = Envelope.Create(
            new OperationCompleted(
                OperationOutcome.Succeeded,
                Config: new ConfigApplyResult(
                    PzConfigFile.Ini, "h", "[]", 1, ConfigReloadOutcome.Failed, "RCON is disabled on this server.")),
            At,
            serverId: ServerId.New(),
            operationId: OperationId.New());

        string json = ProtocolJson.Serialize(original);
        Envelope<OperationCompleted> back = ProtocolJson.Deserialize<OperationCompleted>(json);

        await Assert.That(json).Contains("\"Failed\"");
        await Assert.That(back.Payload.Config!.Reload).IsEqualTo(ConfigReloadOutcome.Failed);
        await Assert.That(back.Payload.Config!.ReloadDetail).IsEqualTo("RCON is disabled on this server.");
    }

    [Test]
    public async Task A_config_result_from_an_agent_without_reload_reporting_reads_as_not_attempted()
    {
        // Additive (#225): a completion serialized without the reload fields still deserializes, defaulting them.
        Envelope<OperationCompleted> original = Envelope.Create(
            new OperationCompleted(
                OperationOutcome.Succeeded, Config: new ConfigApplyResult(PzConfigFile.Ini, "h", "[]", 1)),
            At,
            operationId: OperationId.New());
        string json = ProtocolJson.Serialize(original)
            .Replace(",\"reload\":\"NotAttempted\"", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace(",\"Reload\":\"NotAttempted\"", string.Empty, StringComparison.Ordinal);

        Envelope<OperationCompleted> back = ProtocolJson.Deserialize<OperationCompleted>(json);

        await Assert.That(json).DoesNotContain("NotAttempted");
        await Assert.That(back.Payload.Config!.Reload).IsEqualTo(ConfigReloadOutcome.NotAttempted);
        await Assert.That(back.Payload.Config!.ReloadDetail).IsNull();
    }
}
