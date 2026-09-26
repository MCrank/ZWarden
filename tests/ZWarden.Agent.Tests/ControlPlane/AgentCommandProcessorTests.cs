using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ZWarden.Agent.Backups;
using ZWarden.Agent.Configuration;
using ZWarden.Agent.Console;
using ZWarden.Agent.ControlPlane;
using ZWarden.Agent.Docker;
using ZWarden.Agent.Players;
using ZWarden.Agent.Rcon;
using ZWarden.Agent.ServerConfig;
using ZWarden.Agent.Servers;
using ZWarden.Agent.SteamCmd;
using ZWarden.Agent.Mods;
using ZWarden.Agent.Diagnostics;
using ZWarden.Agent.Tests.Console;
using ZWarden.Agent.Tests.Diagnostics;
using ZWarden.Agent.Tests.Docker;
using ZWarden.Agent.Tests.Mods;
using ZWarden.Agent.Tests.Players;
using ZWarden.Agent.Tests.Rcon;
using ZWarden.Agent.Tests.ServerConfig;
using ZWarden.Contracts.Protocol;
using ZWarden.Contracts.Protocol.Messages;
using ZWarden.Domain.Configuration;
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
        IServerHostDirectories? hostDirectories = null,
        IServerUpdateRunner? updates = null,
        IServerBackupRunner? backups = null,
        IServerRestoreRunner? restores = null,
        IRconHealthProbe? rconProbe = null,
        IRconServerConfig? rconConfig = null,
        IPlayerAdministration? players = null,
        IServerRestartCoordinator? restartCoordinator = null,
        IConsoleAdministration? console = null,
        IServerConfigWriter? configWriter = null,
        IServerConfigRawEditStaging? rawStaging = null,
        IServerConfigReloader? configReloader = null,
        IModDiscovery? modDiscovery = null,
        IHostDiagnosticsGatherer? hostDiagnostics = null,
        IServerDiagnosticsGatherer? serverDiagnostics = null,
        ILogger<AgentCommandProcessor>? logger = null)
    {
        IContainerRuntime effectiveRuntime = runtime ?? new FakeContainerRuntime();
        IOptions<AgentOptions> options = Options.Create(new AgentOptions
        {
            PzImageReference = "zwarden/pzserver:pinned",
            NetworkName = "zwarden",
            DataMountRoot = OperatingSystem.IsWindows() ? @"C:\pz" : "/pz",
        });

        // The default coordinator is the real one, wired to the shared runtime with a resolver that reports no
        // RCON (so the broadcast is skipped) — a plain RestartServer still restarts the container. A test that
        // exercises the countdown passes its own coordinator.
        IServerRestartCoordinator coordinator = restartCoordinator ?? new ServerRestartCoordinator(
            new FakeRconEndpointResolver(),
            new FakeRconConnectionFactory(new FakeRconConnection()),
            effectiveRuntime,
            options,
            NullLogger<ServerRestartCoordinator>.Instance,
            static (_, _) => Task.CompletedTask);

        // The real provisioner over the same fakes, so the provisioning tests below exercise the command end to end.
        var provisioner = new ServerProvisioner(
            effectiveRuntime,
            hostDirectories ?? new FakeServerHostDirectories(),
            rconConfig ?? new FakeRconServerConfig(),
            new FakeInitialSettingsSeeder(),
            coordinator,
            options,
            NullLogger<ServerProvisioner>.Instance);

        return new(
            TimeProvider.System,
            effectiveRuntime,
            provisioner,
            updates ?? new FakeServerUpdateRunner(),
            backups ?? new FakeServerBackupRunner(),
            restores ?? new FakeServerRestoreRunner(),
            rconProbe ?? new FakeRconHealthProbe(),
            players ?? new FakePlayerAdministration(),
            coordinator,
            console ?? new FakeConsoleAdministration(),
            configWriter ?? new FakeServerConfigWriter(),
            rawStaging ?? new ServerConfigRawEditStaging(TimeProvider.System),
            configReloader ?? new FakeServerConfigReloader(),
            modDiscovery ?? new FakeModDiscovery(),
            hostDiagnostics ?? new FakeHostDiagnosticsGatherer(),
            serverDiagnostics ?? new FakeServerDiagnosticsGatherer(),
            options,
            logger ?? NullLogger<AgentCommandProcessor>.Instance);
    }

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
    public async Task A_mod_discovery_succeeds_and_carries_the_result_on_the_same_operation()
    {
        var discovery = new FakeModDiscovery
        {
            Result = new ModDiscoveryResult(
                InstalledItems: [new DiscoveredWorkshopItem("111", [new DiscoveredMod("ModA", "Mod A")])],
                ConfiguredWorkshopIds: ["111"],
                EnabledModIds: ["ModA"],
                Findings: []),
        };
        ServerId server = ServerId.New();
        OperationId operationId = OperationId.New();

        Envelope<OperationCompleted>? reply = await Processor(modDiscovery: discovery)
            .ProcessAsync(Json(new DiscoverMods(), operationId, server), CancellationToken.None);

        await Assert.That(reply!.OperationId).IsEqualTo(operationId);
        await Assert.That(reply.ServerId).IsEqualTo(server);
        await Assert.That(reply.Payload.Outcome).IsEqualTo(OperationOutcome.Succeeded);
        await Assert.That(reply.Payload.Mods!.InstalledItems.Single().Mods.Single().ModId).IsEqualTo("ModA");
        await Assert.That(discovery.DiscoveredFor).IsEqualTo(server);
        await Assert.That(discovery.CallCount).IsEqualTo(1);
    }

    [Test]
    public async Task A_mod_discovery_without_a_target_server_fails()
    {
        Envelope<OperationCompleted>? reply = await Processor()
            .ProcessAsync(Json(new DiscoverMods(), OperationId.New()), CancellationToken.None);

        await Assert.That(reply!.Payload.Outcome).IsEqualTo(OperationOutcome.Failed);
        await Assert.That(reply.Payload.FailureReason).IsEqualTo("No target Server on the mod discovery.");
    }

    [Test]
    public async Task A_redelivered_mod_discovery_runs_once()
    {
        var discovery = new FakeModDiscovery();
        AgentCommandProcessor sut = Processor(modDiscovery: discovery);
        string json = Json(new DiscoverMods(), OperationId.New(), ServerId.New());

        Envelope<OperationCompleted>? first = await sut.ProcessAsync(json, CancellationToken.None);
        Envelope<OperationCompleted>? second = await sut.ProcessAsync(json, CancellationToken.None);

        await Assert.That(first).IsNotNull();
        await Assert.That(second).IsNull();
        await Assert.That(discovery.CallCount).IsEqualTo(1);
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
    public async Task Provisioning_ensures_both_host_bind_mount_sources_before_create()
    {
        var hostDirs = new FakeServerHostDirectories();
        ServerId server = ServerId.New();

        Envelope<OperationCompleted>? reply = await Processor(hostDirectories: hostDirs)
            .ProcessAsync(Json(new CreateServer(), OperationId.New(), server), CancellationToken.None);

        await Assert.That(reply!.Payload.Outcome).IsEqualTo(OperationOutcome.Succeeded);
        // Exactly the provisioned Server's world-data and server-install bind sources were prepared (#184):
        // the Mounts API never auto-creates them, so the daemon would otherwise refuse the create.
        await Assert.That(hostDirs.Created.Count).IsEqualTo(1);
        PzContainerSpec prepared = hostDirs.Created[0];
        await Assert.That(prepared.ServerId).IsEqualTo(server);
        await Assert.That(prepared.DataMountSource).Contains(server.ToString());
        await Assert.That(prepared.ServerMountSource).IsEqualTo(prepared.DataMountSource + ".server");
        // #198: the spec's heap is the configured heap and the limit is derived with headroom above it, so the
        // container can never be provisioned with a limit that OOM-kills the JVM on boot.
        await Assert.That(prepared.HeapSizeBytes).IsEqualTo(4L * 1024 * 1024 * 1024);
        await Assert.That(prepared.MemoryLimitBytes).IsEqualTo(10L * 1024 * 1024 * 1024);
        await Assert.That(prepared.MemoryLimitBytes > prepared.HeapSizeBytes).IsTrue();
    }

    [Test]
    public async Task A_failed_completion_is_logged_as_a_warning_with_its_reason()
    {
        // #266: a refusal the Agent completes cleanly as Failed (here the port pre-flight) used to leave no trace in the
        // Agent log — only exceptions were logged. Every Failed completion now is, whatever the command.
        var logger = new RecordingLogger<AgentCommandProcessor>();
        var runtime = new FakeContainerRuntime
        {
            ClaimException = new PortUnavailableException("Host port 16261/udp is already published by another container on this host."),
        };
        OperationId operationId = OperationId.New();
        ServerId server = ServerId.New();

        Envelope<OperationCompleted>? reply = await Processor(runtime, logger: logger)
            .ProcessAsync(Json(new CreateServer(GamePort: 16261), operationId, server), CancellationToken.None);

        await Assert.That(reply!.Payload.Outcome).IsEqualTo(OperationOutcome.Failed);
        RecordingLogger<AgentCommandProcessor>.Entry entry = logger.Entries.Single(e => e.Level == LogLevel.Warning);
        await Assert.That(entry.Message).Contains(operationId.ToString());
        await Assert.That(entry.Message).Contains(server.ToString());
        await Assert.That(entry.Message).Contains("CreateServer");
        await Assert.That(entry.Message).Contains("16261/udp is already published");
    }

    [Test]
    public async Task A_succeeded_completion_logs_no_warning()
    {
        var logger = new RecordingLogger<AgentCommandProcessor>();

        await Processor(logger: logger).ProcessAsync(Json(new PingAgent(), OperationId.New()), CancellationToken.None);

        await Assert.That(logger.Entries.Any(e => e.Level >= LogLevel.Warning)).IsFalse();
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
        // #230: the completion reports the heap the container was built with (here the Agent default), so Web records it.
        await Assert.That(reply.Payload.Provision!.HeapSizeBytes).IsEqualTo(new AgentOptions().DefaultHeapSizeBytes);
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
    public async Task Create_server_with_a_game_port_provisions_on_that_pair()
    {
        var runtime = new FakeContainerRuntime();

        Envelope<OperationCompleted>? reply = await Processor(runtime)
            .ProcessAsync(Json(new CreateServer(GamePort: 27015), OperationId.New(), ServerId.New()), CancellationToken.None);

        await Assert.That(reply!.Payload.Outcome).IsEqualTo(OperationOutcome.Succeeded);
        await Assert.That(reply.Payload.Provision!.GamePort).IsEqualTo(27015);
        await Assert.That(reply.Payload.Provision!.QueryPort).IsEqualTo(27016);
    }

    [Test]
    public async Task Recreate_server_reports_the_new_pair_and_container()
    {
        ServerId server = ServerId.New();
        string root = OperatingSystem.IsWindows() ? @"C:\pz" : "/pz";
        var runtime = new FakeContainerRuntime
        {
            CreatedContainerId = "new-id",
            ServerContainer = new ServerContainer("old-id", "exited", new PortAllocation(16261, 16262),
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["/pz/data"] = Path.Combine(root, server.ToString()),
                    ["/pz/server"] = Path.Combine(root, $"{server}.server"),
                }),
        };
        OperationId operationId = OperationId.New();

        Envelope<OperationCompleted>? reply = await Processor(runtime)
            .ProcessAsync(Json(new RecreateServer(GamePort: 27015), operationId, server), CancellationToken.None);

        await Assert.That(reply!.OperationId).IsEqualTo(operationId);
        await Assert.That(reply.ServerId).IsEqualTo(server);
        await Assert.That(reply.Payload.Outcome).IsEqualTo(OperationOutcome.Succeeded);
        await Assert.That(reply.Payload.Provision).IsEqualTo(new ProvisionResult(27015, 27016, "new-id", new AgentOptions().DefaultHeapSizeBytes));
        await Assert.That(runtime.RemovedServerId).IsEqualTo(server);
    }

    [Test]
    public async Task Recreate_server_without_a_target_server_fails()
    {
        Envelope<OperationCompleted>? reply = await Processor()
            .ProcessAsync(Json(new RecreateServer(), OperationId.New()), CancellationToken.None);

        await Assert.That(reply!.Payload.Outcome).IsEqualTo(OperationOutcome.Failed);
    }

    [Test]
    public async Task A_redelivered_recreate_server_runs_once()
    {
        var runtime = new FakeContainerRuntime();
        AgentCommandProcessor sut = Processor(runtime);
        string json = Json(new RecreateServer(), OperationId.New(), ServerId.New());

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
    public async Task Backup_server_reports_the_archive_facts_on_success()
    {
        var backups = new FakeServerBackupRunner
        {
            Outcome = new(true, "world-20260912-100000-op-x.tar.gz", 4096, "abc123", Now, null),
        };
        OperationId operationId = OperationId.New();
        ServerId serverId = ServerId.New();

        Envelope<OperationCompleted>? reply = await Processor(backups: backups)
            .ProcessAsync(Json(new BackupServer(), operationId, serverId), CancellationToken.None);

        await Assert.That(reply!.OperationId).IsEqualTo(operationId);
        await Assert.That(reply.ServerId).IsEqualTo(serverId);
        await Assert.That(reply.Payload.Outcome).IsEqualTo(OperationOutcome.Succeeded);
        await Assert.That(reply.Payload.Backup!.ArchiveName).IsEqualTo("world-20260912-100000-op-x.tar.gz");
        await Assert.That(reply.Payload.Backup!.SizeBytes).IsEqualTo(4096L);
        await Assert.That(reply.Payload.Backup!.Sha256).IsEqualTo("abc123");
        await Assert.That(backups.LastServerId).IsEqualTo(serverId);
    }

    [Test]
    public async Task Backup_server_fails_with_the_runners_reason()
    {
        var backups = new FakeServerBackupRunner { Outcome = new(false, null, 0, null, null, "The backup could not be written: disk full.") };

        Envelope<OperationCompleted>? reply = await Processor(backups: backups)
            .ProcessAsync(Json(new BackupServer(), OperationId.New(), ServerId.New()), CancellationToken.None);

        await Assert.That(reply!.Payload.Outcome).IsEqualTo(OperationOutcome.Failed);
        await Assert.That(reply.Payload.FailureReason).IsEqualTo("The backup could not be written: disk full.");
        await Assert.That(reply.Payload.Backup).IsNull();
    }

    [Test]
    public async Task Backup_server_without_a_target_server_fails_and_does_not_run()
    {
        var backups = new FakeServerBackupRunner();

        Envelope<OperationCompleted>? reply = await Processor(backups: backups)
            .ProcessAsync(Json(new BackupServer(), OperationId.New()), CancellationToken.None);

        await Assert.That(reply!.Payload.Outcome).IsEqualTo(OperationOutcome.Failed);
        await Assert.That(backups.RunCount).IsEqualTo(0);
    }

    [Test]
    public async Task A_redelivered_backup_runs_once()
    {
        var backups = new FakeServerBackupRunner();
        AgentCommandProcessor sut = Processor(backups: backups);
        string json = Json(new BackupServer(), OperationId.New(), ServerId.New());

        await sut.ProcessAsync(json, CancellationToken.None);
        Envelope<OperationCompleted>? second = await sut.ProcessAsync(json, CancellationToken.None);

        await Assert.That(second).IsNull();
        await Assert.That(backups.RunCount).IsEqualTo(1);
    }

    [Test]
    public async Task Delete_backup_signals_success_and_passes_the_archive_name()
    {
        var backups = new FakeServerBackupRunner { DeletionOutcome = new(true, null) };
        OperationId operationId = OperationId.New();
        ServerId serverId = ServerId.New();

        Envelope<OperationCompleted>? reply = await Processor(backups: backups)
            .ProcessAsync(Json(new DeleteBackup("world-1.tar.gz"), operationId, serverId), CancellationToken.None);

        await Assert.That(reply!.Payload.Outcome).IsEqualTo(OperationOutcome.Succeeded);
        await Assert.That(reply.Payload.BackupDeletion!.ArchiveName).IsEqualTo("world-1.tar.gz");
        await Assert.That(backups.LastArchiveName).IsEqualTo("world-1.tar.gz");
        await Assert.That(backups.LastServerId).IsEqualTo(serverId);
    }

    [Test]
    public async Task Delete_backup_fails_with_the_runners_reason()
    {
        var backups = new FakeServerBackupRunner { DeletionOutcome = new(false, "The backup archive name is not a bare file name.") };

        Envelope<OperationCompleted>? reply = await Processor(backups: backups)
            .ProcessAsync(Json(new DeleteBackup("../escape"), OperationId.New(), ServerId.New()), CancellationToken.None);

        await Assert.That(reply!.Payload.Outcome).IsEqualTo(OperationOutcome.Failed);
        await Assert.That(reply.Payload.FailureReason).IsEqualTo("The backup archive name is not a bare file name.");
        await Assert.That(reply.Payload.BackupDeletion).IsNull();
    }

    [Test]
    public async Task Delete_backup_without_a_target_server_fails_and_does_not_run()
    {
        var backups = new FakeServerBackupRunner();

        Envelope<OperationCompleted>? reply = await Processor(backups: backups)
            .ProcessAsync(Json(new DeleteBackup("world-1.tar.gz"), OperationId.New()), CancellationToken.None);

        await Assert.That(reply!.Payload.Outcome).IsEqualTo(OperationOutcome.Failed);
        await Assert.That(backups.DeleteCount).IsEqualTo(0);
    }

    [Test]
    public async Task Restore_server_reports_the_restored_archive_and_protective_backup_on_success()
    {
        var restores = new FakeServerRestoreRunner
        {
            Outcome = new(
                true, "world-1.tar.gz", "world-1-pre-restore.tar.gz", 8192, "def456",
                new DateTimeOffset(2026, 9, 14, 9, 59, 0, TimeSpan.Zero), null),
        };
        OperationId operationId = OperationId.New();
        ServerId serverId = ServerId.New();

        Envelope<OperationCompleted>? reply = await Processor(restores: restores)
            .ProcessAsync(Json(new RestoreServer("world-1.tar.gz", "abc123"), operationId, serverId), CancellationToken.None);

        await Assert.That(reply!.OperationId).IsEqualTo(operationId);
        await Assert.That(reply.ServerId).IsEqualTo(serverId);
        await Assert.That(reply.Payload.Outcome).IsEqualTo(OperationOutcome.Succeeded);
        await Assert.That(reply.Payload.Restore!.RestoredArchiveName).IsEqualTo("world-1.tar.gz");
        await Assert.That(reply.Payload.Restore!.ProtectiveBackup.ArchiveName).IsEqualTo("world-1-pre-restore.tar.gz");
        await Assert.That(reply.Payload.Restore!.ProtectiveBackup.Sha256).IsEqualTo("def456");
        await Assert.That(restores.LastServerId).IsEqualTo(serverId);
        await Assert.That(restores.LastArchiveName).IsEqualTo("world-1.tar.gz");
        await Assert.That(restores.LastExpectedSha256).IsEqualTo("abc123");
    }

    [Test]
    public async Task Restore_server_fails_with_the_runners_reason()
    {
        var restores = new FakeServerRestoreRunner
        {
            Outcome = new(false, null, null, 0, null, null, "The server is running. Stop the server before restoring a backup."),
        };

        Envelope<OperationCompleted>? reply = await Processor(restores: restores)
            .ProcessAsync(Json(new RestoreServer("world-1.tar.gz", "abc123"), OperationId.New(), ServerId.New()), CancellationToken.None);

        await Assert.That(reply!.Payload.Outcome).IsEqualTo(OperationOutcome.Failed);
        await Assert.That(reply.Payload.FailureReason).IsEqualTo("The server is running. Stop the server before restoring a backup.");
        await Assert.That(reply.Payload.Restore).IsNull();
    }

    [Test]
    public async Task Restore_server_without_a_target_server_fails_and_does_not_run()
    {
        var restores = new FakeServerRestoreRunner();

        Envelope<OperationCompleted>? reply = await Processor(restores: restores)
            .ProcessAsync(Json(new RestoreServer("world-1.tar.gz", "abc123"), OperationId.New()), CancellationToken.None);

        await Assert.That(reply!.Payload.Outcome).IsEqualTo(OperationOutcome.Failed);
        await Assert.That(restores.RunCount).IsEqualTo(0);
    }

    [Test]
    public async Task A_redelivered_restore_runs_once()
    {
        var restores = new FakeServerRestoreRunner();
        AgentCommandProcessor sut = Processor(restores: restores);
        string json = Json(new RestoreServer("world-1.tar.gz", "abc123"), OperationId.New(), ServerId.New());

        await sut.ProcessAsync(json, CancellationToken.None);
        Envelope<OperationCompleted>? second = await sut.ProcessAsync(json, CancellationToken.None);

        await Assert.That(second).IsNull();
        await Assert.That(restores.RunCount).IsEqualTo(1);
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

    [Test]
    public async Task A_console_command_succeeds_and_carries_the_untrusted_reply()
    {
        var console = new FakeConsoleAdministration { Result = new ConsoleCommandResult("Players connected (0): ", Truncated: false) };
        ServerId server = ServerId.New();
        OperationId operationId = OperationId.New();

        Envelope<OperationCompleted>? reply = await Processor(console: console)
            .ProcessAsync(Json(new ExecuteConsoleCommand("players"), operationId, server), CancellationToken.None);

        await Assert.That(reply!.OperationId).IsEqualTo(operationId);
        await Assert.That(reply.ServerId).IsEqualTo(server);
        await Assert.That(reply.Payload.Outcome).IsEqualTo(OperationOutcome.Succeeded);
        await Assert.That(reply.Payload.ConsoleCommand!.Output).IsEqualTo("Players connected (0): ");
        await Assert.That(console.LastInput).IsEqualTo("players");
    }

    [Test]
    public async Task A_console_command_without_a_target_server_fails()
    {
        Envelope<OperationCompleted>? reply = await Processor()
            .ProcessAsync(Json(new ExecuteConsoleCommand("players"), OperationId.New()), CancellationToken.None);

        await Assert.That(reply!.Payload.Outcome).IsEqualTo(OperationOutcome.Failed);
    }

    [Test]
    public async Task A_console_command_that_cannot_run_fails_with_the_agent_reason()
    {
        var console = new FakeConsoleAdministration { Throw = new ConsoleCommandException("RCON is disabled: no password is set in the server configuration.") };

        Envelope<OperationCompleted>? reply = await Processor(console: console)
            .ProcessAsync(Json(new ExecuteConsoleCommand("players"), OperationId.New(), ServerId.New()), CancellationToken.None);

        await Assert.That(reply!.Payload.Outcome).IsEqualTo(OperationOutcome.Failed);
        await Assert.That(reply.Payload.FailureReason).Contains("RCON is disabled");
        await Assert.That(reply.Payload.ConsoleCommand).IsNull();
    }

    [Test]
    public async Task A_redelivered_console_command_runs_once()
    {
        var console = new FakeConsoleAdministration();
        AgentCommandProcessor sut = Processor(console: console);
        string json = Json(new ExecuteConsoleCommand("save"), OperationId.New(), ServerId.New());

        await sut.ProcessAsync(json, CancellationToken.None);
        Envelope<OperationCompleted>? second = await sut.ProcessAsync(json, CancellationToken.None);

        await Assert.That(second).IsNull();
        await Assert.That(console.CallCount).IsEqualTo(1);
    }

    [Test]
    public async Task Config_apply_succeeds_and_carries_the_recorded_revision()
    {
        var writer = new FakeServerConfigWriter { Outcome = ConfigApplyOutcome.Applied("[[\"Zombies\",\"n:1:i\"]]", "hash-9", 2) };
        ServerId server = ServerId.New();
        OperationId operationId = OperationId.New();
        var command = new ConfigApply(PzConfigFile.SandboxVars, "base-1", [new ConfigValueEdit("Zombies", ConfigValueKind.Number, "1")]);

        Envelope<OperationCompleted>? reply = await Processor(configWriter: writer)
            .ProcessAsync(Json(command, operationId, server), CancellationToken.None);

        await Assert.That(reply!.OperationId).IsEqualTo(operationId);
        await Assert.That(reply.ServerId).IsEqualTo(server);
        await Assert.That(reply.Payload.Outcome).IsEqualTo(OperationOutcome.Succeeded);
        await Assert.That(reply.Payload.Config!.File).IsEqualTo(PzConfigFile.SandboxVars);
        await Assert.That(reply.Payload.Config!.SnapshotHash).IsEqualTo("hash-9");
        await Assert.That(reply.Payload.Config!.CanonicalSnapshot).IsEqualTo("[[\"Zombies\",\"n:1:i\"]]");
        await Assert.That(reply.Payload.Config!.ChangedCount).IsEqualTo(2);
        // The command's file, baseline, and edits reached the writer unchanged.
        await Assert.That(writer.LastFile).IsEqualTo(PzConfigFile.SandboxVars);
        await Assert.That(writer.LastBaselineHash).IsEqualTo("base-1");
        await Assert.That(writer.LastEdits!.Count).IsEqualTo(1);
    }

    [Test]
    public async Task An_ini_config_apply_that_changed_values_is_reloaded_live_and_reports_it()
    {
        // #225: after a successful INI write the Agent sends reloadoptions (best-effort) and reports the outcome.
        var writer = new FakeServerConfigWriter { Outcome = ConfigApplyOutcome.Applied("[[\"MaxPlayers\",\"s:20\"]]", "hash-ini", 1) };
        var reloader = new FakeServerConfigReloader();
        ServerId server = ServerId.New();
        var command = new ConfigApply(PzConfigFile.Ini, "base-1", [new ConfigValueEdit("MaxPlayers", ConfigValueKind.Number, "20")]);

        Envelope<OperationCompleted>? reply = await Processor(configWriter: writer, configReloader: reloader)
            .ProcessAsync(Json(command, OperationId.New(), server), CancellationToken.None);

        await Assert.That(reply!.Payload.Outcome).IsEqualTo(OperationOutcome.Succeeded);
        await Assert.That(reloader.LastServerId).IsEqualTo(server);
        await Assert.That(reply.Payload.Config!.Reload).IsEqualTo(ConfigReloadOutcome.Reloaded);
        // The revision is the writer's post-write snapshot, independent of the reload.
        await Assert.That(reply.Payload.Config!.SnapshotHash).IsEqualTo("hash-ini");
    }

    [Test]
    public async Task A_failed_live_reload_still_succeeds_the_write_and_carries_the_reason()
    {
        var writer = new FakeServerConfigWriter { Outcome = ConfigApplyOutcome.Applied("[]", "hash-ini", 1) };
        var reloader = new FakeServerConfigReloader { Attempt = new(ConfigReloadOutcome.Failed, "RCON is disabled on this server.") };
        var command = new ConfigApply(PzConfigFile.Ini, "base-1", [new ConfigValueEdit("MaxPlayers", ConfigValueKind.Number, "20")]);

        Envelope<OperationCompleted>? reply = await Processor(configWriter: writer, configReloader: reloader)
            .ProcessAsync(Json(command, OperationId.New(), ServerId.New()), CancellationToken.None);

        await Assert.That(reply!.Payload.Outcome).IsEqualTo(OperationOutcome.Succeeded);
        await Assert.That(reply.Payload.Config!.Reload).IsEqualTo(ConfigReloadOutcome.Failed);
        await Assert.That(reply.Payload.Config!.ReloadDetail).IsEqualTo("RCON is disabled on this server.");
    }

    [Test]
    public async Task An_interrupted_live_reload_still_reports_the_write()
    {
        var writer = new FakeServerConfigWriter { Outcome = ConfigApplyOutcome.Applied("[]", "hash-ini", 1) };
        var reloader = new FakeServerConfigReloader { Throw = new OperationCanceledException() };
        var command = new ConfigApply(PzConfigFile.Ini, "base-1", [new ConfigValueEdit("MaxPlayers", ConfigValueKind.Number, "20")]);

        Envelope<OperationCompleted>? reply = await Processor(configWriter: writer, configReloader: reloader)
            .ProcessAsync(Json(command, OperationId.New(), ServerId.New()), CancellationToken.None);

        await Assert.That(reply!.Payload.Outcome).IsEqualTo(OperationOutcome.Succeeded);
        await Assert.That(reply.Payload.Config!.Reload).IsEqualTo(ConfigReloadOutcome.Failed);
    }

    [Test]
    public async Task A_sandbox_apply_or_an_unchanged_ini_apply_is_not_reloaded()
    {
        // reloadoptions does not cover SandboxVars, and an INI write that changed nothing has nothing to reload.
        var reloader = new FakeServerConfigReloader();

        Envelope<OperationCompleted>? sandbox = await Processor(
                configWriter: new FakeServerConfigWriter { Outcome = ConfigApplyOutcome.Applied("[]", "h", 1) },
                configReloader: reloader)
            .ProcessAsync(Json(new ConfigApply(PzConfigFile.SandboxVars, "b", []), OperationId.New(), ServerId.New()), CancellationToken.None);
        Envelope<OperationCompleted>? unchanged = await Processor(
                configWriter: new FakeServerConfigWriter { Outcome = ConfigApplyOutcome.Applied("[]", "h", 0) },
                configReloader: reloader)
            .ProcessAsync(Json(new ConfigApply(PzConfigFile.Ini, "b", []), OperationId.New(), ServerId.New()), CancellationToken.None);

        await Assert.That(reloader.CallCount).IsEqualTo(0);
        await Assert.That(sandbox!.Payload.Config!.Reload).IsEqualTo(ConfigReloadOutcome.NotAttempted);
        await Assert.That(unchanged!.Payload.Config!.Reload).IsEqualTo(ConfigReloadOutcome.NotAttempted);
    }

    [Test]
    public async Task A_raw_ini_apply_is_reloaded_live()
    {
        var staging = new ServerConfigRawEditStaging(TimeProvider.System);
        foreach (ServerConfigRawEditChunk chunk in ServerConfigRawEditCodec.Encode("corr-ini", "MaxPlayers=20\n"))
        {
            staging.Accept(chunk);
        }

        var writer = new FakeServerConfigWriter { RawOutcome = ConfigApplyOutcome.Applied("[]", "hash-raw", 1) };
        var reloader = new FakeServerConfigReloader();

        Envelope<OperationCompleted>? reply = await Processor(configWriter: writer, rawStaging: staging, configReloader: reloader)
            .ProcessAsync(Json(new ConfigApplyRaw(PzConfigFile.Ini, "b", "corr-ini"), OperationId.New(), ServerId.New()), CancellationToken.None);

        await Assert.That(reloader.CallCount).IsEqualTo(1);
        await Assert.That(reply!.Payload.Config!.Reload).IsEqualTo(ConfigReloadOutcome.Reloaded);
    }

    [Test]
    public async Task Config_apply_fails_closed_on_drift_with_the_writers_reason_and_no_result()
    {
        var writer = new FakeServerConfigWriter { Outcome = ConfigApplyOutcome.DriftRefused("The configuration on disk changed outside ZWarden.") };

        Envelope<OperationCompleted>? reply = await Processor(configWriter: writer)
            .ProcessAsync(Json(new ConfigApply(PzConfigFile.Ini, "base-1", []), OperationId.New(), ServerId.New()), CancellationToken.None);

        await Assert.That(reply!.Payload.Outcome).IsEqualTo(OperationOutcome.Failed);
        await Assert.That(reply.Payload.FailureReason).Contains("changed outside ZWarden");
        await Assert.That(reply.Payload.Config).IsNull();
    }

    [Test]
    public async Task Config_apply_without_a_target_server_fails_and_does_not_write()
    {
        var writer = new FakeServerConfigWriter();

        Envelope<OperationCompleted>? reply = await Processor(configWriter: writer)
            .ProcessAsync(Json(new ConfigApply(PzConfigFile.Ini, null, []), OperationId.New()), CancellationToken.None);

        await Assert.That(reply!.Payload.Outcome).IsEqualTo(OperationOutcome.Failed);
        await Assert.That(writer.ApplyCount).IsEqualTo(0);
    }

    [Test]
    public async Task A_redelivered_config_apply_runs_once()
    {
        var writer = new FakeServerConfigWriter();
        AgentCommandProcessor sut = Processor(configWriter: writer);
        string json = Json(new ConfigApply(PzConfigFile.SandboxVars, "base-1", []), OperationId.New(), ServerId.New());

        await sut.ProcessAsync(json, CancellationToken.None);
        Envelope<OperationCompleted>? second = await sut.ProcessAsync(json, CancellationToken.None);

        await Assert.That(second).IsNull();
        await Assert.That(writer.ApplyCount).IsEqualTo(1);
    }

    [Test]
    public async Task Config_apply_raw_takes_the_staged_text_and_carries_the_recorded_revision()
    {
        // Stage the operator's whole-file text first (as the transport channel would), then run the Operation.
        var staging = new ServerConfigRawEditStaging(TimeProvider.System);
        const string rawText = "VERSION = 1,\nSandboxVars = {\n    Zombies = 2,\n}\n";
        foreach (ServerConfigRawEditChunk chunk in ServerConfigRawEditCodec.Encode("corr-raw", rawText))
        {
            staging.Accept(chunk);
        }

        var writer = new FakeServerConfigWriter { RawOutcome = ConfigApplyOutcome.Applied("[[\"Zombies\",\"n:2:i\"]]", "hash-raw", 1) };
        ServerId server = ServerId.New();
        OperationId operationId = OperationId.New();
        var command = new ConfigApplyRaw(PzConfigFile.SandboxVars, "base-1", "corr-raw");

        Envelope<OperationCompleted>? reply = await Processor(configWriter: writer, rawStaging: staging)
            .ProcessAsync(Json(command, operationId, server), CancellationToken.None);

        await Assert.That(reply!.Payload.Outcome).IsEqualTo(OperationOutcome.Succeeded);
        await Assert.That(reply.Payload.Config!.SnapshotHash).IsEqualTo("hash-raw");
        await Assert.That(reply.Payload.Config!.File).IsEqualTo(PzConfigFile.SandboxVars);
        // The reassembled text and the baseline reached the writer's raw path unchanged.
        await Assert.That(writer.ApplyRawCount).IsEqualTo(1);
        await Assert.That(writer.LastRawContent).IsEqualTo(rawText);
        await Assert.That(writer.LastBaselineHash).IsEqualTo("base-1");
        // The staged text is one-shot — a redelivery finds nothing to take.
        await Assert.That(staging.TryTake("corr-raw", out _)).IsFalse();
    }

    [Test]
    public async Task Config_apply_raw_fails_when_no_text_was_staged()
    {
        var writer = new FakeServerConfigWriter();

        Envelope<OperationCompleted>? reply = await Processor(configWriter: writer)
            .ProcessAsync(
                Json(new ConfigApplyRaw(PzConfigFile.Ini, "base-1", "corr-missing"), OperationId.New(), ServerId.New()),
                CancellationToken.None);

        await Assert.That(reply!.Payload.Outcome).IsEqualTo(OperationOutcome.Failed);
        await Assert.That(reply.Payload.FailureReason).Contains("not received");
        await Assert.That(writer.ApplyRawCount).IsEqualTo(0);
    }

    [Test]
    public async Task Config_apply_raw_without_a_target_server_fails_and_does_not_write()
    {
        var writer = new FakeServerConfigWriter();

        Envelope<OperationCompleted>? reply = await Processor(configWriter: writer)
            .ProcessAsync(Json(new ConfigApplyRaw(PzConfigFile.Ini, null, "corr-x"), OperationId.New()), CancellationToken.None);

        await Assert.That(reply!.Payload.Outcome).IsEqualTo(OperationOutcome.Failed);
        await Assert.That(writer.ApplyRawCount).IsEqualTo(0);
    }

    [Test]
    public async Task A_host_diagnostics_gather_succeeds_and_carries_the_host_bundle()
    {
        FakeHostDiagnosticsGatherer gatherer = new()
        {
            Result = new HostDiagnosticsResult([new DiagnosticCheckFact(DiagnosticDomain.Docker, ProbeStatus.Pass, "ok", null)]),
        };
        AgentCommandProcessor sut = Processor(hostDiagnostics: gatherer);

        Envelope<OperationCompleted>? reply = await sut
            .ProcessAsync(Json(new GatherHostDiagnostics(), OperationId.New()), CancellationToken.None);

        await Assert.That(reply!.Payload.Outcome).IsEqualTo(OperationOutcome.Succeeded);
        await Assert.That(reply.Payload.HostDiagnostics!.Checks.Count).IsEqualTo(1);
        await Assert.That(reply.Payload.ServerDiagnostics).IsNull();
        await Assert.That(gatherer.Calls).IsEqualTo(1);
    }

    [Test]
    public async Task A_server_diagnostics_gather_carries_the_server_bundle_and_target()
    {
        FakeServerDiagnosticsGatherer gatherer = new()
        {
            Result = new ServerDiagnosticsResult([new DiagnosticCheckFact(DiagnosticDomain.Rcon, ProbeStatus.Fail, "no rcon", "refused")]),
        };
        AgentCommandProcessor sut = Processor(serverDiagnostics: gatherer);
        ServerId server = ServerId.New();

        Envelope<OperationCompleted>? reply = await sut
            .ProcessAsync(Json(new GatherServerDiagnostics(), OperationId.New(), server), CancellationToken.None);

        await Assert.That(reply!.Payload.Outcome).IsEqualTo(OperationOutcome.Succeeded);
        await Assert.That(reply.Payload.ServerDiagnostics!.Checks[0].Domain).IsEqualTo(DiagnosticDomain.Rcon);
        await Assert.That(reply.ServerId).IsEqualTo(server);
        await Assert.That(gatherer.LastServerId).IsEqualTo(server);
    }

    [Test]
    public async Task A_server_diagnostics_gather_without_a_target_fails()
    {
        FakeServerDiagnosticsGatherer gatherer = new();
        AgentCommandProcessor sut = Processor(serverDiagnostics: gatherer);

        Envelope<OperationCompleted>? reply = await sut
            .ProcessAsync(Json(new GatherServerDiagnostics(), OperationId.New()), CancellationToken.None);

        await Assert.That(reply!.Payload.Outcome).IsEqualTo(OperationOutcome.Failed);
        await Assert.That(gatherer.Calls).IsEqualTo(0);
    }

    [Test]
    public async Task A_redelivered_host_gather_runs_once()
    {
        FakeHostDiagnosticsGatherer gatherer = new();
        AgentCommandProcessor sut = Processor(hostDiagnostics: gatherer);
        string json = Json(new GatherHostDiagnostics(), OperationId.New());

        await sut.ProcessAsync(json, CancellationToken.None);
        Envelope<OperationCompleted>? second = await sut.ProcessAsync(json, CancellationToken.None);

        await Assert.That(second).IsNull();
        await Assert.That(gatherer.Calls).IsEqualTo(1);
    }
}
