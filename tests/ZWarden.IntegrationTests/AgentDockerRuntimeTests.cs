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
/// wollomatic proxy carrying the deployment allowlist</b>, so any drift between what the Agent calls and what
/// the deployment permits fails the build — a denied verb (exec) is refused, and DELETE passes only for a canonical
/// srv-&lt;uuid&gt; name (ADR 0045).
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
    [Timeout(300_000)]
    public async Task Create_requires_both_bind_sources_and_ServerHostDirectories_prepares_them(CancellationToken ct)
    {
        // #184: the Docker Mounts API does NOT auto-create a bind source, so a canonical create with per-server
        // mount sources that do not yet exist is refused — and ServerHostDirectories (run by provisioning before
        // the create) is what makes it succeed. Regression guard for the whole provisioning-mount chain.
        await EnsureImageAsync(ct);
        string network = await CreateNetworkAsync(ct);
        ServerId server = ServerId.New();
        string root = Path.Combine(Path.GetTempPath(), $"zw-it-{Guid.NewGuid():N}");
        PzContainerSpec spec = SpecFor(server, network) with
        {
            DataMountSource = Path.Combine(root, server.ToString()),
            ServerMountSource = Path.Combine(root, $"{server}.server"),
        };
        ContainerRuntime runtime = DirectRuntime();

        try
        {
            // Sources absent → the daemon rejects the create ("bind source path does not exist").
            await Assert.ThrowsAsync<ContainerCreateException>(() => runtime.CreateAsync(spec, ct));

            // Prepare both sources exactly as provisioning does, then the same create succeeds.
            new ServerHostDirectories().EnsureCreated(spec);
            string id = await runtime.CreateAsync(spec, ct);
            _containers.Add(id);
            await Assert.That(id).IsNotNull();
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
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
                // ADR 0045 (#229): remove, but only a canonical srv-<uuid> NAME — a hex id or any other name is a 403.
                "-allowDELETE=(/v1\\.[0-9]+)?/containers/srv-[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}",
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
        ServerId server = ServerId.New();
        string id = await runtime.CreateAsync(SpecFor(server, network) with { ContainerName = server.ToString() }, ct);
        _containers.Add(id);
        await runtime.StartAsync(id, ct);

        // The F16 metrics read (GET /containers/{id}/stats?stream=false) is an allowlisted verb, so it
        // round-trips through the proxy without being refused — that is the point of the eleventh allowlist
        // entry (a refusal throws DockerApiException). Assert on the round-trip, not on any counter: a single
        // non-streaming read on a cgroup-v2 host (GitHub's runners) legitimately reports an all-zero snapshot
        // (memory unlimited → 0, and CPU % needs two samples), so no field is reliably non-zero (#127). This
        // is the allow-side companion to the denied-verb (403) assertions below.
        await Assert.That(async () => await new DockerDotNetEngine(proxied).StatsAsync(id, ct)).ThrowsNothing();

        await runtime.StopAsync(id, ct);

        // A denied verb (exec-create) is refused by the allowlist — proving the proxy really constrains the surface.
        // It lives under the /containers prefix (ADR 0008 trap 1) but matches no POST rule, so it is a path miss: 403.
        DockerApiException? denied = await Assert.ThrowsAsync<DockerApiException>(() =>
            proxied.Exec.CreateContainerExecAsync(id, new ContainerExecCreateParameters { Cmd = ["true"] }, ct));
        await Assert.That((int)denied!.StatusCode).IsEqualTo(403);

        // DELETE is admitted only for a canonical srv-<uuid> NAME (ADR 0045). The same container addressed by its hex
        // id, and a foreign container addressed by its own name, are both a path miss — 403 — so an Agent bug that
        // picks the wrong container still cannot delete it through the proxy.
        DockerApiException? byHexId = await Assert.ThrowsAsync<DockerApiException>(() =>
            proxied.Containers.RemoveContainerAsync(id, new ContainerRemoveParameters(), ct));
        await Assert.That((int)byHexId!.StatusCode).IsEqualTo(403);

        string foreignName = $"zwarden-itest-foreign-{Guid.NewGuid():N}";
        CreateContainerResponse foreign = await _direct.Containers.CreateContainerAsync(
            new CreateContainerParameters { Image = TestImage, Name = foreignName, Cmd = ["sleep", "300"] }, ct);
        _containers.Add(foreign.ID);
        DockerApiException? byForeignName = await Assert.ThrowsAsync<DockerApiException>(() =>
            proxied.Containers.RemoveContainerAsync(foreignName, new ContainerRemoveParameters(), ct));
        await Assert.That((int)byForeignName!.StatusCode).IsEqualTo(403);

        // The runtime's own remove — by ServerId name, stopped, owned — round-trips through the proxy.
        await runtime.RemoveAsync(server, ct);
        await Assert.That(await runtime.InspectServerAsync(server, ct)).IsNull();

        proxied.Dispose();
    }

    [Test]
    [Category("Networked")]
    [Timeout(300_000)]
    public async Task Log_follow_streams_multiplexed_stdout_and_stderr_live_through_the_allowlist_proxy(CancellationToken ct)
    {
        // F27's risk gate (docs/research/docker-socket-proxy.md §9 item 9): long-lived, multiplexed, hijacked log
        // streaming through wollomatic was never tested and is the likeliest place for an unpleasant surprise. Prove
        // it here — if it does not hold, the allowlist or the proxy choice gives, not the feature.
        await EnsureImageAsync(ct);

        // A container that writes a distinguishable line to BOTH stdout and stderr every second and never exits —
        // the shape of a live server's console, and exactly what a follow must keep delivering rather than block on.
        CreateContainerResponse chatty = await _direct.Containers.CreateContainerAsync(new CreateContainerParameters
        {
            Image = TestImage,
            Cmd = ["sh", "-c", "i=0; while true; do echo \"out-line $i\"; echo \"err-line $i\" 1>&2; i=$((i+1)); sleep 1; done"],
        }, ct);
        _containers.Add(chatty.ID);
        await _direct.Containers.StartContainerAsync(chatty.ID, new ContainerStartParameters(), ct);

        // The SAME §3.5 allowlist the deployment ships — `logs` is already permitted (path-only match), so a
        // follow=1 stream must ride the existing entry unchanged; no allowlist amendment (ADR 0008).
        await using IContainer proxy = new ContainerBuilder("wollomatic/socket-proxy:1.13.1")
            .WithBindMount("/var/run/docker.sock", "/var/run/docker.sock")
            .WithCreateParameterModifier(parameters => parameters.User = "0:0")
            .WithCommand(
                "-loglevel=INFO",
                "-listenip=0.0.0.0",
                "-allowfrom=0.0.0.0/0",
                "-allowGET=(/v1\\.[0-9]+)?/(_ping|version|info|containers/json|containers/[a-zA-Z0-9_.-]+/(json|logs|stats))",
                "-allowHEAD=(/v1\\.[0-9]+)?/_ping",
                "-allowPOST=(/v1\\.[0-9]+)?/(containers/create|containers/[a-zA-Z0-9_.-]+/(start|stop|restart))",
                // ADR 0045 (#229): remove, but only a canonical srv-<uuid> NAME — a hex id or any other name is a 403.
                "-allowDELETE=(/v1\\.[0-9]+)?/containers/srv-[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}",
                "-allowbindmountfrom=/tmp",
                "-watchdoginterval=0")
            .WithPortBinding(2375, assignRandomHostPort: true)
            .Build();
        await proxy.StartAsync(ct);

        DockerClient proxied = new DockerClientBuilder()
            .WithEndpoint(new Uri($"tcp://{proxy.Hostname}:{proxy.GetMappedPublicPort(2375)}"))
            .Build();
        await WaitUntilReachableAsync(RuntimeOver(new DockerDotNetEngine(proxied)), ct);

        // Follow through the proxy on a background task; collect frames until BOTH streams have arrived live
        // (incrementally, while the container keeps running) or the budget elapses.
        System.Collections.Concurrent.ConcurrentQueue<ContainerLogFrame> frames = new();
        using CancellationTokenSource follow = CancellationTokenSource.CreateLinkedTokenSource(ct);
        Task following = new DockerDotNetEngine(proxied).FollowLogsAsync(
            chatty.ID,
            tailLines: 0,
            (frame, _) =>
            {
                frames.Enqueue(frame);
                return ValueTask.CompletedTask;
            },
            follow.Token);

        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(60);
        while (DateTimeOffset.UtcNow < deadline)
        {
            ContainerLogFrame[] seen = frames.ToArray();
            if (seen.Count(f => !f.IsStderr && f.Text.StartsWith("out-line", StringComparison.Ordinal)) >= 2
                && seen.Count(f => f.IsStderr && f.Text.StartsWith("err-line", StringComparison.Ordinal)) >= 2)
            {
                break;
            }

            await Task.Delay(500, ct);
        }

        // Cancelling is how a subscription is torn down mid-stream — it must complete the follow cleanly, not throw.
        await follow.CancelAsync();
        await following;

        ContainerLogFrame[] all = frames.ToArray();
        await Assert.That(all.Count(f => !f.IsStderr && f.Text.StartsWith("out-line", StringComparison.Ordinal))).IsGreaterThanOrEqualTo(2);
        await Assert.That(all.Count(f => f.IsStderr && f.Text.StartsWith("err-line", StringComparison.Ordinal))).IsGreaterThanOrEqualTo(2);

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
        "/tmp",
        PortStrideAllocator.ForStride(0),
        64L * 1024 * 1024,
        32L * 1024 * 1024);

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
