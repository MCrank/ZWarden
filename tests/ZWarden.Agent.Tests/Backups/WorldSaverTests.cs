using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ZWarden.Agent.Backups;
using ZWarden.Agent.Configuration;
using ZWarden.Agent.Docker;
using ZWarden.Agent.Rcon;
using ZWarden.Agent.Tests.Players;
using ZWarden.Agent.Tests.Rcon;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Security;
using ZWarden.Rcon;

namespace ZWarden.Agent.Tests.Backups;

/// <summary>
/// #377: before a running Server is archived, the Agent asks PZ to save its world over RCON. PZ only queues the save
/// when it replies, so the saver then waits a fixed settle before the archive is written. A stopped Server needs no
/// save; any reason RCON can't take the command is reported (never thrown) so the backup can still run with a
/// warning. The connection is always disposed (cap-slot safety).
/// </summary>
public class WorldSaverTests
{
    private static readonly RconEndpoint AnyEndpoint = new("172.20.0.5", 27015, new SecretString("pw"));

    [Test]
    public async Task A_running_server_is_sent_save_and_the_settle_is_waited()
    {
        ServerId serverId = ServerId.New();
        var connection = new ScriptedRconConnection("World saved");
        List<TimeSpan> delays = [];
        WorldSaver saver = Saver(Running(serverId), RconResolveResult.Resolved(AnyEndpoint), connection, delays);

        WorldSaveResult result = await saver.SaveAsync(serverId, CancellationToken.None);

        await Assert.That(result.Status).IsEqualTo(WorldSaveStatus.Saved);
        await Assert.That(connection.LastCommand).IsEqualTo("save");
        await Assert.That(delays).IsEquivalentTo(new[] { TimeSpan.FromSeconds(7) });
        await Assert.That(connection.DisposeCount).IsEqualTo(1);
    }

    [Test]
    public async Task A_stopped_server_is_not_saved()
    {
        ServerId serverId = ServerId.New();
        var connection = new ScriptedRconConnection();
        List<TimeSpan> delays = [];
        var runtime = new StubContainerRuntime(new ManagedContainer("c1", serverId, "exited"));
        WorldSaver saver = Saver(runtime, RconResolveResult.Resolved(AnyEndpoint), connection, delays);

        WorldSaveResult result = await saver.SaveAsync(serverId, CancellationToken.None);

        await Assert.That(result.Status).IsEqualTo(WorldSaveStatus.NotRunning);
        await Assert.That(connection.LastCommand).IsNull();
        await Assert.That(delays).IsEmpty();
    }

    [Test]
    public async Task A_server_with_no_container_is_not_saved()
    {
        var connection = new ScriptedRconConnection();
        WorldSaver saver = Saver(new StubContainerRuntime(), RconResolveResult.Resolved(AnyEndpoint), connection, []);

        WorldSaveResult result = await saver.SaveAsync(ServerId.New(), CancellationToken.None);

        await Assert.That(result.Status).IsEqualTo(WorldSaveStatus.NotRunning);
        await Assert.That(connection.LastCommand).IsNull();
    }

    [Test]
    public async Task RCON_disabled_is_a_failed_save_with_a_reason()
    {
        ServerId serverId = ServerId.New();
        var connection = new ScriptedRconConnection();
        WorldSaver saver = Saver(Running(serverId), RconResolveResult.RconDisabled, connection, []);

        WorldSaveResult result = await saver.SaveAsync(serverId, CancellationToken.None);

        await Assert.That(result.Status).IsEqualTo(WorldSaveStatus.Failed);
        await Assert.That(result.FailureReason!).Contains("RCON is disabled");
        await Assert.That(connection.LastCommand).IsNull();
    }

    [Test]
    public async Task A_running_container_with_no_address_is_a_failed_save()
    {
        ServerId serverId = ServerId.New();
        WorldSaver saver = Saver(Running(serverId), RconResolveResult.NoContainer, new ScriptedRconConnection(), []);

        WorldSaveResult result = await saver.SaveAsync(serverId, CancellationToken.None);

        await Assert.That(result.Status).IsEqualTo(WorldSaveStatus.Failed);
        await Assert.That(result.FailureReason!).Contains("could not be reached");
    }

    [Test]
    [Arguments("auth")]
    [Arguments("timeout")]
    [Arguments("transport")]
    public async Task An_RCON_fault_is_a_failed_save_and_the_settle_is_skipped(string fault)
    {
        ServerId serverId = ServerId.New();
        Exception thrown = fault switch
        {
            "auth" => new RconAuthenticationException("rejected"),
            "timeout" => new RconTimeoutException("slow"),
            _ => new RconException("refused"),
        };
        var connection = new ScriptedRconConnection(executeThrow: thrown);
        List<TimeSpan> delays = [];
        WorldSaver saver = Saver(Running(serverId), RconResolveResult.Resolved(AnyEndpoint), connection, delays);

        WorldSaveResult result = await saver.SaveAsync(serverId, CancellationToken.None);

        await Assert.That(result.Status).IsEqualTo(WorldSaveStatus.Failed);
        await Assert.That(result.FailureReason).IsNotNull();
        await Assert.That(delays).IsEmpty();
        await Assert.That(connection.DisposeCount).IsEqualTo(1);
    }

    private static StubContainerRuntime Running(ServerId serverId) =>
        new(new ManagedContainer("c1", serverId, "running"));

    private static WorldSaver Saver(
        IContainerRuntime runtime, RconResolveResult resolution, ScriptedRconConnection connection, List<TimeSpan> delays) =>
        new(
            runtime,
            new FakeRconEndpointResolver { Result = resolution },
            new ScriptedRconConnectionFactory(connection),
            Options.Create(new AgentOptions { BackupSaveSettle = TimeSpan.FromSeconds(7) }),
            NullLogger<WorldSaver>.Instance,
            (delay, _) =>
            {
                delays.Add(delay);
                return Task.CompletedTask;
            });
}
