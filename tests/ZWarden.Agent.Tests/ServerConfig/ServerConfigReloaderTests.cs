using Microsoft.Extensions.Logging.Abstractions;
using ZWarden.Agent.Rcon;
using ZWarden.Agent.ServerConfig;
using ZWarden.Agent.Tests.Rcon;
using ZWarden.Contracts.Protocol.Messages;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Security;
using ZWarden.Rcon;

namespace ZWarden.Agent.Tests.ServerConfig;

/// <summary>
/// #225: the live INI reload sends RCON <c>reloadoptions</c> to a running server and reports what happened. It is
/// best-effort — an RCON problem is an outcome, never an exception — and always closes the connection.
/// </summary>
public class ServerConfigReloaderTests
{
    private static readonly ServerId Server = ServerId.New();

    [Test]
    public async Task It_sends_reloadoptions_and_reports_reloaded_on_pzs_confirmation()
    {
        var connection = new ScriptedConnection { Reply = "Options reloaded" };

        ConfigReloadAttempt attempt = await Reloader(Reachable(), connection).ReloadAsync(Server, CancellationToken.None);

        await Assert.That(attempt.Outcome).IsEqualTo(ConfigReloadOutcome.Reloaded);
        await Assert.That(attempt.Detail).IsNull();
        await Assert.That(string.Join(",", connection.Commands)).IsEqualTo("reloadoptions");
        await Assert.That(connection.DisposeCount).IsEqualTo(1);
    }

    [Test]
    public async Task A_stopped_server_is_not_running_and_nothing_is_sent()
    {
        var connection = new ScriptedConnection();

        ConfigReloadAttempt attempt = await Reloader(RconResolveResult.NoContainer, connection).ReloadAsync(Server, CancellationToken.None);

        await Assert.That(attempt.Outcome).IsEqualTo(ConfigReloadOutcome.NotRunning);
        await Assert.That(connection.Commands).IsEmpty();
    }

    [Test]
    public async Task Disabled_rcon_is_a_failed_reload_with_the_reason()
    {
        ConfigReloadAttempt attempt = await Reloader(RconResolveResult.RconDisabled, new ScriptedConnection())
            .ReloadAsync(Server, CancellationToken.None);

        await Assert.That(attempt.Outcome).IsEqualTo(ConfigReloadOutcome.Failed);
        await Assert.That(attempt.Detail!).Contains("RCON is disabled");
    }

    [Test]
    public async Task An_rcon_error_is_a_failed_reload_not_an_exception_and_the_connection_is_closed()
    {
        var connection = new ScriptedConnection { Throw = new RconTimeoutException("RCON timed out.") };

        ConfigReloadAttempt attempt = await Reloader(Reachable(), connection).ReloadAsync(Server, CancellationToken.None);

        await Assert.That(attempt.Outcome).IsEqualTo(ConfigReloadOutcome.Failed);
        await Assert.That(attempt.Detail).IsEqualTo("RCON timed out.");
        await Assert.That(connection.DisposeCount).IsEqualTo(1);
    }

    [Test]
    public async Task An_unexpected_reply_is_a_failed_reload_carrying_the_bounded_reply()
    {
        var connection = new ScriptedConnection { Reply = "Unknown command " + new string('x', 500) };

        ConfigReloadAttempt attempt = await Reloader(Reachable(), connection).ReloadAsync(Server, CancellationToken.None);

        await Assert.That(attempt.Outcome).IsEqualTo(ConfigReloadOutcome.Failed);
        await Assert.That(attempt.Detail!).StartsWith("The server replied: Unknown command");
        await Assert.That(attempt.Detail!.Length).IsLessThan(260);
    }

    private static ServerConfigReloader Reloader(RconResolveResult resolution, ScriptedConnection connection) =>
        new(new FakeRconEndpointResolver { Result = resolution }, new SingleConnectionFactory(connection),
            NullLogger<ServerConfigReloader>.Instance);

    private static RconResolveResult Reachable()
        => RconResolveResult.Resolved(new RconEndpoint("10.0.0.5", 27015, new SecretString("pw")));

    private sealed class ScriptedConnection : IRconConnection
    {
        public List<string> Commands { get; } = [];

        public string Reply { get; set; } = string.Empty;

        public Exception? Throw { get; set; }

        public int DisposeCount { get; private set; }

        public bool IsConnected => true;

        public Task ConnectAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<string> ExecuteAsync(string command, CancellationToken cancellationToken = default)
        {
            if (Throw is not null)
            {
                return Task.FromException<string>(Throw);
            }

            Commands.Add(command);
            return Task.FromResult(Reply);
        }

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class SingleConnectionFactory(IRconConnection connection) : IRconConnectionFactory
    {
        public IRconConnection Create(RconEndpoint endpoint) => connection;
    }
}
