using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ZWarden.Agent.Configuration;
using ZWarden.Agent.ControlPlane;
using ZWarden.Agent.Rcon;
using ZWarden.Agent.Servers;
using ZWarden.Agent.Tests.Docker;
using ZWarden.Agent.Tests.Rcon;
using ZWarden.Contracts.Protocol.Messages;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Security;
using ZWarden.Rcon;

namespace ZWarden.Agent.Tests.Servers;

/// <summary>
/// #114: the graceful-restart coordinator broadcasts a <c>servermsg</c> countdown to players over the Agent-owned
/// RCON connection, then runs the F15 safe restart. The broadcast is best-effort — an RCON failure or an
/// unreachable server never blocks the restart — and cancelling during the countdown aborts before any stop.
/// </summary>
public class ServerRestartCoordinatorTests
{
    private static readonly ServerId Server = ServerId.New();
    private static readonly OperationId Operation = OperationId.New();

    [Test]
    public async Task It_broadcasts_the_countdown_in_descending_order_then_restarts()
    {
        var connection = new RecordingRconConnection();
        var runtime = new FakeContainerRuntime();
        var delays = new List<TimeSpan>();
        var coordinator = Build(Reachable(), connection, runtime, delays, [300, 60, 30, 10]);

        await coordinator.RestartAsync(Server, plan: null, Operation, NullOperationProgressReporter.Instance, CancellationToken.None);

        await Assert.That(string.Join("\n", connection.Commands)).IsEqualTo(string.Join("\n",
            "servermsg \"Server restarting in 5 minutes.\"",
            "servermsg \"Server restarting in 1 minute.\"",
            "servermsg \"Server restarting in 30 seconds.\"",
            "servermsg \"Server restarting in 10 seconds.\""));
        await Assert.That(runtime.RestartedServerId).IsEqualTo(Server);
        // The whole countdown lasts the first lead-time (300s), regardless of how it is chunked for heartbeats.
        await Assert.That(delays.Sum(d => d.TotalSeconds)).IsEqualTo(300d);
        await Assert.That(connection.DisposeCount).IsEqualTo(4);
    }

    [Test]
    public async Task A_plan_reason_is_appended_to_each_notice()
    {
        var connection = new RecordingRconConnection();
        var coordinator = Build(Reachable(), connection, new FakeContainerRuntime(), [], [60, 10]);
        var plan = new GracefulRestartPlan([60, 10], "Applying mod changes.");

        await coordinator.WarnAsync(Server, plan, Operation, NullOperationProgressReporter.Instance, CancellationToken.None);

        await Assert.That(string.Join("\n", connection.Commands)).IsEqualTo(string.Join("\n",
            "servermsg \"Server restarting in 1 minute. Applying mod changes.\"",
            "servermsg \"Server restarting in 10 seconds. Applying mod changes.\""));
    }

    [Test]
    public async Task An_rcon_failure_never_blocks_the_restart()
    {
        var connection = new RecordingRconConnection { Throw = new RconTimeoutException("timed out") };
        var runtime = new FakeContainerRuntime();
        var coordinator = Build(Reachable(), connection, runtime, [], [60, 10]);

        await coordinator.RestartAsync(Server, plan: null, Operation, NullOperationProgressReporter.Instance, CancellationToken.None);

        // Every send threw, but the countdown ran and the restart still happened.
        await Assert.That(connection.Attempts).IsEqualTo(2);
        await Assert.That(runtime.RestartedServerId).IsEqualTo(Server);
    }

    [Test]
    public async Task An_unreachable_server_skips_the_countdown_and_restarts_immediately()
    {
        var connection = new RecordingRconConnection();
        var runtime = new FakeContainerRuntime();
        var delays = new List<TimeSpan>();
        var coordinator = Build(RconResolveResult.NoContainer, connection, runtime, delays, [300, 60, 30, 10]);

        await coordinator.RestartAsync(Server, plan: null, Operation, NullOperationProgressReporter.Instance, CancellationToken.None);

        await Assert.That(connection.Attempts).IsEqualTo(0);
        await Assert.That(delays).IsEmpty();      // no waiting — "never blocks the restart"
        await Assert.That(runtime.RestartedServerId).IsEqualTo(Server);
    }

    [Test]
    public async Task An_empty_schedule_skips_the_broadcast()
    {
        var connection = new RecordingRconConnection();
        var runtime = new FakeContainerRuntime();
        var coordinator = Build(Reachable(), connection, runtime, [], [300, 60, 30, 10]);

        await coordinator.RestartAsync(Server, new GracefulRestartPlan([]), Operation, NullOperationProgressReporter.Instance, CancellationToken.None);

        await Assert.That(connection.Attempts).IsEqualTo(0);
        await Assert.That(runtime.RestartedServerId).IsEqualTo(Server);
    }

    [Test]
    public async Task Cancelling_during_the_countdown_aborts_before_the_restart()
    {
        var connection = new RecordingRconConnection();
        var runtime = new FakeContainerRuntime();
        using var cts = new CancellationTokenSource();

        // A delay that cancels the operation the first time the coordinator waits, then honours the token.
        var coordinator = new ServerRestartCoordinator(
            Resolver(Reachable()),
            new SingleConnectionFactory(connection),
            runtime,
            Options.Create(BaseOptions([300, 60, 30, 10])),
            NullLogger<ServerRestartCoordinator>.Instance,
            (_, ct) =>
            {
                cts.Cancel();
                ct.ThrowIfCancellationRequested();
                return Task.CompletedTask;
            });

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            coordinator.RestartAsync(Server, plan: null, Operation, NullOperationProgressReporter.Instance, cts.Token));

        await Assert.That(connection.Attempts).IsEqualTo(1);        // first broadcast fired
        await Assert.That(runtime.RestartedServerId).IsNull();      // the restart never happened
    }

    private static RconResolveResult Reachable()
        => RconResolveResult.Resolved(new RconEndpoint("10.0.0.5", 27015, new SecretString("pw")));

    private static FakeRconEndpointResolver Resolver(RconResolveResult result)
        => new() { Result = result };

    private static AgentOptions BaseOptions(IReadOnlyList<int> leads) => new()
    {
        PzImageReference = "zwarden/pzserver:pinned",
        NetworkName = "zwarden",
        DataMountRoot = OperatingSystem.IsWindows() ? @"C:\pz" : "/pz",
        RestartWarningLeadSeconds = leads,
        HeartbeatInterval = TimeSpan.FromSeconds(30),
    };

    private static ServerRestartCoordinator Build(
        RconResolveResult resolution,
        RecordingRconConnection connection,
        FakeContainerRuntime runtime,
        List<TimeSpan> delays,
        IReadOnlyList<int> defaultLeads)
        => new(
            Resolver(resolution),
            new SingleConnectionFactory(connection),
            runtime,
            Options.Create(BaseOptions(defaultLeads)),
            NullLogger<ServerRestartCoordinator>.Instance,
            (delay, _) =>
            {
                delays.Add(delay);
                return Task.CompletedTask;
            });

    /// <summary>A fake <see cref="IRconConnection"/> that records the commands sent (or throws a preset RCON
    /// exception on send) and counts disposals.</summary>
    private sealed class RecordingRconConnection : IRconConnection
    {
        public List<string> Commands { get; } = [];

        public int Attempts { get; private set; }

        public int DisposeCount { get; private set; }

        public Exception? Throw { get; set; }

        public bool IsConnected => true;

        public Task ConnectAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<string> ExecuteAsync(string command, CancellationToken cancellationToken = default)
        {
            Attempts++;
            if (Throw is not null)
            {
                return Task.FromException<string>(Throw);
            }

            Commands.Add(command);
            return Task.FromResult("Message sent.");
        }

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class SingleConnectionFactory : IRconConnectionFactory
    {
        private readonly IRconConnection _connection;

        public SingleConnectionFactory(IRconConnection connection) => _connection = connection;

        public IRconConnection Create(RconEndpoint endpoint) => _connection;
    }
}
