using System.Net;
using Docker.DotNet;
using Microsoft.Extensions.Options;
using ZWarden.Agent.Configuration;
using ZWarden.Agent.Docker;
using ZWarden.Domain.Ids;

namespace ZWarden.Agent.Tests.Docker;

/// <summary>
/// F13 S3: the container runtime's policy over a fake engine — health/negotiation, discovery scoped to this
/// Agent, stride allocation from discovered ports, the image-absent create diagnostic, and (the load-bearing
/// one) allowed-container enforcement that refuses a foreign container before any verb is issued.
/// </summary>
public class ContainerRuntimeTests
{
    private static readonly AgentId Self = AgentId.New();

    private const int StopTimeout = 120;

    private static ContainerRuntime Runtime(FakeDockerEngine engine, int stopTimeoutSeconds = StopTimeout)
    {
        var identity = new FixedAgentIdentity(Self);
        return new ContainerRuntime(
            engine,
            new ContainerOwnershipGuard(identity),
            new PzContainerFactory(identity),
            Options.Create(new AgentOptions { StopTimeoutSeconds = stopTimeoutSeconds }),
            new RecordingLogger<ContainerRuntime>());
    }

    private static EngineContainer Container(string id, AgentId owner, ServerId server, string state = "running", params PublishedPort[] ports) =>
        new(
            id,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [CanonicalLabels.Managed] = CanonicalLabels.ManagedValue,
                [CanonicalLabels.Runtime] = CanonicalLabels.RuntimeValue,
                [CanonicalLabels.SchemaVersion] = CanonicalLabels.SchemaVersionValue,
                [CanonicalLabels.ServerId] = server.ToString(),
                [CanonicalLabels.AgentId] = owner.ToString(),
            },
            state,
            ports);

    private static PzContainerSpec Spec() => new(
        ServerId.New(), "zwarden-srv", "img@sha256:abc", "zwarden-pz", "/srv/zwarden/data", "/srv/zwarden/server",
        PortStrideAllocator.ForStride(0), 6L * 1024 * 1024 * 1024, 4L * 1024 * 1024 * 1024);

    [Test]
    public async Task Health_reports_the_negotiated_api_version_when_reachable()
    {
        DockerHealth health = await Runtime(new FakeDockerEngine { ApiVersion = "1.53" })
            .ProbeHealthAsync(CancellationToken.None);

        await Assert.That(health.DaemonReachable).IsTrue();
        await Assert.That(health.ApiVersion).IsEqualTo("1.53");
        await Assert.That(health.Detail).IsNull();
    }

    [Test]
    public async Task Health_reports_unreachable_without_throwing()
    {
        DockerHealth health = await Runtime(new FakeDockerEngine { PingException = new TimeoutException() })
            .ProbeHealthAsync(CancellationToken.None);

        await Assert.That(health.DaemonReachable).IsFalse();
        await Assert.That(health.ApiVersion).IsNull();
        await Assert.That(health.Detail).IsNotNull();
    }

    [Test]
    public async Task Discovery_returns_only_canonical_containers_this_agent_owns()
    {
        ServerId mine = ServerId.New();
        var engine = new FakeDockerEngine();
        engine.Listed.Add(Container("mine", Self, mine));
        engine.Listed.Add(Container("foreign", AgentId.New(), ServerId.New()));
        engine.Listed.Add(new EngineContainer("stranger", new Dictionary<string, string>(), "running", []));

        IReadOnlyList<ManagedContainer> managed = await Runtime(engine).ListManagedAsync(CancellationToken.None);

        await Assert.That(managed).HasSingleItem();
        await Assert.That(managed[0].DockerId).IsEqualTo("mine");
        await Assert.That(managed[0].ServerId).IsEqualTo(mine);
    }

    [Test]
    public async Task Allocation_picks_the_next_stride_above_discovered_containers()
    {
        var engine = new FakeDockerEngine();
        // An owned container already publishing stride 0's game port (16261/udp).
        engine.Listed.Add(Container("mine", Self, ServerId.New(), "running",
            new PublishedPort(16261, 16261, "udp")));

        PortAllocation next = await Runtime(engine).AllocateNextPortsAsync(CancellationToken.None);

        await Assert.That(next).IsEqualTo(PortStrideAllocator.ForStride(1));
    }

    [Test]
    public async Task Allocation_respects_a_foreign_containers_published_udp_port()
    {
        var engine = new FakeDockerEngine();
        // Something else on the daemon already holds 16262/udp — stride 0 is unusable (#229).
        engine.Listed.Add(new EngineContainer("stranger", new Dictionary<string, string>(), "running",
            [new PublishedPort(16262, 9000, "udp")]));

        PortAllocation next = await Runtime(engine).AllocateNextPortsAsync(CancellationToken.None);

        await Assert.That(next).IsEqualTo(PortStrideAllocator.ForStride(1));
    }

    [Test]
    public async Task Allocation_ignores_a_tcp_port_of_the_same_number()
    {
        var engine = new FakeDockerEngine();
        engine.Listed.Add(new EngineContainer("web", new Dictionary<string, string>(), "running",
            [new PublishedPort(16261, 80, "tcp")]));

        PortAllocation next = await Runtime(engine).AllocateNextPortsAsync(CancellationToken.None);

        await Assert.That(next).IsEqualTo(PortStrideAllocator.ForStride(0));
    }

    [Test]
    public async Task Allocation_counts_a_stopped_containers_configured_bindings()
    {
        // A stopped container publishes nothing in the list API, but it was created with stride 0 and will want it
        // back on start — so stride 0 is still occupied (#229).
        ServerId stopped = ServerId.New();
        var engine = new FakeDockerEngine();
        engine.Listed.Add(Container("stopped", Self, stopped, "exited"));
        engine.Inspected["stopped"] = Container("stopped", Self, stopped, "exited") with
        {
            ConfiguredPorts = [new PublishedPort(16261, 16261, "udp"), new PublishedPort(16262, 16262, "udp")],
        };

        PortAllocation next = await Runtime(engine).AllocateNextPortsAsync(CancellationToken.None);

        await Assert.That(next).IsEqualTo(PortStrideAllocator.ForStride(1));
    }

    [Test]
    public async Task A_free_requested_game_port_is_granted_as_its_pair()
    {
        var engine = new FakeDockerEngine();
        engine.Listed.Add(Container("other", Self, ServerId.New(), "running", new PublishedPort(16261, 16261, "udp")));

        PortAllocation pair = await Runtime(engine).ClaimRequestedPortsAsync(27015, ServerId.New(), CancellationToken.None);

        await Assert.That(pair).IsEqualTo(new PortAllocation(27015, 27016));
    }

    [Test]
    public async Task A_requested_pair_overlapping_another_container_is_refused()
    {
        var engine = new FakeDockerEngine();
        engine.Listed.Add(Container("other", Self, ServerId.New(), "running", new PublishedPort(16262, 16262, "udp")));

        var ex = await Assert.ThrowsAsync<PortUnavailableException>(() =>
            Runtime(engine).ClaimRequestedPortsAsync(16261, ServerId.New(), CancellationToken.None));

        await Assert.That(ex!.Message).Contains("16262");
    }

    [Test]
    public async Task A_requested_pair_held_by_the_same_server_is_not_a_clash()
    {
        // Recreate on the ports the server already has — its own container is about to be removed.
        ServerId self = ServerId.New();
        var engine = new FakeDockerEngine();
        engine.Listed.Add(Container("self", Self, self, "running", new PublishedPort(16261, 16261, "udp")));

        PortAllocation pair = await Runtime(engine).ClaimRequestedPortsAsync(16261, self, CancellationToken.None);

        await Assert.That(pair).IsEqualTo(PortStrideAllocator.ForStride(0));
    }

    [Test]
    [Arguments(80)]
    [Arguments(65535)]
    public async Task A_requested_game_port_out_of_range_is_refused(int port)
    {
        await Assert.ThrowsAsync<PortUnavailableException>(() =>
            Runtime(new FakeDockerEngine()).ClaimRequestedPortsAsync(port, ServerId.New(), CancellationToken.None));
    }

    [Test]
    public async Task Create_builds_the_invariant_body_and_returns_the_id()
    {
        var engine = new FakeDockerEngine { CreatedId = "abc" };

        string id = await Runtime(engine).CreateAsync(Spec(), CancellationToken.None);

        await Assert.That(id).IsEqualTo("abc");
        await Assert.That(engine.CreatedWith!.HostConfig!.Privileged).IsFalse();
    }

    [Test]
    public async Task Create_maps_a_missing_image_to_the_pre_provision_diagnostic()
    {
        var engine = new FakeDockerEngine
        {
            CreateException = new DockerApiException(HttpStatusCode.NotFound, "No such image: img@sha256:abc"),
        };

        var ex = await Assert.ThrowsAsync<ContainerCreateException>(() =>
            Runtime(engine).CreateAsync(Spec(), CancellationToken.None));

        await Assert.That(ex!.Failure).IsEqualTo(ContainerCreateFailure.ImageNotProvisioned);
    }

    [Test]
    public async Task Start_refuses_a_foreign_container_and_issues_no_verb()
    {
        var engine = new FakeDockerEngine
        {
            InspectResult = Container("foreign", AgentId.New(), ServerId.New()),
        };

        await Assert.ThrowsAsync<ForeignContainerException>(() =>
            Runtime(engine).StartAsync("foreign", CancellationToken.None));

        await Assert.That(engine.Started).IsEmpty();
    }

    [Test]
    public async Task Start_stop_restart_act_on_a_container_this_agent_owns()
    {
        var engine = new FakeDockerEngine { InspectResult = Container("mine", Self, ServerId.New()) };
        ContainerRuntime runtime = Runtime(engine);

        await runtime.StartAsync("mine", CancellationToken.None);
        await runtime.StopAsync("mine", CancellationToken.None);
        await runtime.RestartAsync("mine", CancellationToken.None);

        await Assert.That(engine.Started).Contains("mine");
        await Assert.That(engine.Stopped).Contains("mine");
        await Assert.That(engine.Restarted).Contains("mine");
    }

    [Test]
    public async Task Stop_and_restart_carry_the_configured_safe_timeout()
    {
        var engine = new FakeDockerEngine { InspectResult = Container("mine", Self, ServerId.New()) };
        ContainerRuntime runtime = Runtime(engine, stopTimeoutSeconds: 90);

        await runtime.StopAsync("mine", CancellationToken.None);
        await Assert.That(engine.LastWaitBeforeKillSeconds).IsEqualTo(90);

        await runtime.RestartAsync("mine", CancellationToken.None);
        await Assert.That(engine.LastWaitBeforeKillSeconds).IsEqualTo(90);
    }

    [Test]
    public async Task Lifecycle_by_server_id_resolves_the_owned_container_and_acts()
    {
        ServerId mine = ServerId.New();
        var engine = new FakeDockerEngine { InspectResult = Container("mine", Self, mine) };
        engine.Listed.Add(Container("mine", Self, mine));
        engine.Listed.Add(Container("foreign", AgentId.New(), ServerId.New()));
        ContainerRuntime runtime = Runtime(engine);

        await runtime.StartAsync(mine, CancellationToken.None);
        await runtime.StopAsync(mine, CancellationToken.None);
        await runtime.RestartAsync(mine, CancellationToken.None);

        await Assert.That(engine.Started).Contains("mine");
        await Assert.That(engine.Stopped).Contains("mine");
        await Assert.That(engine.Restarted).Contains("mine");
        await Assert.That(engine.LastWaitBeforeKillSeconds).IsEqualTo(StopTimeout);
    }

    [Test]
    public async Task Lifecycle_by_server_id_throws_when_no_owned_container_matches()
    {
        var engine = new FakeDockerEngine();
        // Only a foreign container exists — nothing this Agent owns for the target server.
        engine.Listed.Add(Container("foreign", AgentId.New(), ServerId.New()));
        ContainerRuntime runtime = Runtime(engine);

        await Assert.ThrowsAsync<ContainerNotFoundException>(() =>
            runtime.StopAsync(ServerId.New(), CancellationToken.None));

        await Assert.That(engine.Stopped).IsEmpty();
    }

    [Test]
    public async Task Remove_deletes_a_stopped_owned_container_by_its_server_id_name()
    {
        ServerId mine = ServerId.New();
        var engine = new FakeDockerEngine();
        engine.Listed.Add(Container("docker-hex-id", Self, mine, "exited"));
        engine.InspectResult = Container("docker-hex-id", Self, mine, "exited") with { Name = mine.ToString() };

        await Runtime(engine).RemoveAsync(mine, CancellationToken.None);

        // By NAME, never the hex id — the proxy only admits a UUID-shaped DELETE path (ADR 0045).
        await Assert.That(engine.Removed).IsEquivalentTo([mine.ToString()]);
    }

    [Test]
    [Arguments("running")]
    [Arguments("restarting")]
    [Arguments("paused")]
    public async Task Remove_refuses_a_container_that_is_not_stopped(string state)
    {
        ServerId mine = ServerId.New();
        var engine = new FakeDockerEngine();
        engine.Listed.Add(Container("c", Self, mine, state));
        engine.InspectResult = Container("c", Self, mine, state) with { Name = mine.ToString() };

        await Assert.ThrowsAsync<InvalidOperationException>(() => Runtime(engine).RemoveAsync(mine, CancellationToken.None));

        await Assert.That(engine.Removed).IsEmpty();
    }

    [Test]
    public async Task Remove_refuses_a_foreign_container_even_if_inspect_disagrees_with_the_list()
    {
        // The list said "mine", but the authoritative inspect shows another Agent's labels — refuse before any verb.
        ServerId mine = ServerId.New();
        var engine = new FakeDockerEngine();
        engine.Listed.Add(Container("c", Self, mine, "exited"));
        engine.InspectResult = Container("c", AgentId.New(), mine, "exited") with { Name = mine.ToString() };

        await Assert.ThrowsAsync<ForeignContainerException>(() => Runtime(engine).RemoveAsync(mine, CancellationToken.None));

        await Assert.That(engine.Removed).IsEmpty();
    }

    [Test]
    public async Task Remove_refuses_a_container_not_named_by_its_server_id()
    {
        ServerId mine = ServerId.New();
        var engine = new FakeDockerEngine();
        engine.Listed.Add(Container("c", Self, mine, "exited"));
        engine.InspectResult = Container("c", Self, mine, "exited") with { Name = "some-other-name" };

        await Assert.ThrowsAsync<InvalidOperationException>(() => Runtime(engine).RemoveAsync(mine, CancellationToken.None));

        await Assert.That(engine.Removed).IsEmpty();
    }

    [Test]
    public async Task Remove_throws_not_found_when_no_owned_container_matches()
    {
        var engine = new FakeDockerEngine();
        engine.Listed.Add(Container("foreign", AgentId.New(), ServerId.New(), "exited"));

        await Assert.ThrowsAsync<ContainerNotFoundException>(() =>
            Runtime(engine).RemoveAsync(ServerId.New(), CancellationToken.None));

        await Assert.That(engine.Removed).IsEmpty();
    }

    [Test]
    public async Task Inspect_server_returns_the_owned_containers_recreate_facts()
    {
        ServerId mine = ServerId.New();
        var engine = new FakeDockerEngine();
        engine.Listed.Add(Container("c", Self, mine, "running"));
        engine.InspectResult = Container("c", Self, mine, "running") with
        {
            Name = mine.ToString(),
            ConfiguredPorts = [new PublishedPort(27015, 16261, "udp"), new PublishedPort(27016, 16262, "udp")],
            BindMounts = new Dictionary<string, string>(StringComparer.Ordinal) { ["/pz/data"] = "/srv/zwarden/x" },
        };

        ServerContainer? facts = await Runtime(engine).InspectServerAsync(mine, CancellationToken.None);

        await Assert.That(facts).IsNotNull();
        await Assert.That(facts!.IsRunning).IsTrue();
        await Assert.That(facts.Ports).IsEqualTo(new PortAllocation(27015, 27016));
        await Assert.That(facts.BindMounts["/pz/data"]).IsEqualTo("/srv/zwarden/x");
    }

    [Test]
    public async Task Inspect_server_returns_null_when_no_owned_container_matches()
    {
        var engine = new FakeDockerEngine();
        engine.Listed.Add(Container("foreign", AgentId.New(), ServerId.New()));

        await Assert.That(await Runtime(engine).InspectServerAsync(ServerId.New(), CancellationToken.None)).IsNull();
    }

    [Test]
    public async Task Follow_logs_by_server_id_streams_the_owned_containers_frames()
    {
        ServerId mine = ServerId.New();
        var engine = new FakeDockerEngine();
        engine.Listed.Add(Container("mine", Self, mine));
        engine.Listed.Add(Container("foreign", AgentId.New(), ServerId.New()));
        engine.LogFrames.Add(new ContainerLogFrame(DateTimeOffset.UnixEpoch, IsStderr: false, "hello"));
        engine.LogFrames.Add(new ContainerLogFrame(DateTimeOffset.UnixEpoch, IsStderr: true, "oops"));
        ContainerRuntime runtime = Runtime(engine);

        List<ContainerLogFrame> seen = [];
        await runtime.FollowServerLogsAsync(
            mine, tailLines: 50, (frame, _) => { seen.Add(frame); return ValueTask.CompletedTask; }, CancellationToken.None);

        string[] expected = ["hello", "oops"];
        await Assert.That(engine.LastFollowContainerId).IsEqualTo("mine");
        await Assert.That(engine.LastFollowTailLines).IsEqualTo(50);
        await Assert.That(seen.Select(f => f.Text)).IsEquivalentTo(expected);
    }

    [Test]
    public async Task Follow_logs_by_server_id_throws_when_no_owned_container_matches()
    {
        var engine = new FakeDockerEngine();
        // Only a foreign container exists — nothing this Agent owns to follow.
        engine.Listed.Add(Container("foreign", AgentId.New(), ServerId.New()));
        ContainerRuntime runtime = Runtime(engine);

        await Assert.ThrowsAsync<ContainerNotFoundException>(() =>
            runtime.FollowServerLogsAsync(
                ServerId.New(), tailLines: 0, (_, _) => ValueTask.CompletedTask, CancellationToken.None));

        await Assert.That(engine.LastFollowContainerId).IsNull();
    }
}
