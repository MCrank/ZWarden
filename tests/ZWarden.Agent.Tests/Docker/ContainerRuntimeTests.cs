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
        PortStrideAllocator.ForStride(0), 6L * 1024 * 1024 * 1024);

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
}
