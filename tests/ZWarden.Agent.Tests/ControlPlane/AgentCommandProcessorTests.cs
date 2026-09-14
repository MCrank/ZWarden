using Microsoft.Extensions.Options;
using ZWarden.Agent.Configuration;
using ZWarden.Agent.ControlPlane;
using ZWarden.Agent.Docker;
using ZWarden.Agent.Players;
using ZWarden.Agent.Rcon;
using ZWarden.Agent.SteamCmd;
using ZWarden.Agent.Tests.Docker;
using ZWarden.Agent.Tests.Players;
using ZWarden.Agent.Tests.Rcon;
using ZWarden.Contracts.Protocol;
using ZWarden.Contracts.Protocol.Messages;
using ZWarden.Domain.Ids;

namespace ZWarden.Agent.Tests.ControlPlane;

/// <summary>
/// The <see cref="AgentCommandProcessor"/> — the Agent's handling of a dispatched command. A
/// <c>Diagnostics.Ping</c> (F11) completes successfully; a <c>Diagnostics.DockerHealth</c> (F13) reports the
/// Docker probe result on the same operation; a redelivered command is deduped so the work runs once (PRD 20);
/// a command with no operation id, or one this Agent version does not understand, is ignored.
/// </summary>
public class AgentCommandProcessorTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 12, 10, 0, 0, TimeSpan.Zero);

    private static AgentCommandProcessor Processor(
        IContainerRuntime? runtime = null,
        IServerUpdateRunner? updates = null,
        IRconHealthProbe? rconProbe = null,
        IRconServerConfig? rconConfig = null,
        IPlayerAdministration? players = null) =>
        new(
            TimeProvider.System,
            runtime ?? new FakeContainerRuntime(),
            updates ?? new FakeServerUpdateRunner(),
            rconProbe ?? new FakeRconHealthProbe(),
            rconConfig ?? new FakeRconServerConfig(),
            players ?? new FakePlayerAdministration(),
            Options.Create(new AgentOptions
            {
                PzImageReference = "zwarden/pzserver:pinned",
                NetworkName = "zwarden",
                DataMountRoot = OperatingSystem.IsWindows() ? @"C:\pz" : "/pz",
                DefaultMemoryLimitBytes = 4L * 1024 * 1024 * 1024,
            }));

    private static string Json(AgentCommand command, OperationId? operationId = null, ServerId? serverId = null)
        => ProtocolJson.Serialize(operationId is { } op
            ? Envelope.Create(command, Now, serverId: serverId, operationId: op)
            : Envelope.Create(command, Now));

    [Test]
    public async Task A_ping_completes_successfully_on_the_same_operation()
    {
        OperationId operationId = OperationId.New();

        Envelope<OperationCompleted>? reply = await Processor().ProcessAsync(Json(new PingAgent(), operationId), CancellationToken.None);

        await Assert.That(reply).IsNotNull();
        await Assert.That(reply!.OperationId).IsEqualTo(operationId);
        await Assert.That(reply.Payload.Outcome).IsEqualTo(OperationOutcome.Succeeded);
    }

    [Test]
    public async Task A_docker_health_probe_succeeds_when_the_daemon_is_reachable()
    {
        var runtime = new FakeContainerRuntime { Health = new DockerHealth(true, "1.53", null) };
        OperationId operationId = OperationId.New();

        Envelope<OperationCompleted>? reply = await Processor(runtime)
            .ProcessAsync(Json(new ProbeDockerHealth(), operationId), CancellationToken.None);

        await Assert.That(reply).IsNotNull();
        await Assert.That(reply!.OperationId).IsEqualTo(operationId);
        await Assert.That(reply.Payload.Outcome).IsEqualTo(OperationOutcome.Succeeded);
        await Assert.That(runtime.ProbeCount).IsEqualTo(1);
    }

    [Test]
    public async Task A_docker_health_probe_fails_with_a_reason_when_the_daemon_is_unreachable()
    {
        var runtime = new FakeContainerRuntime { Health = new DockerHealth(false, null, "The Docker daemon is not reachable.") };

        Envelope<OperationCompleted>? reply = await Processor(runtime)
            .ProcessAsync(Json(new ProbeDockerHealth(), OperationId.New()), CancellationToken.None);

        await Assert.That(reply!.Payload.Outcome).IsEqualTo(OperationOutcome.Failed);
        await Assert.That(reply.Payload.FailureReason).IsEqualTo("The Docker daemon is not reachable.");
    }

    [Test]
    public async Task A_redelivered_command_is_deduped()
    {
        AgentCommandProcessor sut = Processor();
        string json = Json(new PingAgent(), OperationId.New());

        Envelope<OperationCompleted>? first = await sut.ProcessAsync(json, CancellationToken.None);
        Envelope<OperationCompleted>? second = await sut.ProcessAsync(json, CancellationToken.None);

        await Assert.That(first).IsNotNull();
        await Assert.That(second).IsNull();
    }

    [Test]
    public async Task A_redelivered_docker_health_probe_runs_once()
    {
        var runtime = new FakeContainerRuntime();
        AgentCommandProcessor sut = Processor(runtime);
        string json = Json(new ProbeDockerHealth(), OperationId.New());

        await sut.ProcessAsync(json, CancellationToken.None);
        Envelope<OperationCompleted>? second = await sut.ProcessAsync(json, CancellationToken.None);

        await Assert.That(second).IsNull();
        await Assert.That(runtime.ProbeCount).IsEqualTo(1);
    }

    [Test]
    public async Task An_rcon_health_probe_succeeds_and_carries_the_result_when_authenticated()
    {
        var probe = new FakeRconHealthProbe { Result = new RconHealthResult(true, true, null) };
        ServerId server = ServerId.New();
        OperationId operationId = OperationId.New();

        Envelope<OperationCompleted>? reply = await Processor(rconProbe: probe)
            .ProcessAsync(Json(new ProbeRconHealth(), operationId, server), CancellationToken.None);

        await Assert.That(reply!.OperationId).IsEqualTo(operationId);
        await Assert.That(reply.ServerId).IsEqualTo(server);
        await Assert.That(reply.Payload.Outcome).IsEqualTo(OperationOutcome.Succeeded);
        await Assert.That(reply.Payload.Rcon!.Authenticated).IsTrue();
        await Assert.That(probe.ProbeCount).IsEqualTo(1);
        await Assert.That(probe.LastServerId).IsEqualTo(server);
    }

    [Test]
    public async Task An_rcon_health_probe_fails_with_the_detail_when_not_authenticated()
    {
        var probe = new FakeRconHealthProbe
        {
            Result = new RconHealthResult(false, false, "RCON is disabled: no password is set in the server configuration."),
        };

        Envelope<OperationCompleted>? reply = await Processor(rconProbe: probe)
            .ProcessAsync(Json(new ProbeRconHealth(), OperationId.New(), ServerId.New()), CancellationToken.None);

        await Assert.That(reply!.Payload.Outcome).IsEqualTo(OperationOutcome.Failed);
        await Assert.That(reply.Payload.FailureReason).IsEqualTo("RCON is disabled: no password is set in the server configuration.");
        await Assert.That(reply.Payload.Rcon!.Reachable).IsFalse();
    }

    [Test]
    public async Task An_rcon_health_probe_without_a_target_server_fails()
    {
        Envelope<OperationCompleted>? reply = await Processor()
            .ProcessAsync(Json(new ProbeRconHealth(), OperationId.New()), CancellationToken.None);

        await Assert.That(reply!.Payload.Outcome).IsEqualTo(OperationOutcome.Failed);
        await Assert.That(reply.Payload.FailureReason).IsEqualTo("No target Server on the RCON health probe.");
    }

    [Test]
    public async Task A_redelivered_rcon_health_probe_runs_once()
    {
        var probe = new FakeRconHealthProbe();
        AgentCommandProcessor sut = Processor(rconProbe: probe);
        string json = Json(new ProbeRconHealth(), OperationId.New(), ServerId.New());

        await sut.ProcessAsync(json, CancellationToken.None);
        Envelope<OperationCompleted>? second = await sut.ProcessAsync(json, CancellationToken.None);

        await Assert.That(second).IsNull();
        await Assert.That(probe.ProbeCount).IsEqualTo(1);
    }

    [Test]
    public async Task Provisioning_seeds_rcon_into_the_server_config_before_launch()
    {
        var config = new FakeRconServerConfig();
        ServerId server = ServerId.New();

        Envelope<OperationCompleted>? reply = await Processor(rconConfig: config)
            .ProcessAsync(Json(new CreateServer(), OperationId.New(), server), CancellationToken.None);

        await Assert.That(reply!.Payload.Outcome).IsEqualTo(OperationOutcome.Succeeded);
        await Assert.That(config.EnabledServers).Contains(server);
    }

    [Test]
    public async Task Distinct_operations_are_each_handled()
    {
        AgentCommandProcessor sut = Processor();

        Envelope<OperationCompleted>? a = await sut.ProcessAsync(Json(new PingAgent(), OperationId.New()), CancellationToken.None);
        Envelope<OperationCompleted>? b = await sut.ProcessAsync(Json(new PingAgent(), OperationId.New()), CancellationToken.None);

        await Assert.That(a).IsNotNull();
        await Assert.That(b).IsNotNull();
    }

    [Test]
    public async Task A_command_without_an_operation_id_is_ignored()
    {
        await Assert.That(await Processor().ProcessAsync(Json(new PingAgent()), CancellationToken.None)).IsNull();
    }

    [Test]
    public async Task Create_server_provisions_and_reports_the_allocated_ports_and_container_id()
    {
        var runtime = new FakeContainerRuntime { NextPorts = new(16265, 16266), CreatedContainerId = "c-1" };
        OperationId operationId = OperationId.New();
        ServerId serverId = ServerId.New();

        Envelope<OperationCompleted>? reply = await Processor(runtime)
            .ProcessAsync(Json(new CreateServer(), operationId, serverId), CancellationToken.None);

        await Assert.That(reply).IsNotNull();
        await Assert.That(reply!.OperationId).IsEqualTo(operationId);
        await Assert.That(reply.ServerId).IsEqualTo(serverId);
        await Assert.That(reply.Payload.Outcome).IsEqualTo(OperationOutcome.Succeeded);
        await Assert.That(reply.Payload.Provision!.GamePort).IsEqualTo(16265);
        await Assert.That(reply.Payload.Provision!.QueryPort).IsEqualTo(16266);
        await Assert.That(reply.Payload.Provision!.ContainerId).IsEqualTo("c-1");
        // The container was created for this Server and then started.
        await Assert.That(runtime.LastSpec!.ServerId).IsEqualTo(serverId);
        await Assert.That(runtime.StartedContainerId).IsEqualTo("c-1");
    }

    [Test]
    public async Task Create_server_fails_with_the_reason_when_the_image_is_not_provisioned()
    {
        var runtime = new FakeContainerRuntime
        {
            CreateException = new ContainerCreateException(
                ContainerCreateFailure.ImageNotProvisioned, "Pre-provision the canonical PZ image."),
        };

        Envelope<OperationCompleted>? reply = await Processor(runtime)
            .ProcessAsync(Json(new CreateServer(), OperationId.New(), ServerId.New()), CancellationToken.None);

        await Assert.That(reply!.Payload.Outcome).IsEqualTo(OperationOutcome.Failed);
        await Assert.That(reply.Payload.FailureReason).IsEqualTo("Pre-provision the canonical PZ image.");
        await Assert.That(reply.Payload.Provision).IsNull();
    }

    [Test]
    public async Task Create_server_without_a_target_server_fails()
    {
        Envelope<OperationCompleted>? reply = await Processor()
            .ProcessAsync(Json(new CreateServer(), OperationId.New()), CancellationToken.None);

        await Assert.That(reply!.Payload.Outcome).IsEqualTo(OperationOutcome.Failed);
    }

    [Test]
    public async Task A_redelivered_create_server_runs_once()
    {
        var runtime = new FakeContainerRuntime();
        AgentCommandProcessor sut = Processor(runtime);
        string json = Json(new CreateServer(), OperationId.New(), ServerId.New());

        await sut.ProcessAsync(json, CancellationToken.None);
        Envelope<OperationCompleted>? second = await sut.ProcessAsync(json, CancellationToken.None);

        await Assert.That(second).IsNull();
        await Assert.That(runtime.CreateCount).IsEqualTo(1);
    }

    [Test]
    public async Task Start_server_resolves_the_target_and_completes_successfully()
    {
        var runtime = new FakeContainerRuntime();
        OperationId operationId = OperationId.New();
        ServerId serverId = ServerId.New();

        Envelope<OperationCompleted>? reply = await Processor(runtime)
            .ProcessAsync(Json(new StartServer(), operationId, serverId), CancellationToken.None);

        await Assert.That(reply!.OperationId).IsEqualTo(operationId);
        await Assert.That(reply.ServerId).IsEqualTo(serverId);
        await Assert.That(reply.Payload.Outcome).IsEqualTo(OperationOutcome.Succeeded);
        await Assert.That(runtime.StartedServerId).IsEqualTo(serverId);
    }

    [Test]
    public async Task Stop_and_restart_server_call_their_own_verbs()
    {
        var runtime = new FakeContainerRuntime();
        ServerId serverId = ServerId.New();

        Envelope<OperationCompleted>? stop = await Processor(runtime)
            .ProcessAsync(Json(new StopServer(), OperationId.New(), serverId), CancellationToken.None);
        await Assert.That(stop!.Payload.Outcome).IsEqualTo(OperationOutcome.Succeeded);
        await Assert.That(runtime.StoppedServerId).IsEqualTo(serverId);

        var restartRuntime = new FakeContainerRuntime();
        Envelope<OperationCompleted>? restart = await Processor(restartRuntime)
            .ProcessAsync(Json(new RestartServer(), OperationId.New(), serverId), CancellationToken.None);
        await Assert.That(restart!.Payload.Outcome).IsEqualTo(OperationOutcome.Succeeded);
        await Assert.That(restartRuntime.RestartedServerId).IsEqualTo(serverId);
    }

    [Test]
    public async Task A_lifecycle_command_without_a_target_server_fails()
    {
        Envelope<OperationCompleted>? reply = await Processor()
            .ProcessAsync(Json(new StartServer(), OperationId.New()), CancellationToken.None);

        await Assert.That(reply!.Payload.Outcome).IsEqualTo(OperationOutcome.Failed);
    }

    [Test]
    public async Task A_lifecycle_command_fails_with_an_actionable_reason_when_no_container_exists()
    {
        ServerId serverId = ServerId.New();
        var runtime = new FakeContainerRuntime { LifecycleException = new ContainerNotFoundException(serverId) };

        Envelope<OperationCompleted>? reply = await Processor(runtime)
            .ProcessAsync(Json(new StartServer(), OperationId.New(), serverId), CancellationToken.None);

        await Assert.That(reply!.Payload.Outcome).IsEqualTo(OperationOutcome.Failed);
        await Assert.That(reply.Payload.FailureReason).Contains("Provision");
    }

    [Test]
    public async Task A_lifecycle_command_fails_when_the_container_is_foreign()
    {
        ServerId serverId = ServerId.New();
        var runtime = new FakeContainerRuntime
        {
            LifecycleException = new ForeignContainerException("c-9", "assigned to a different agent"),
        };

        Envelope<OperationCompleted>? reply = await Processor(runtime)
            .ProcessAsync(Json(new StopServer(), OperationId.New(), serverId), CancellationToken.None);

        await Assert.That(reply!.Payload.Outcome).IsEqualTo(OperationOutcome.Failed);
    }

    [Test]
    public async Task A_redelivered_lifecycle_command_runs_once()
    {
        var runtime = new FakeContainerRuntime();
        AgentCommandProcessor sut = Processor(runtime);
        string json = Json(new StartServer(), OperationId.New(), ServerId.New());

        await sut.ProcessAsync(json, CancellationToken.None);
        Envelope<OperationCompleted>? second = await sut.ProcessAsync(json, CancellationToken.None);

        await Assert.That(second).IsNull();
        await Assert.That(runtime.StartServerCount).IsEqualTo(1);
    }

    [Test]
    public async Task Update_server_reports_the_installed_build_id_on_success()
    {
        var updates = new FakeServerUpdateRunner { Outcome = new(true, "24909836", null) };
        OperationId operationId = OperationId.New();
        ServerId serverId = ServerId.New();

        Envelope<OperationCompleted>? reply = await Processor(updates: updates)
            .ProcessAsync(Json(new UpdateServer(), operationId, serverId), CancellationToken.None);

        await Assert.That(reply!.OperationId).IsEqualTo(operationId);
        await Assert.That(reply.ServerId).IsEqualTo(serverId);
        await Assert.That(reply.Payload.Outcome).IsEqualTo(OperationOutcome.Succeeded);
        await Assert.That(reply.Payload.Update!.InstalledBuildId).IsEqualTo("24909836");
        await Assert.That(updates.LastServerId).IsEqualTo(serverId);
    }

    [Test]
    public async Task Update_server_fails_with_the_runners_reason()
    {
        var updates = new FakeServerUpdateRunner { Outcome = new(false, null, "Error! App '380870' state is 0x202.") };

        Envelope<OperationCompleted>? reply = await Processor(updates: updates)
            .ProcessAsync(Json(new UpdateServer(), OperationId.New(), ServerId.New()), CancellationToken.None);

        await Assert.That(reply!.Payload.Outcome).IsEqualTo(OperationOutcome.Failed);
        await Assert.That(reply.Payload.FailureReason).IsEqualTo("Error! App '380870' state is 0x202.");
        await Assert.That(reply.Payload.Update).IsNull();
    }

    [Test]
    public async Task Update_server_without_a_target_server_fails_and_does_not_run()
    {
        var updates = new FakeServerUpdateRunner();

        Envelope<OperationCompleted>? reply = await Processor(updates: updates)
            .ProcessAsync(Json(new UpdateServer(), OperationId.New()), CancellationToken.None);

        await Assert.That(reply!.Payload.Outcome).IsEqualTo(OperationOutcome.Failed);
        await Assert.That(updates.RunCount).IsEqualTo(0);
    }

    [Test]
    public async Task A_redelivered_update_runs_once()
    {
        var updates = new FakeServerUpdateRunner();
        AgentCommandProcessor sut = Processor(updates: updates);
        string json = Json(new UpdateServer(), OperationId.New(), ServerId.New());

        await sut.ProcessAsync(json, CancellationToken.None);
        Envelope<OperationCompleted>? second = await sut.ProcessAsync(json, CancellationToken.None);

        await Assert.That(second).IsNull();
        await Assert.That(updates.RunCount).IsEqualTo(1);
    }

    [Test]
    public async Task List_players_succeeds_and_carries_the_roster()
    {
        var players = new FakePlayerAdministration { Roster = new PlayerRosterResult(2, ["Bob", "Alice"]) };
        ServerId server = ServerId.New();
        OperationId operationId = OperationId.New();

        Envelope<OperationCompleted>? reply = await Processor(players: players)
            .ProcessAsync(Json(new ListPlayers(), operationId, server), CancellationToken.None);

        await Assert.That(reply!.OperationId).IsEqualTo(operationId);
        await Assert.That(reply.ServerId).IsEqualTo(server);
        await Assert.That(reply.Payload.Outcome).IsEqualTo(OperationOutcome.Succeeded);
        await Assert.That(reply.Payload.Roster!.Count).IsEqualTo(2);
        await Assert.That(players.LastServerId).IsEqualTo(server);
    }

    [Test]
    public async Task List_players_without_a_target_server_fails()
    {
        Envelope<OperationCompleted>? reply = await Processor()
            .ProcessAsync(Json(new ListPlayers(), OperationId.New()), CancellationToken.None);

        await Assert.That(reply!.Payload.Outcome).IsEqualTo(OperationOutcome.Failed);
    }

    [Test]
    public async Task List_players_fails_with_the_reason_when_rcon_is_unavailable()
    {
        var players = new FakePlayerAdministration { Throw = new PlayerCommandException("RCON is disabled: no password is set in the server configuration.") };

        Envelope<OperationCompleted>? reply = await Processor(players: players)
            .ProcessAsync(Json(new ListPlayers(), OperationId.New(), ServerId.New()), CancellationToken.None);

        await Assert.That(reply!.Payload.Outcome).IsEqualTo(OperationOutcome.Failed);
        await Assert.That(reply.Payload.FailureReason).Contains("RCON is disabled");
        await Assert.That(reply.Payload.Roster).IsNull();
    }

    [Test]
    public async Task Kick_applies_and_carries_the_action_result()
    {
        var players = new FakePlayerAdministration { ActionResult = new PlayerActionResult(PlayerActionOutcome.Applied, "User Bob kicked.") };
        ServerId server = ServerId.New();

        Envelope<OperationCompleted>? reply = await Processor(players: players)
            .ProcessAsync(Json(new KickPlayer("Bob", "griefing"), OperationId.New(), server), CancellationToken.None);

        await Assert.That(reply!.Payload.Outcome).IsEqualTo(OperationOutcome.Succeeded);
        await Assert.That(reply.Payload.PlayerAction!.Outcome).IsEqualTo(PlayerActionOutcome.Applied);
        await Assert.That(players.LastUsername).IsEqualTo("Bob");
        await Assert.That(players.LastReason).IsEqualTo("griefing");
    }

    [Test]
    public async Task Kick_of_a_missing_user_fails_but_still_carries_the_action_result()
    {
        var players = new FakePlayerAdministration { ActionResult = new PlayerActionResult(PlayerActionOutcome.NotFound, "User Bob doesn't exist.") };

        Envelope<OperationCompleted>? reply = await Processor(players: players)
            .ProcessAsync(Json(new KickPlayer("Bob"), OperationId.New(), ServerId.New()), CancellationToken.None);

        await Assert.That(reply!.Payload.Outcome).IsEqualTo(OperationOutcome.Failed);
        await Assert.That(reply.Payload.FailureReason).IsEqualTo("User Bob doesn't exist.");
        await Assert.That(reply.Payload.PlayerAction!.Outcome).IsEqualTo(PlayerActionOutcome.NotFound);
    }

    [Test]
    public async Task Ban_routes_to_the_ban_runner_and_carries_the_result()
    {
        var players = new FakePlayerAdministration();
        ServerId server = ServerId.New();

        Envelope<OperationCompleted>? reply = await Processor(players: players)
            .ProcessAsync(Json(new BanPlayer("Mallory", "cheating"), OperationId.New(), server), CancellationToken.None);

        await Assert.That(reply!.Payload.Outcome).IsEqualTo(OperationOutcome.Succeeded);
        await Assert.That(reply.Payload.PlayerAction).IsNotNull();
        await Assert.That(players.LastUsername).IsEqualTo("Mallory");
        await Assert.That(players.LastReason).IsEqualTo("cheating");
    }

    [Test]
    public async Task Set_whitelist_mode_routes_the_open_flag_to_the_runner()
    {
        var players = new FakePlayerAdministration();

        Envelope<OperationCompleted>? reply = await Processor(players: players)
            .ProcessAsync(Json(new SetWhitelistMode(false), OperationId.New(), ServerId.New()), CancellationToken.None);

        await Assert.That(reply!.Payload.Outcome).IsEqualTo(OperationOutcome.Succeeded);
        await Assert.That(players.LastOpen!.Value).IsFalse();
    }

    [Test]
    public async Task A_player_action_without_a_target_server_fails()
    {
        Envelope<OperationCompleted>? reply = await Processor()
            .ProcessAsync(Json(new UnbanPlayer("Bob"), OperationId.New()), CancellationToken.None);

        await Assert.That(reply!.Payload.Outcome).IsEqualTo(OperationOutcome.Failed);
    }

    [Test]
    public async Task A_player_action_that_cannot_run_fails_with_the_agent_reason()
    {
        var players = new FakePlayerAdministration { Throw = new PlayerCommandException("The server's RCON port could not be reached (refused, or the connection cap is full).") };

        Envelope<OperationCompleted>? reply = await Processor(players: players)
            .ProcessAsync(Json(new RemoveFromWhitelist("Bob"), OperationId.New(), ServerId.New()), CancellationToken.None);

        await Assert.That(reply!.Payload.Outcome).IsEqualTo(OperationOutcome.Failed);
        await Assert.That(reply.Payload.FailureReason).Contains("could not be reached");
    }

    [Test]
    public async Task A_redelivered_player_action_runs_once()
    {
        var players = new FakePlayerAdministration();
        AgentCommandProcessor sut = Processor(players: players);
        string json = Json(new KickPlayer("Bob"), OperationId.New(), ServerId.New());

        await sut.ProcessAsync(json, CancellationToken.None);
        Envelope<OperationCompleted>? second = await sut.ProcessAsync(json, CancellationToken.None);

        await Assert.That(second).IsNull();
        await Assert.That(players.CallCount).IsEqualTo(1);
    }
}
