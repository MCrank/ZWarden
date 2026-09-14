using ZWarden.Agent.Players;
using ZWarden.Agent.Rcon;
using ZWarden.Agent.Tests.Rcon;
using ZWarden.Contracts.Protocol.Messages;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Security;
using ZWarden.Rcon;

namespace ZWarden.Agent.Tests.Players;

/// <summary>
/// F19: the player-administration runner resolves the Server's private RCON endpoint, runs the built command, and
/// parses the reply — and turns every reason it could not run (RCON disabled, no container, password rejected,
/// timeout, refused) into a legible <see cref="PlayerCommandException"/>. The connection is always disposed
/// (cap-slot safety), and an invalid argument is refused before a connection is even opened.
/// </summary>
public class PlayerAdministrationTests
{
    private static readonly RconEndpoint AnyEndpoint = new("172.20.0.5", 27015, new SecretString("pw"));

    private static (PlayerAdministration Runner, ScriptedRconConnection Connection, ScriptedRconConnectionFactory Factory) Build(
        RconResolveResult resolution, string reply = "", Exception? executeThrow = null)
    {
        var connection = new ScriptedRconConnection(reply, executeThrow);
        var factory = new ScriptedRconConnectionFactory(connection);
        var resolver = new FakeRconEndpointResolver { Result = resolution };
        return (new PlayerAdministration(resolver, factory), connection, factory);
    }

    [Test]
    public async Task ListPlayers_runs_the_players_command_and_parses_the_roster()
    {
        (PlayerAdministration runner, ScriptedRconConnection connection, _) =
            Build(RconResolveResult.Resolved(AnyEndpoint), "Players connected (1): \n-Bob\n");

        PlayerRosterResult roster = await runner.ListPlayersAsync(ServerId.New(), CancellationToken.None);

        await Assert.That(connection.LastCommand).IsEqualTo("players");
        await Assert.That(roster.Players).Contains("Bob");
        await Assert.That(connection.DisposeCount).IsEqualTo(1);
    }

    [Test]
    public async Task Kick_runs_the_quoted_command_and_parses_the_outcome()
    {
        (PlayerAdministration runner, ScriptedRconConnection connection, _) =
            Build(RconResolveResult.Resolved(AnyEndpoint), "User Bob kicked.");

        PlayerActionResult result = await runner.KickAsync(ServerId.New(), "Bob", "griefing", CancellationToken.None);

        await Assert.That(connection.LastCommand).IsEqualTo("kickuser \"Bob\" -r \"griefing\"");
        await Assert.That(result.Outcome).IsEqualTo(PlayerActionOutcome.Applied);
        await Assert.That(connection.DisposeCount).IsEqualTo(1);
    }

    [Test]
    public async Task SetWhitelistMode_runs_the_changeoption_command()
    {
        (PlayerAdministration runner, ScriptedRconConnection connection, _) =
            Build(RconResolveResult.Resolved(AnyEndpoint), "Option : Open is now : false");

        PlayerActionResult result = await runner.SetWhitelistModeAsync(ServerId.New(), open: false, CancellationToken.None);

        await Assert.That(connection.LastCommand).IsEqualTo("changeoption Open false");
        await Assert.That(result.Outcome).IsEqualTo(PlayerActionOutcome.Applied);
    }

    [Test]
    public async Task A_disabled_rcon_raises_a_player_command_exception_without_connecting()
    {
        (PlayerAdministration runner, _, ScriptedRconConnectionFactory factory) = Build(RconResolveResult.RconDisabled);

        await Assert.That(async () => await runner.KickAsync(ServerId.New(), "Bob", null, CancellationToken.None))
            .Throws<PlayerCommandException>();
        await Assert.That(factory.CreateCount).IsEqualTo(0);
    }

    [Test]
    public async Task No_container_raises_a_player_command_exception()
    {
        (PlayerAdministration runner, _, _) = Build(RconResolveResult.NoContainer);

        await Assert.That(async () => await runner.BanAsync(ServerId.New(), "Bob", null, CancellationToken.None))
            .Throws<PlayerCommandException>();
    }

    [Test]
    public async Task A_rejected_password_raises_a_player_command_exception_and_still_disposes()
    {
        (PlayerAdministration runner, ScriptedRconConnection connection, _) =
            Build(RconResolveResult.Resolved(AnyEndpoint), executeThrow: new RconAuthenticationException("rejected"));

        await Assert.That(async () => await runner.UnbanAsync(ServerId.New(), "Bob", CancellationToken.None))
            .Throws<PlayerCommandException>();
        await Assert.That(connection.DisposeCount).IsEqualTo(1);
    }

    [Test]
    public async Task A_timeout_raises_a_player_command_exception()
    {
        (PlayerAdministration runner, ScriptedRconConnection connection, _) =
            Build(RconResolveResult.Resolved(AnyEndpoint), executeThrow: new RconTimeoutException("timed out"));

        await Assert.That(async () => await runner.KickAsync(ServerId.New(), "Bob", null, CancellationToken.None))
            .Throws<PlayerCommandException>();
        await Assert.That(connection.DisposeCount).IsEqualTo(1);
    }

    [Test]
    public async Task A_refused_or_capped_connection_raises_a_player_command_exception()
    {
        (PlayerAdministration runner, _, _) =
            Build(RconResolveResult.Resolved(AnyEndpoint), executeThrow: new RconException("refused"));

        await Assert.That(async () => await runner.RemoveFromWhitelistAsync(ServerId.New(), "Bob", CancellationToken.None))
            .Throws<PlayerCommandException>();
    }

    [Test]
    public async Task An_invalid_username_is_refused_before_the_endpoint_is_resolved()
    {
        (PlayerAdministration runner, _, ScriptedRconConnectionFactory factory) = Build(RconResolveResult.Resolved(AnyEndpoint));

        await Assert.That(async () => await runner.KickAsync(ServerId.New(), "Bob\" -r \"x", null, CancellationToken.None))
            .Throws<PlayerCommandException>();
        await Assert.That(factory.CreateCount).IsEqualTo(0);
    }
}
