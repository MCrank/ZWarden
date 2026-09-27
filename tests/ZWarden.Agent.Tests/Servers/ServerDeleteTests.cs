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
/// #271: deleting a Server's container — (warn + safe stop if running) → ownership-guarded remove by name — never
/// touching world data. An absent container is already deleted; a stop or remove failure is reported and nothing
/// else happens.
/// </summary>
public class ServerDeleteTests
{
    private static readonly string Root = OperatingSystem.IsWindows() ? @"C:\pz" : "/pz";

    private static ServerProvisioner Provisioner(FakeContainerRuntime runtime, FakeServerRestartCoordinator? coordinator = null) =>
        new(
            runtime,
            new FakeServerHostDirectories(),
            new FakeRconServerConfig(),
            new FakeInitialSettingsSeeder(),
            coordinator ?? new FakeServerRestartCoordinator(),
            Options.Create(new AgentOptions { PzImageReference = "zwarden/pzserver:pinned", DataMountRoot = Root }),
            NullLogger<ServerProvisioner>.Instance);

    private static ServerContainer Existing(ServerId server, string state) =>
        new("old-id", state, new PortAllocation(16261, 16262), new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["/pz/data"] = Path.Combine(Root, server.ToString()),
            ["/pz/server"] = Path.Combine(Root, $"{server}.server"),
        });

    private static Task<ServerDeleteOutcome> Delete(ServerProvisioner sut, ServerId server, GracefulRestartPlan? plan = null) =>
        sut.DeleteAsync(server, new DeleteServer(plan), OperationId.New(), NullOperationProgressReporter.Instance, CancellationToken.None);

    [Test]
    public async Task Deleting_a_running_server_warns_players_stops_it_safely_then_removes_the_container()
    {
        ServerId server = ServerId.New();
        var runtime = new FakeContainerRuntime { ServerContainer = Existing(server, "running") };
        var coordinator = new FakeServerRestartCoordinator();
        var plan = new GracefulRestartPlan([60], "Retiring this server.");

        ServerDeleteOutcome outcome = await Delete(Provisioner(runtime, coordinator), server, plan);

        await Assert.That(outcome.Succeeded).IsTrue();
        await Assert.That(coordinator.WarnCount).IsEqualTo(1);
        await Assert.That(coordinator.LastPlan).IsEqualTo(plan);
        string[] expected = ["inspect", "stop", "remove"];
        await Assert.That(runtime.Calls).IsEquivalentTo(expected, TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    public async Task Deleting_a_stopped_server_removes_the_container_without_warning_anyone()
    {
        ServerId server = ServerId.New();
        var runtime = new FakeContainerRuntime { ServerContainer = Existing(server, "exited") };
        var coordinator = new FakeServerRestartCoordinator();

        ServerDeleteOutcome outcome = await Delete(Provisioner(runtime, coordinator), server);

        await Assert.That(outcome.Succeeded).IsTrue();
        await Assert.That(coordinator.WarnCount).IsEqualTo(0);
        string[] expected = ["inspect", "remove"];
        await Assert.That(runtime.Calls).IsEquivalentTo(expected, TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    public async Task Deleting_a_server_with_no_container_succeeds_without_touching_docker()
    {
        var runtime = new FakeContainerRuntime();

        ServerDeleteOutcome outcome = await Delete(Provisioner(runtime), ServerId.New());

        await Assert.That(outcome.Succeeded).IsTrue();
        string[] expected = ["inspect"];
        await Assert.That(runtime.Calls).IsEquivalentTo(expected, TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    public async Task A_stop_failure_fails_the_delete_and_leaves_the_container()
    {
        ServerId server = ServerId.New();
        var runtime = new FakeContainerRuntime
        {
            ServerContainer = Existing(server, "running"),
            LifecycleException = new DockerApiException(HttpStatusCode.InternalServerError, "{\"message\":\"boom\"}"),
        };

        ServerDeleteOutcome outcome = await Delete(Provisioner(runtime), server);

        await Assert.That(outcome.Succeeded).IsFalse();
        await Assert.That(outcome.FailureReason).Contains("boom");
        await Assert.That(runtime.Calls).DoesNotContain("remove");
    }

    [Test]
    public async Task A_remove_failure_fails_the_delete_and_starts_a_previously_running_server_again()
    {
        ServerId server = ServerId.New();
        var runtime = new FakeContainerRuntime
        {
            ServerContainer = Existing(server, "running"),
            RemoveException = new InvalidOperationException("The container must be stopped before it is removed."),
        };

        ServerDeleteOutcome outcome = await Delete(Provisioner(runtime), server);

        await Assert.That(outcome.Succeeded).IsFalse();
        await Assert.That(outcome.FailureReason).Contains("must be stopped");
        await Assert.That(runtime.StartServerCount).IsEqualTo(1);
    }
}
