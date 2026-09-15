using ZWarden.Agent.Console;
using ZWarden.Agent.Rcon;
using ZWarden.Agent.Tests.Players;
using ZWarden.Agent.Tests.Rcon;
using ZWarden.Contracts.Protocol.Messages;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Security;
using ZWarden.Rcon;

namespace ZWarden.Agent.Tests.Console;

/// <summary>
/// F28: the console runner resolves the Server's private RCON endpoint, sends the operator's line as-is (ADR 0026
/// — quoting is the operator's job), and returns the untrusted reply bounded to the output cap. It re-checks the
/// F28 input-safety + command policy (ADR 0032) before opening a connection (defence in depth), and turns every
/// reason it could not run — RCON disabled, no container, password rejected, timeout, refused — into a legible
/// <see cref="ConsoleCommandException"/>. The connection is always disposed (cap-slot safety).
/// </summary>
public class ConsoleAdministrationTests
{
    private static readonly RconEndpoint AnyEndpoint = new("172.20.0.5", 27015, new SecretString("pw"));

    private static (ConsoleAdministration Runner, ScriptedRconConnection Connection, ScriptedRconConnectionFactory Factory) Build(
        RconResolveResult resolution, string reply = "", Exception? executeThrow = null)
    {
        var connection = new ScriptedRconConnection(reply, executeThrow);
        var factory = new ScriptedRconConnectionFactory(connection);
        var resolver = new FakeRconEndpointResolver { Result = resolution };
        return (new ConsoleAdministration(resolver, factory), connection, factory);
    }

    [Test]
    public async Task A_command_is_sent_verbatim_and_the_reply_is_returned()
    {
        (ConsoleAdministration runner, ScriptedRconConnection connection, _) =
            Build(RconResolveResult.Resolved(AnyEndpoint), "Players connected (1): \n-Bob\n");

        ConsoleCommandResult result = await runner.ExecuteAsync(ServerId.New(), "players", CancellationToken.None);

        await Assert.That(connection.LastCommand).IsEqualTo("players");
        await Assert.That(result.Output).IsEqualTo("Players connected (1): \n-Bob\n");
        await Assert.That(result.Truncated).IsFalse();
        await Assert.That(connection.DisposeCount).IsEqualTo(1);
    }

    [Test]
    public async Task An_empty_reply_is_a_valid_empty_result()
    {
        (ConsoleAdministration runner, _, _) = Build(RconResolveResult.Resolved(AnyEndpoint), reply: "");

        ConsoleCommandResult result = await runner.ExecuteAsync(ServerId.New(), "save", CancellationToken.None);

        await Assert.That(result.Output).IsEqualTo(string.Empty);
        await Assert.That(result.Truncated).IsFalse();
    }

    [Test]
    public async Task A_reply_over_the_cap_is_truncated_and_flagged()
    {
        string huge = new('x', ConsoleAdministration.MaxOutputLength + 500);
        (ConsoleAdministration runner, _, _) = Build(RconResolveResult.Resolved(AnyEndpoint), reply: huge);

        ConsoleCommandResult result = await runner.ExecuteAsync(ServerId.New(), "help", CancellationToken.None);

        await Assert.That(result.Truncated).IsTrue();
        await Assert.That(result.Output.Length).IsEqualTo(ConsoleAdministration.MaxOutputLength);
    }

    [Test]
    public async Task An_unsafe_command_is_refused_before_the_endpoint_is_resolved()
    {
        (ConsoleAdministration runner, _, ScriptedRconConnectionFactory factory) = Build(RconResolveResult.Resolved(AnyEndpoint));

        await Assert.That(async () => await runner.ExecuteAsync(ServerId.New(), "save\nquit", CancellationToken.None))
            .Throws<ConsoleCommandException>();
        await Assert.That(factory.CreateCount).IsEqualTo(0);
    }

    [Test]
    public async Task A_denied_command_is_refused_before_the_endpoint_is_resolved()
    {
        (ConsoleAdministration runner, _, ScriptedRconConnectionFactory factory) = Build(RconResolveResult.Resolved(AnyEndpoint));

        await Assert.That(async () => await runner.ExecuteAsync(ServerId.New(), "setpassword \"bob\" \"pw\"", CancellationToken.None))
            .Throws<ConsoleCommandException>();
        await Assert.That(factory.CreateCount).IsEqualTo(0);
    }

    [Test]
    public async Task A_disabled_rcon_raises_a_console_command_exception_without_connecting()
    {
        (ConsoleAdministration runner, _, ScriptedRconConnectionFactory factory) = Build(RconResolveResult.RconDisabled);

        await Assert.That(async () => await runner.ExecuteAsync(ServerId.New(), "players", CancellationToken.None))
            .Throws<ConsoleCommandException>();
        await Assert.That(factory.CreateCount).IsEqualTo(0);
    }

    [Test]
    public async Task No_container_raises_a_console_command_exception()
    {
        (ConsoleAdministration runner, _, _) = Build(RconResolveResult.NoContainer);

        await Assert.That(async () => await runner.ExecuteAsync(ServerId.New(), "players", CancellationToken.None))
            .Throws<ConsoleCommandException>();
    }

    [Test]
    public async Task A_rejected_password_raises_a_console_command_exception_and_still_disposes()
    {
        (ConsoleAdministration runner, ScriptedRconConnection connection, _) =
            Build(RconResolveResult.Resolved(AnyEndpoint), executeThrow: new RconAuthenticationException("rejected"));

        await Assert.That(async () => await runner.ExecuteAsync(ServerId.New(), "players", CancellationToken.None))
            .Throws<ConsoleCommandException>();
        await Assert.That(connection.DisposeCount).IsEqualTo(1);
    }

    [Test]
    public async Task A_timeout_raises_a_console_command_exception()
    {
        (ConsoleAdministration runner, ScriptedRconConnection connection, _) =
            Build(RconResolveResult.Resolved(AnyEndpoint), executeThrow: new RconTimeoutException("timed out"));

        await Assert.That(async () => await runner.ExecuteAsync(ServerId.New(), "players", CancellationToken.None))
            .Throws<ConsoleCommandException>();
        await Assert.That(connection.DisposeCount).IsEqualTo(1);
    }

    [Test]
    public async Task A_refused_or_capped_connection_raises_a_console_command_exception()
    {
        (ConsoleAdministration runner, _, _) =
            Build(RconResolveResult.Resolved(AnyEndpoint), executeThrow: new RconException("refused"));

        await Assert.That(async () => await runner.ExecuteAsync(ServerId.New(), "players", CancellationToken.None))
            .Throws<ConsoleCommandException>();
    }
}
