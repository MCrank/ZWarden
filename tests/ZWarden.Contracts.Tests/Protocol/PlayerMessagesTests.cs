using ZWarden.Contracts.Protocol;
using ZWarden.Contracts.Protocol.Messages;
using ZWarden.Domain.Ids;

namespace ZWarden.Contracts.Tests.Protocol;

/// <summary>
/// F19: the player-management wire surface. The five action commands and the enumeration are leaves of the
/// closed <see cref="AgentCommand"/> vocabulary carrying a <c>players.*</c> discriminator; the payload-bearing
/// ones (<see cref="KickPlayer"/> etc.) are the first commands with a payload, so they must round-trip their
/// arguments through the one canonical <see cref="ProtocolJson"/>. The completion carries an optional
/// <see cref="PlayerRosterResult"/> or <see cref="PlayerActionResult"/> that is additive (ADR 0020), so
/// <see cref="ProtocolVersion.Current"/> stays 1.
/// </summary>
public class PlayerMessagesTests
{
    private static readonly DateTimeOffset At = new(2026, 9, 13, 9, 0, 0, TimeSpan.Zero);

    private static Envelope<IProtocolMessage> RoundTrip(AgentCommand command, ServerId server) =>
        ProtocolJson.Deserialize(ProtocolJson.Serialize(
            Envelope.Create(command, At, agentId: AgentId.New(), serverId: server, operationId: OperationId.New())));

    [Test]
    public async Task ListPlayers_round_trips_with_the_target_server_on_the_envelope()
    {
        ServerId server = ServerId.New();
        AgentCommand command = new ListPlayers();

        Envelope<IProtocolMessage> back = RoundTrip(command, server);

        await Assert.That(back.ServerId).IsEqualTo(server);
        await Assert.That(back.Payload).IsEqualTo((IProtocolMessage)command);
    }

    [Test]
    public async Task KickPlayer_round_trips_its_username_and_reason()
    {
        ServerId server = ServerId.New();

        Envelope<IProtocolMessage> back = RoundTrip(new KickPlayer("Bob", "griefing"), server);

        await Assert.That(back.Payload).IsTypeOf<KickPlayer>();
        KickPlayer kick = (KickPlayer)back.Payload;
        await Assert.That(kick.Username).IsEqualTo("Bob");
        await Assert.That(kick.Reason).IsEqualTo("griefing");
    }

    [Test]
    public async Task KickPlayer_round_trips_with_no_reason()
    {
        Envelope<IProtocolMessage> back = RoundTrip(new KickPlayer("Bob"), ServerId.New());

        KickPlayer kick = (KickPlayer)back.Payload;
        await Assert.That(kick.Reason).IsNull();
    }

    [Test]
    public async Task BanPlayer_round_trips_its_username_and_reason()
    {
        Envelope<IProtocolMessage> back = RoundTrip(new BanPlayer("Mallory", "cheating"), ServerId.New());

        BanPlayer ban = (BanPlayer)back.Payload;
        await Assert.That(ban.Username).IsEqualTo("Mallory");
        await Assert.That(ban.Reason).IsEqualTo("cheating");
    }

    [Test]
    public async Task UnbanPlayer_round_trips_its_username()
    {
        Envelope<IProtocolMessage> back = RoundTrip(new UnbanPlayer("Mallory"), ServerId.New());

        await Assert.That(((UnbanPlayer)back.Payload).Username).IsEqualTo("Mallory");
    }

    [Test]
    public async Task RemoveFromWhitelist_round_trips_its_username()
    {
        Envelope<IProtocolMessage> back = RoundTrip(new RemoveFromWhitelist("Alice"), ServerId.New());

        await Assert.That(((RemoveFromWhitelist)back.Payload).Username).IsEqualTo("Alice");
    }

    [Test]
    public async Task SetWhitelistMode_round_trips_the_open_flag()
    {
        await Assert.That(((SetWhitelistMode)RoundTrip(new SetWhitelistMode(false), ServerId.New()).Payload).Open).IsFalse();
        await Assert.That(((SetWhitelistMode)RoundTrip(new SetWhitelistMode(true), ServerId.New()).Payload).Open).IsTrue();
    }

    [Test]
    public async Task Every_player_command_declares_its_registered_discriminator()
    {
        (Type Type, string Discriminator)[] expected =
        [
            (typeof(ListPlayers), "players.list"),
            (typeof(KickPlayer), "players.kick"),
            (typeof(BanPlayer), "players.ban"),
            (typeof(UnbanPlayer), "players.unban"),
            (typeof(RemoveFromWhitelist), "players.remove-from-whitelist"),
            (typeof(SetWhitelistMode), "players.set-whitelist-mode"),
        ];

        foreach ((Type type, string discriminator) in expected)
        {
            ProtocolMessageAttribute? attribute =
                (ProtocolMessageAttribute?)Attribute.GetCustomAttribute(type, typeof(ProtocolMessageAttribute));
            await Assert.That(attribute).IsNotNull();
            await Assert.That(attribute!.Discriminator).IsEqualTo(discriminator);
            await Assert.That(ProtocolJson.MessageTypes.ContainsKey(discriminator)).IsTrue();
        }
    }

    [Test]
    public async Task OperationCompleted_round_trips_a_player_roster()
    {
        string[] roster = ["Bob", "Alice"];
        Envelope<OperationCompleted> original = Envelope.Create(
            new OperationCompleted(OperationOutcome.Succeeded, Roster: new PlayerRosterResult(2, roster)),
            At,
            serverId: ServerId.New(),
            operationId: OperationId.New());

        Envelope<OperationCompleted> back = ProtocolJson.Deserialize<OperationCompleted>(ProtocolJson.Serialize(original));

        await Assert.That(back.Payload.Roster!.Count).IsEqualTo(2);
        await Assert.That(back.Payload.Roster!.Players).IsEquivalentTo(roster);
        await Assert.That(back.Payload.PlayerAction).IsNull();
    }

    [Test]
    public async Task OperationCompleted_round_trips_a_player_action_result()
    {
        Envelope<OperationCompleted> original = Envelope.Create(
            new OperationCompleted(
                OperationOutcome.Succeeded,
                PlayerAction: new PlayerActionResult(PlayerActionOutcome.NotFound, "User Bob doesn't exist.")),
            At,
            serverId: ServerId.New(),
            operationId: OperationId.New());

        Envelope<OperationCompleted> back = ProtocolJson.Deserialize<OperationCompleted>(ProtocolJson.Serialize(original));

        await Assert.That(back.Payload.PlayerAction!.Outcome).IsEqualTo(PlayerActionOutcome.NotFound);
        await Assert.That(back.Payload.PlayerAction!.Detail).IsEqualTo("User Bob doesn't exist.");
        await Assert.That(back.Payload.Roster).IsNull();
    }

    [Test]
    public async Task OperationCompleted_without_a_player_result_stays_null()
    {
        Envelope<OperationCompleted> original = Envelope.Create(
            new OperationCompleted(OperationOutcome.Succeeded), At, operationId: OperationId.New());

        Envelope<OperationCompleted> back = ProtocolJson.Deserialize<OperationCompleted>(ProtocolJson.Serialize(original));

        await Assert.That(back.Payload.Roster).IsNull();
        await Assert.That(back.Payload.PlayerAction).IsNull();
    }

    [Test]
    public async Task The_change_is_additive_so_the_protocol_version_stays_one()
    {
        await Assert.That(ProtocolVersionRange.Supported.Current).IsEqualTo(1);
    }
}
