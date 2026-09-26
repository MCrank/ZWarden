using System.Net;
using Docker.DotNet;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ZWarden.Agent.Configuration;
using ZWarden.Agent.ControlPlane;
using ZWarden.Agent.Docker;
using ZWarden.Agent.Servers;
using ZWarden.Agent.Tests.ControlPlane;
using ZWarden.Agent.Tests.Docker;
using ZWarden.Agent.Tests.Rcon;
using ZWarden.Agent.Tests.ServerConfig;
using ZWarden.Contracts.Protocol.Messages;
using ZWarden.Domain.Ids;

namespace ZWarden.Agent.Tests.Servers;

/// <summary>
/// #229: provisioning with an operator-chosen host port, and the Recreate primitive — (warn + safe stop if running)
/// → remove → create from the same closed template → start (if it was running) — with the fail-closed mount check,
/// the port pre-flight, Docker's start as the authority for a port clash, and rollback to the previous ports.
/// </summary>
public class ServerProvisionerTests
{
    private static readonly string Root = OperatingSystem.IsWindows() ? @"C:\pz" : "/pz";

    private static readonly PortAllocation OldPorts = new(16261, 16262);

    private static ServerProvisioner Provisioner(
        FakeContainerRuntime runtime,
        FakeServerRestartCoordinator? coordinator = null,
        FakeServerHostDirectories? hostDirectories = null,
        FakeRconServerConfig? rconConfig = null,
        FakeInitialSettingsSeeder? seeder = null) =>
        new(
            runtime,
            hostDirectories ?? new FakeServerHostDirectories(),
            rconConfig ?? new FakeRconServerConfig(),
            seeder ?? new FakeInitialSettingsSeeder(),
            coordinator ?? new FakeServerRestartCoordinator(),
            Options.Create(new AgentOptions { PzImageReference = "zwarden/pzserver:pinned", DataMountRoot = Root }),
            NullLogger<ServerProvisioner>.Instance);

    private static ServerContainer Existing(ServerId server, string state = "running", PortAllocation? ports = null, long? heap = null) =>
        new("old-id", state, ports ?? OldPorts, new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["/pz/data"] = Path.Combine(Root, server.ToString()),
            ["/pz/server"] = Path.Combine(Root, $"{server}.server"),
        }, heap);

    private static DockerApiException PortClash(int port) => new(
        HttpStatusCode.InternalServerError,
        $"{{\"message\":\"driver failed programming external connectivity on endpoint x: Bind for 0.0.0.0:{port} failed: port is already allocated\"}}");

    private static Task<ServerProvisionOutcome> Recreate(
        ServerProvisioner sut, ServerId server, int? gamePort, GracefulRestartPlan? plan = null, long? heap = null) =>
        sut.RecreateAsync(
            server, new RecreateServer(gamePort, plan, heap), OperationId.New(), NullOperationProgressReporter.Instance, CancellationToken.None);

    // --- Provisioning ------------------------------------------------------------------------------------------

    [Test]
    public async Task Provisioning_with_a_requested_port_claims_that_pair_instead_of_the_next_stride()
    {
        var runtime = new FakeContainerRuntime { CreatedContainerId = "c-1" };
        ServerId server = ServerId.New();

        ServerProvisionOutcome outcome = await Provisioner(runtime).ProvisionAsync(server, new CreateServer(27015), CancellationToken.None);

        await Assert.That(outcome.Succeeded).IsTrue();
        await Assert.That(outcome.Ports).IsEqualTo(new PortAllocation(27015, 27016));
        await Assert.That(runtime.LastSpec!.Ports).IsEqualTo(new PortAllocation(27015, 27016));
        await Assert.That(runtime.Calls).DoesNotContain("allocate");
    }

    [Test]
    public async Task Provisioning_without_a_port_allocates_the_next_stride()
    {
        var runtime = new FakeContainerRuntime { NextPorts = new(16263, 16264) };

        ServerProvisionOutcome outcome = await Provisioner(runtime).ProvisionAsync(ServerId.New(), new CreateServer(null), CancellationToken.None);

        await Assert.That(outcome.Ports).IsEqualTo(new PortAllocation(16263, 16264));
        await Assert.That(runtime.Calls[0]).IsEqualTo("allocate");
    }

    [Test]
    public async Task Provisioning_on_an_unavailable_port_fails_before_creating_anything()
    {
        var runtime = new FakeContainerRuntime { ClaimException = new PortUnavailableException("Host port 27015/udp is already published.") };

        ServerProvisionOutcome outcome = await Provisioner(runtime).ProvisionAsync(ServerId.New(), new CreateServer(27015), CancellationToken.None);

        await Assert.That(outcome.Succeeded).IsFalse();
        await Assert.That(outcome.FailureReason).IsEqualTo("Host port 27015/udp is already published.");
        await Assert.That(runtime.CreateCount).IsEqualTo(0);
    }

    [Test]
    public async Task A_port_clash_at_start_fails_the_provision_and_removes_the_new_container()
    {
        var runtime = new FakeContainerRuntime { StartFailure = _ => PortClash(16261) };
        ServerId server = ServerId.New();

        ServerProvisionOutcome outcome = await Provisioner(runtime).ProvisionAsync(server, new CreateServer(null), CancellationToken.None);

        await Assert.That(outcome.Succeeded).IsFalse();
        await Assert.That(outcome.FailureReason).Contains("already in use");
        await Assert.That(outcome.FailureReason).Contains("16261");
        // Removed so a retry (Recreate with another port) is not a name clash.
        await Assert.That(runtime.RemovedServerId).IsEqualTo(server);
    }

    [Test]
    public async Task Any_other_start_failure_is_reported_rather_than_left_to_the_reaper()
    {
        var runtime = new FakeContainerRuntime
        {
            StartFailure = _ => new DockerApiException(HttpStatusCode.InternalServerError, "{\"message\":\"boom\"}"),
        };

        ServerProvisionOutcome outcome = await Provisioner(runtime).ProvisionAsync(ServerId.New(), new CreateServer(null), CancellationToken.None);

        await Assert.That(outcome.Succeeded).IsFalse();
        await Assert.That(outcome.FailureReason).Contains("boom");
    }

    // --- Recreate: happy paths ---------------------------------------------------------------------------------

    [Test]
    public async Task Recreating_a_running_server_warns_stops_removes_creates_and_starts_on_the_new_ports()
    {
        ServerId server = ServerId.New();
        var runtime = new FakeContainerRuntime { ServerContainer = Existing(server), CreatedContainerId = "new-id" };
        var coordinator = new FakeServerRestartCoordinator();
        var plan = new GracefulRestartPlan([60], "Changing ports.");

        ServerProvisionOutcome outcome = await Recreate(Provisioner(runtime, coordinator), server, 27015, plan);

        await Assert.That(outcome.Succeeded).IsTrue();
        await Assert.That(outcome.Ports).IsEqualTo(new PortAllocation(27015, 27016));
        await Assert.That(outcome.ContainerId).IsEqualTo("new-id");
        await Assert.That(coordinator.WarnCount).IsEqualTo(1);
        await Assert.That(coordinator.LastPlan).IsEqualTo(plan);
        string[] expected = ["inspect", "claim:27015", "stop", "remove", "create:27015", "start:new-id"];
        await Assert.That(runtime.Calls).IsEquivalentTo(expected, TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    public async Task Recreating_a_stopped_server_neither_warns_nor_starts_it()
    {
        ServerId server = ServerId.New();
        var runtime = new FakeContainerRuntime { ServerContainer = Existing(server, "exited") };
        var coordinator = new FakeServerRestartCoordinator();

        ServerProvisionOutcome outcome = await Recreate(Provisioner(runtime, coordinator), server, 27015);

        await Assert.That(outcome.Succeeded).IsTrue();
        await Assert.That(coordinator.WarnCount).IsEqualTo(0);
        string[] expected = ["inspect", "claim:27015", "remove", "create:27015"];
        await Assert.That(runtime.Calls).IsEquivalentTo(expected, TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    public async Task Recreating_without_a_port_keeps_the_current_pair()
    {
        ServerId server = ServerId.New();
        var runtime = new FakeContainerRuntime { ServerContainer = Existing(server, "exited", new PortAllocation(27015, 27016)) };

        ServerProvisionOutcome outcome = await Recreate(Provisioner(runtime), server, null);

        await Assert.That(outcome.Ports).IsEqualTo(new PortAllocation(27015, 27016));
        await Assert.That(runtime.ClaimedGamePort).IsNull();
        await Assert.That(runtime.Calls).DoesNotContain("allocate");
    }

    [Test]
    public async Task Recreating_keeps_the_same_server_id_derived_binds_so_the_world_survives()
    {
        ServerId server = ServerId.New();
        var runtime = new FakeContainerRuntime { ServerContainer = Existing(server) };

        await Recreate(Provisioner(runtime), server, 27015);

        PzContainerSpec spec = runtime.LastSpec!;
        await Assert.That(spec.ContainerName).IsEqualTo(server.ToString());
        await Assert.That(spec.DataMountSource).IsEqualTo(Path.Combine(Root, server.ToString()));
        await Assert.That(spec.ServerMountSource).IsEqualTo(Path.Combine(Root, $"{server}.server"));
    }

    [Test]
    public async Task Recreating_an_absent_container_repairs_it_from_scratch()
    {
        // A failed provision (or a failed rollback) left the Server without a container: create it and start it.
        ServerId server = ServerId.New();
        var runtime = new FakeContainerRuntime { ServerContainer = null, NextPorts = new(16263, 16264), CreatedContainerId = "fresh" };

        ServerProvisionOutcome outcome = await Recreate(Provisioner(runtime), server, null);

        await Assert.That(outcome.Succeeded).IsTrue();
        string[] expected = ["inspect", "allocate", "create:16263", "start:fresh"];
        await Assert.That(runtime.Calls).IsEquivalentTo(expected, TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    // --- Recreate: refusals before anything changes ------------------------------------------------------------

    [Test]
    public async Task Recreate_refuses_a_container_whose_data_binds_are_not_the_derived_ones()
    {
        ServerId server = ServerId.New();
        var foreignMounts = Existing(server) with
        {
            BindMounts = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["/pz/data"] = "/somewhere/else",
                ["/pz/server"] = Path.Combine(Root, $"{server}.server"),
            },
        };
        var runtime = new FakeContainerRuntime { ServerContainer = foreignMounts };

        ServerProvisionOutcome outcome = await Recreate(Provisioner(runtime), server, 27015);

        await Assert.That(outcome.Succeeded).IsFalse();
        await Assert.That(outcome.FailureReason).Contains("/pz/data");
        string[] expected = ["inspect"];
        await Assert.That(runtime.Calls).IsEquivalentTo(expected);
    }

    [Test]
    public async Task Recreate_on_an_unavailable_port_changes_nothing()
    {
        ServerId server = ServerId.New();
        var runtime = new FakeContainerRuntime
        {
            ServerContainer = Existing(server),
            ClaimException = new PortUnavailableException("Host port 27016/udp is already published."),
        };
        var coordinator = new FakeServerRestartCoordinator();

        ServerProvisionOutcome outcome = await Recreate(Provisioner(runtime, coordinator), server, 27015);

        await Assert.That(outcome.FailureReason).IsEqualTo("Host port 27016/udp is already published.");
        await Assert.That(coordinator.WarnCount).IsEqualTo(0);
        await Assert.That(runtime.Calls).DoesNotContain("stop");
        await Assert.That(runtime.Calls).DoesNotContain("remove");
    }

    // --- Recreate: rollback ------------------------------------------------------------------------------------

    [Test]
    public async Task A_port_clash_on_the_new_container_rolls_back_to_the_old_ports_and_run_state()
    {
        ServerId server = ServerId.New();
        var runtime = new FakeContainerRuntime { ServerContainer = Existing(server), CreatedContainerId = "c" };
        int starts = 0;
        runtime.StartFailure = _ => ++starts == 1 ? PortClash(27015) : null;

        ServerProvisionOutcome outcome = await Recreate(Provisioner(runtime), server, 27015);

        await Assert.That(outcome.Succeeded).IsFalse();
        await Assert.That(outcome.FailureReason).Contains("already in use");
        await Assert.That(outcome.FailureReason).Contains("rolled back");
        // The previous pair is what the server ends on — reported so the Web keeps it.
        await Assert.That(outcome.Ports).IsEqualTo(OldPorts);
        string[] expected = ["inspect", "claim:27015", "stop", "remove", "create:27015", "start:c", "remove", "create:16261", "start:c"];
        await Assert.That(runtime.Calls).IsEquivalentTo(expected, TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    public async Task A_create_failure_rolls_back_without_a_second_remove()
    {
        ServerId server = ServerId.New();
        var runtime = new FakeContainerRuntime
        {
            ServerContainer = Existing(server, "exited"),
            CreateFailure = spec => spec.Ports.GamePort == 27015
                ? new ContainerCreateException(ContainerCreateFailure.Unknown, "Creating the container failed: HTTP 500.")
                : null,
        };

        ServerProvisionOutcome outcome = await Recreate(Provisioner(runtime), server, 27015);

        await Assert.That(outcome.Succeeded).IsFalse();
        await Assert.That(outcome.FailureReason).Contains("HTTP 500");
        await Assert.That(outcome.Ports).IsEqualTo(OldPorts);
        // Nothing new was created, so only the original remove; the old pair comes back, left stopped as before.
        string[] expected = ["inspect", "claim:27015", "remove", "create:27015", "create:16261"];
        await Assert.That(runtime.Calls).IsEquivalentTo(expected, TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    public async Task A_failed_rollback_says_the_server_needs_a_repair_recreate()
    {
        ServerId server = ServerId.New();
        var runtime = new FakeContainerRuntime
        {
            ServerContainer = Existing(server, "exited"),
            CreateFailure = _ => new ContainerCreateException(ContainerCreateFailure.Unknown, "Creating the container failed: HTTP 500."),
        };

        ServerProvisionOutcome outcome = await Recreate(Provisioner(runtime), server, 27015);

        await Assert.That(outcome.Succeeded).IsFalse();
        await Assert.That(outcome.FailureReason).Contains("no container");
        await Assert.That(outcome.Ports).IsNull();
    }

    [Test]
    public async Task A_remove_failure_after_the_stop_starts_the_old_container_again()
    {
        ServerId server = ServerId.New();
        var runtime = new FakeContainerRuntime
        {
            ServerContainer = Existing(server),
            RemoveException = new InvalidOperationException("The container must be stopped before it is removed."),
        };

        ServerProvisionOutcome outcome = await Recreate(Provisioner(runtime), server, 27015);

        await Assert.That(outcome.Succeeded).IsFalse();
        await Assert.That(outcome.FailureReason).Contains("must be stopped");
        await Assert.That(runtime.StartServerCount).IsEqualTo(1);
        await Assert.That(runtime.CreateCount).IsEqualTo(0);
    }

    // --- #230: per-server heap and initial settings ------------------------------------------------------------

    private const long GiB = 1024L * 1024 * 1024;

    // The provisioner's options: default heap 4 GiB + overhead 6 GiB.
    private static readonly AgentOptions Defaults = new();

    [Test]
    public async Task Provisioning_with_a_heap_builds_the_container_with_that_heap_plus_the_agents_overhead()
    {
        var runtime = new FakeContainerRuntime();

        ServerProvisionOutcome outcome = await Provisioner(runtime)
            .ProvisionAsync(ServerId.New(), new CreateServer(HeapSizeBytes: 8 * GiB), CancellationToken.None);

        await Assert.That(outcome.Succeeded).IsTrue();
        await Assert.That(runtime.LastSpec!.HeapSizeBytes).IsEqualTo(8 * GiB);
        await Assert.That(runtime.LastSpec.MemoryLimitBytes).IsEqualTo((8 * GiB) + Defaults.MemoryOverheadBytes);
        await Assert.That(outcome.HeapSizeBytes).IsEqualTo(8 * GiB);
    }

    [Test]
    public async Task Provisioning_without_a_heap_uses_the_agents_defaults()
    {
        var runtime = new FakeContainerRuntime();

        await Provisioner(runtime).ProvisionAsync(ServerId.New(), new CreateServer(), CancellationToken.None);

        await Assert.That(runtime.LastSpec!.HeapSizeBytes).IsEqualTo(Defaults.DefaultHeapSizeBytes);
        await Assert.That(runtime.LastSpec.MemoryLimitBytes).IsEqualTo(Defaults.DefaultMemoryLimitBytes);
    }

    [Test]
    public async Task Provisioning_refuses_an_invalid_heap_before_touching_anything()
    {
        var runtime = new FakeContainerRuntime();

        ServerProvisionOutcome outcome = await Provisioner(runtime)
            .ProvisionAsync(ServerId.New(), new CreateServer(HeapSizeBytes: 512L * 1024 * 1024), CancellationToken.None);

        await Assert.That(outcome.Succeeded).IsFalse();
        await Assert.That(outcome.FailureReason!).Contains("heap");
        await Assert.That(runtime.Calls).IsEmpty();
    }

    [Test]
    public async Task Provisioning_seeds_the_initial_settings_before_the_container_is_created()
    {
        var runtime = new FakeContainerRuntime();
        var seeder = new FakeInitialSettingsSeeder { OnSeed = () => runtime.Calls.Add("seed") };
        ServerId server = ServerId.New();
        InitialServerSettings settings = new(Public: true, PublicName: "Knox", MaxPlayers: 8);

        await Provisioner(runtime, seeder: seeder).ProvisionAsync(server, new CreateServer(Settings: settings), CancellationToken.None);

        await Assert.That(seeder.Seeded).IsEquivalentTo([(server, settings)]);
        await Assert.That(runtime.Calls.IndexOf("seed")).IsLessThan(runtime.Calls.FindIndex(c => c.StartsWith("create", StringComparison.Ordinal)));
    }

    [Test]
    public async Task Provisioning_refuses_invalid_settings_before_seeding_or_creating()
    {
        var runtime = new FakeContainerRuntime();
        var seeder = new FakeInitialSettingsSeeder();

        ServerProvisionOutcome outcome = await Provisioner(runtime, seeder: seeder).ProvisionAsync(
            ServerId.New(), new CreateServer(Settings: new InitialServerSettings(PublicName: "a\nb")), CancellationToken.None);

        await Assert.That(outcome.Succeeded).IsFalse();
        await Assert.That(seeder.Seeded).IsEmpty();
        await Assert.That(runtime.Calls).IsEmpty();
    }

    [Test]
    public async Task Recreating_with_a_heap_rebuilds_the_container_with_the_new_heap()
    {
        ServerId server = ServerId.New();
        var runtime = new FakeContainerRuntime { ServerContainer = Existing(server, heap: 4 * GiB) };

        ServerProvisionOutcome outcome = await Recreate(Provisioner(runtime), server, gamePort: null, heap: 10 * GiB);

        await Assert.That(outcome.Succeeded).IsTrue();
        await Assert.That(runtime.LastSpec!.HeapSizeBytes).IsEqualTo(10 * GiB);
        await Assert.That(runtime.LastSpec.MemoryLimitBytes).IsEqualTo((10 * GiB) + Defaults.MemoryOverheadBytes);
        await Assert.That(outcome.HeapSizeBytes).IsEqualTo(10 * GiB);
        await Assert.That(runtime.LastSpec.Ports).IsEqualTo(OldPorts);
    }

    [Test]
    public async Task Recreating_without_a_heap_keeps_the_heap_the_container_runs_with()
    {
        ServerId server = ServerId.New();
        var runtime = new FakeContainerRuntime { ServerContainer = Existing(server, heap: 7 * GiB) };

        await Recreate(Provisioner(runtime), server, gamePort: 27015);

        await Assert.That(runtime.LastSpec!.HeapSizeBytes).IsEqualTo(7 * GiB);
        await Assert.That(runtime.LastSpec.MemoryLimitBytes).IsEqualTo((7 * GiB) + Defaults.MemoryOverheadBytes);
    }

    [Test]
    public async Task Recreating_a_container_with_an_unknown_heap_falls_back_to_the_agents_defaults()
    {
        ServerId server = ServerId.New();
        var runtime = new FakeContainerRuntime { ServerContainer = Existing(server, heap: null) };

        await Recreate(Provisioner(runtime), server, gamePort: 27015);

        await Assert.That(runtime.LastSpec!.HeapSizeBytes).IsEqualTo(Defaults.DefaultHeapSizeBytes);
        await Assert.That(runtime.LastSpec.MemoryLimitBytes).IsEqualTo(Defaults.DefaultMemoryLimitBytes);
    }

    [Test]
    public async Task A_failed_recreate_rolls_back_to_the_previous_heap_as_well_as_the_previous_ports()
    {
        ServerId server = ServerId.New();
        var runtime = new FakeContainerRuntime { ServerContainer = Existing(server, heap: 6 * GiB), CreatedContainerId = "c" };
        int starts = 0;
        runtime.StartFailure = _ => ++starts == 1 ? PortClash(27015) : null;

        ServerProvisionOutcome outcome = await Recreate(Provisioner(runtime), server, gamePort: 27015, heap: 12 * GiB);

        await Assert.That(outcome.Succeeded).IsFalse();
        await Assert.That(runtime.CreatedSpecs[0].HeapSizeBytes).IsEqualTo(12 * GiB);
        await Assert.That(runtime.CreatedSpecs[^1].HeapSizeBytes).IsEqualTo(6 * GiB);
        await Assert.That(runtime.CreatedSpecs[^1].Ports).IsEqualTo(OldPorts);
        await Assert.That(outcome.HeapSizeBytes).IsEqualTo(6 * GiB);
    }

    [Test]
    public async Task Recreating_refuses_an_invalid_heap_before_stopping_anything()
    {
        ServerId server = ServerId.New();
        var runtime = new FakeContainerRuntime { ServerContainer = Existing(server) };

        ServerProvisionOutcome outcome = await Recreate(Provisioner(runtime), server, gamePort: null, heap: 200 * GiB);

        await Assert.That(outcome.Succeeded).IsFalse();
        await Assert.That(runtime.Calls).DoesNotContain("stop");
        await Assert.That(runtime.RemoveCount).IsEqualTo(0);
    }
}
