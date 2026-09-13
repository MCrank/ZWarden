using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Docker.DotNet;
using Docker.DotNet.Models;
using Microsoft.Extensions.Logging.Abstractions;
using ZWarden.Agent.Docker;
using ZWarden.Agent.Identity;
using ZWarden.Domain.Ids;

namespace ZWarden.IntegrationTests;

/// <summary>
/// F13 tier-2: the Agent's Docker runtime against a real, disposable daemon (ADR 0008 §7 — a different trust
/// context from the reference deployment). It proves discovery is scoped to this Agent, that a foreign
/// container is refused, the create→inspect→lifecycle round-trip carries the §5.3 invariants unsanitized, and
/// the pre-provision diagnostic. The final test is ADR 0008's adoptable claim: the runtime driven <b>through a
/// wollomatic proxy carrying the ten-entry allowlist</b>, so any drift between what the Agent calls and what
/// the deployment permits fails the build — and a denied verb (DELETE) is refused by the proxy.
/// </summary>
[ParallelLimiter<ContainerParallelLimit>]
public sealed class AgentDockerRuntimeTests : IAsyncDisposable
{
    // A tiny, pinned image (never :latest, so the create template accepts it). Pre-provisioned by the test.
    private const string TestImage = "busybox:1.36";

    private readonly AgentId _self = AgentId.New();
    private readonly DockerClient _direct = new DockerClientBuilder().Build();
    private readonly List<string> _containers = [];
    private readonly List<string> _networks = [];

    private ContainerRuntime RuntimeOver(IDockerEngine engine) =>
        new(engine, new ContainerOwnershipGuard(new FixedIdentity(_self)), new PzContainerFactory(new FixedIdentity(_self)),
            Microsoft.Extensions.Options.Options.Create(new Agent.Configuration.AgentOptions()),
            NullLogger<ContainerRuntime>.Instance);

    private ContainerRuntime DirectRuntime() => RuntimeOver(new DockerDotNetEngine(_direct));

    [Test]
    [Category("Networked")]
    [Timeout(180_000)]
    public async Task Health_probe_reaches_the_local_daemon(CancellationToken ct)
    {
        DockerHealth health = await DirectRuntime().ProbeHealthAsync(ct);

        await Assert.That(health.DaemonReachable).IsTrue();
        await Assert.That(health.ApiVersion).IsNotNull();
    }

    [Test]
    [Category("Networked")]
    [Timeout(300_000)]
    public async Task Discovery_returns_only_this_agents_canonical_containers(CancellationToken ct)
    {
        await EnsureImageAsync(ct);
        ServerId mine = ServerId.New();
        string mineId = await SeedAsync(CanonicalLabelSet(_self, mine), ct);
        await SeedAsync(CanonicalLabelSet(AgentId.New(), ServerId.New()), ct);           // foreign
        await SeedAsync(new Dictionary<string, string> { ["com.example"] = "x" }, ct);   // non-canonical

        IReadOnlyList<ManagedContainer> managed = await DirectRuntime().ListManagedAsync(ct);

        await Assert.That(managed.Any(c => c.DockerId == mineId && c.ServerId == mine)).IsTrue();
        await Assert.That(managed.All(c => c.ServerId == mine)).IsTrue();
    }

    [Test]
    [Category("Networked")]
    [Timeout(300_000)]
    public async Task Start_refuses_a_foreign_container(CancellationToken ct)
    {
        await EnsureImageAsync(ct);
        string foreignId = await SeedAsync(CanonicalLabelSet(AgentId.New(), ServerId.New()), ct);

        await Assert.ThrowsAsync<ForeignContainerException>(() => DirectRuntime().StartAsync(foreignId, ct));
    }

    [Test]
    [Category("Networked")]
    [Timeout(300_000)]
    public async Task Create_inspect_lifecycle_round_trip_carries_the_invariants(CancellationToken ct)
    {
        await EnsureImageAsync(ct);
        string network = await CreateNetworkAsync(ct);
        ContainerRuntime runtime = DirectRuntime();

        string id = await runtime.CreateAsync(SpecFor(ServerId.New(), network), ct);
        _containers.Add(id);

        // The invariant body reached the daemon unsanitized in our favour.
        ContainerInspectResponse inspected = await _direct.Containers.InspectContainerAsync(id, ct);
        await Assert.That(inspected.HostConfig!.Privileged).IsFalse();
        await Assert.That(inspected.HostConfig!.ReadonlyRootfs).IsTrue();
        await Assert.That(inspected.Config!.Labels![CanonicalLabels.AgentId]).IsEqualTo(_self.ToString());

        // The allowlisted lifecycle verbs round-trip on a container this Agent owns.
        await runtime.StartAsync(id, ct);
        await runtime.RestartAsync(id, ct);
        await runtime.StopAsync(id, ct);
    }

    [Test]
    [Category("Networked")]
    [Timeout(180_000)]
    public async Task Create_with_a_missing_image_yields_the_pre_provision_diagnostic(CancellationToken ct)
    {
        string network = await CreateNetworkAsync(ct);
        var spec = SpecFor(ServerId.New(), network) with
        {
            ImageReference = "zwarden-nonexistent@sha256:0000000000000000000000000000000000000000000000000000000000000000",
        };

        var ex = await Assert.ThrowsAsync<ContainerCreateException>(() => DirectRuntime().CreateAsync(spec, ct));

        await Assert.That(ex!.Failure).IsEqualTo(ContainerCreateFailure.ImageNotProvisioned);
    }

    [Test]
    [Category("Networked")]
    [Timeout(420_000)]
    public async Task The_runtime_works_through_the_wollomatic_allowlist_and_a_denied_verb_is_refused(CancellationToken ct)
    {
        await EnsureImageAsync(ct);
        string network = await CreateNetworkAsync(ct);

        await using IContainer proxy = new ContainerBuilder("wollomatic/socket-proxy:1.13.1")
            .WithBindMount("/var/run/docker.sock", "/var/run/docker.sock")
            // Run as root so the proxy can read the root-owned docker socket. The test exercises the allowlist
            // (verb filtering), not the proxy's own privilege posture — that is F34's deployment concern.
            .WithCreateParameterModifier(parameters => parameters.User = "0:0")
            .WithCommand(
                "-loglevel=INFO",
                "-listenip=0.0.0.0",
                "-allowfrom=0.0.0.0/0",
                // The eleventh allowlist entry (ADR 0008 as amended by F16): read-only container stats for the
                // runtime-metrics sampler. It is a GET, alongside json/logs — no mutation, no exec.
                "-allowGET=(/v1\\.[0-9]+)?/(_ping|version|info|containers/json|containers/[a-zA-Z0-9_.-]+/(json|logs|stats))",
                "-allowHEAD=(/v1\\.[0-9]+)?/_ping",
                "-allowPOST=(/v1\\.[0-9]+)?/(containers/create|containers/[a-zA-Z0-9_.-]+/(start|stop|restart))",
                "-allowbindmountfrom=/tmp",
                "-watchdoginterval=0")
            .WithPortBinding(2375, assignRandomHostPort: true)
            .Build();
        // The wollomatic image is distroless, so an exec-based port wait can't run in it. Start, then poll the
        // proxy from the test until the health probe round-trips (or the test's timeout fires).
        await proxy.StartAsync(ct);

        DockerClient proxied = new DockerClientBuilder()
            .WithEndpoint(new Uri($"tcp://{proxy.Hostname}:{proxy.GetMappedPublicPort(2375)}"))
            .Build();
        ContainerRuntime runtime = RuntimeOver(new DockerDotNetEngine(proxied));

        await WaitUntilReachableAsync(runtime, ct);

        // Every runtime call is an allowlisted verb, so the whole round-trip succeeds through the proxy.
        await Assert.That((await runtime.ProbeHealthAsync(ct)).DaemonReachable).IsTrue();
        string id = await runtime.CreateAsync(SpecFor(ServerId.New(), network), ct);
        _containers.Add(id);
        await runtime.StartAsync(id, ct);

        // The F16 metrics read (GET /containers/{id}/stats?stream=false) round-trips through the amended
        // allowlist while the container is running.
        ContainerStatsSnapshot stats = await new DockerDotNetEngine(proxied).StatsAsync(id, ct);
        await Assert.That(stats.MemoryLimit).IsGreaterThan(0UL);

        await runtime.StopAsync(id, ct);

        // A denied verb (DELETE) is refused by the allowlist — proving the proxy really constrains the surface.
        DockerApiException? denied = await Assert.ThrowsAsync<DockerApiException>(() =>
            proxied.Containers.RemoveContainerAsync(id, new ContainerRemoveParameters { Force = true }, ct));
        await Assert.That((int)denied!.StatusCode).IsEqualTo(405);

        proxied.Dispose();
    }

    private static async Task WaitUntilReachableAsync(ContainerRuntime runtime, CancellationToken ct)
    {
        for (int attempt = 0; attempt < 30; attempt++)
        {
            if ((await runtime.ProbeHealthAsync(ct)).DaemonReachable)
            {
                return;
            }

            await Task.Delay(1000, ct);
        }
    }

    private static PzContainerSpec SpecFor(ServerId server, string network) => new(
        server,
        $"zwarden-itest-{Guid.NewGuid():N}",
        TestImage,
        network,
        "/tmp",
        PortStrideAllocator.ForStride(0),
        64L * 1024 * 1024);

    private static Dictionary<string, string> CanonicalLabelSet(AgentId owner, ServerId server) => new(StringComparer.Ordinal)
    {
        [CanonicalLabels.Managed] = CanonicalLabels.ManagedValue,
        [CanonicalLabels.Runtime] = CanonicalLabels.RuntimeValue,
        [CanonicalLabels.SchemaVersion] = CanonicalLabels.SchemaVersionValue,
        [CanonicalLabels.ServerId] = server.ToString(),
        [CanonicalLabels.AgentId] = owner.ToString(),
    };

    private async Task EnsureImageAsync(CancellationToken ct)
    {
        IList<ImagesListResponse> present = await _direct.Images.ListImagesAsync(
            new ImagesListParameters { Filters = new Dictionary<string, IDictionary<string, bool>> { ["reference"] = new Dictionary<string, bool> { [TestImage] = true } } }, ct);
        if (present.Count == 0)
        {
            await _direct.Images.CreateImageAsync(
                new ImagesCreateParameters { FromImage = "busybox", Tag = "1.36" }, null, new Progress<JSONMessage>(), ct);
        }
    }

    private async Task<string> SeedAsync(IDictionary<string, string> labels, CancellationToken ct)
    {
        CreateContainerResponse created = await _direct.Containers.CreateContainerAsync(new CreateContainerParameters
        {
            Image = TestImage,
            Labels = labels,
            Cmd = ["sleep", "300"],
        }, ct);
        _containers.Add(created.ID);
        return created.ID;
    }

    private async Task<string> CreateNetworkAsync(CancellationToken ct)
    {
        NetworksCreateResponse net = await _direct.Networks.CreateNetworkAsync(
            new NetworksCreateParameters { Name = $"zwarden-itest-{Guid.NewGuid():N}", Driver = "bridge" }, ct);
        _networks.Add(net.ID);
        return net.ID;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (string id in _containers)
        {
            try
            {
                await _direct.Containers.RemoveContainerAsync(id, new ContainerRemoveParameters { Force = true });
            }
            catch (DockerApiException)
            {
                // Best-effort cleanup; a container already gone must not fail teardown.
            }
        }

        foreach (string net in _networks)
        {
            try
            {
                await _direct.Networks.DeleteNetworkAsync(net);
            }
            catch (DockerApiException)
            {
                // Best-effort cleanup.
            }
        }

        _direct.Dispose();
    }

    private sealed class FixedIdentity(AgentId agentId) : IAgentIdentity
    {
        public AgentId AgentId { get; } = agentId;
    }
}
