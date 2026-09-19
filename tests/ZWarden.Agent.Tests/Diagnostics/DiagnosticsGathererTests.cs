using Microsoft.Extensions.Options;
using ZWarden.Agent.Configuration;
using ZWarden.Agent.Diagnostics;
using ZWarden.Agent.Docker;
using ZWarden.Agent.Health;
using ZWarden.Agent.Mods;
using ZWarden.Agent.Rcon;
using ZWarden.Agent.SteamCmd;
using ZWarden.Agent.Tests.Docker;
using ZWarden.Agent.Tests.Mods;
using ZWarden.Contracts.Protocol;
using ZWarden.Contracts.Protocol.Messages;
using ZWarden.Domain.Ids;
using ZWarden.PzConfig;
using ZWarden.PzConfig.Validation;

namespace ZWarden.Agent.Tests.Diagnostics;

/// <summary>
/// F29 PR-B: the read-only, fail-soft-per-domain Agent gatherers. Each domain check is wrapped so a probe fault
/// becomes a Fail/Warn check with a legible detail, never a thrown gather — one bad domain never sinks the bundle.
/// Proven against fakes of the F13/F16/F17/F18 seams, with no Docker, RCON, or network.
/// </summary>
public class DiagnosticsGathererTests
{
    // --- Host gatherer ---

    [Test]
    public async Task Host_gather_reports_docker_reachable_as_a_pass()
    {
        FakeContainerRuntime runtime = new() { Health = new DockerHealth(DaemonReachable: true, ApiVersion: "1.53", Detail: null) };
        HostDiagnosticsResult result = await HostGatherer(runtime, new StubDisk(Healthy)).GatherAsync(CancellationToken.None);

        await Assert.That(Check(result.Checks, DiagnosticDomain.Docker).Status).IsEqualTo(ProbeStatus.Pass);
    }

    [Test]
    public async Task Host_gather_reports_docker_unreachable_as_a_fail()
    {
        FakeContainerRuntime runtime = new() { Health = new DockerHealth(DaemonReachable: false, ApiVersion: null, Detail: "no socket") };
        HostDiagnosticsResult result = await HostGatherer(runtime, new StubDisk(Healthy)).GatherAsync(CancellationToken.None);

        DiagnosticCheckFact docker = Check(result.Checks, DiagnosticDomain.Docker);
        await Assert.That(docker.Status).IsEqualTo(ProbeStatus.Fail);
        await Assert.That(docker.Detail).IsEqualTo("no socket");
    }

    [Test]
    public async Task Host_gather_is_fail_soft_when_the_docker_probe_throws()
    {
        FakeContainerRuntime runtime = new() { ProbeException = new InvalidOperationException("boom") };
        HostDiagnosticsResult result = await HostGatherer(runtime, new StubDisk(Healthy)).GatherAsync(CancellationToken.None);

        // The gather still completes with a full bundle; the failing domain is a Fail check.
        await Assert.That(Check(result.Checks, DiagnosticDomain.Docker).Status).IsEqualTo(ProbeStatus.Fail);
        await Assert.That(Check(result.Checks, DiagnosticDomain.Filesystem)).IsNotNull();
    }

    [Test]
    public async Task Host_gather_fails_the_filesystem_when_the_data_root_is_missing()
    {
        string missing = Path.Combine(Path.GetTempPath(), $"zw-missing-{Guid.NewGuid():N}");
        HostDiagnosticsResult result = await HostGatherer(new FakeContainerRuntime(), new StubDisk(Healthy), missing)
            .GatherAsync(CancellationToken.None);

        await Assert.That(Check(result.Checks, DiagnosticDomain.Filesystem).Status).IsEqualTo(ProbeStatus.Fail);
    }

    [Test]
    public async Task Host_gather_warns_the_filesystem_when_disk_space_is_low()
    {
        string dir = TempDir();
        try
        {
            HostDiagnosticsResult result = await HostGatherer(new FakeContainerRuntime(), new StubDisk(Low), dir)
                .GatherAsync(CancellationToken.None);

            await Assert.That(Check(result.Checks, DiagnosticDomain.Filesystem).Status).IsEqualTo(ProbeStatus.Warn);
        }
        finally
        {
            Directory.Delete(dir);
        }
    }

    // --- Server gatherer ---

    [Test]
    public async Task Server_gather_reports_rcon_authenticated_as_a_pass()
    {
        ServerDiagnosticsResult result = await ServerGatherer(rcon: new RconHealthResult(true, true, null))
            .GatherAsync(ServerId.New(), CancellationToken.None);

        await Assert.That(Check(result.Checks, DiagnosticDomain.Rcon).Status).IsEqualTo(ProbeStatus.Pass);
    }

    [Test]
    public async Task Server_gather_reports_rcon_unusable_as_a_fail_with_detail()
    {
        ServerDiagnosticsResult result = await ServerGatherer(rcon: new RconHealthResult(false, false, "connection refused"))
            .GatherAsync(ServerId.New(), CancellationToken.None);

        DiagnosticCheckFact rcon = Check(result.Checks, DiagnosticDomain.Rcon);
        await Assert.That(rcon.Status).IsEqualTo(ProbeStatus.Fail);
        await Assert.That(rcon.Detail).IsEqualTo("connection refused");
    }

    [Test]
    public async Task Server_gather_skips_the_game_port_when_no_container_hosts_the_server()
    {
        ServerDiagnosticsResult result = await ServerGatherer()
            .GatherAsync(ServerId.New(), CancellationToken.None);

        await Assert.That(Check(result.Checks, DiagnosticDomain.GamePort).Status).IsEqualTo(ProbeStatus.Skipped);
    }

    [Test]
    public async Task Server_gather_passes_the_game_port_when_the_published_udp_port_is_reachable()
    {
        ServerId server = ServerId.New();
        FakeContainerRuntime runtime = new()
        {
            Observed = [new ObservedContainer(server, new ContainerHealthFacts(
                "running", "healthy", 0, false, [new PublishedPort(27015, 16261, "udp")],
                new Dictionary<string, string>(StringComparer.Ordinal) { ["zwarden"] = "172.22.0.3" }))],
        };
        ServerDiagnosticsResult result = await ServerGatherer(runtime: runtime, portReachable: true)
            .GatherAsync(server, CancellationToken.None);

        await Assert.That(Check(result.Checks, DiagnosticDomain.GamePort).Status).IsEqualTo(ProbeStatus.Pass);
    }

    [Test]
    public async Task Server_gather_skips_the_game_port_when_the_container_has_no_zwarden_network_address()
    {
        // #199: with no resolvable address on the ZWarden network, the probe is skipped rather than falsely failed.
        ServerId server = ServerId.New();
        FakeContainerRuntime runtime = new()
        {
            Observed = [new ObservedContainer(server, new ContainerHealthFacts(
                "running", "healthy", 0, false, [new PublishedPort(27015, 16261, "udp")], NetworkAddresses: null))],
        };
        ServerDiagnosticsResult result = await ServerGatherer(runtime: runtime, portReachable: false)
            .GatherAsync(server, CancellationToken.None);

        await Assert.That(Check(result.Checks, DiagnosticDomain.GamePort).Status).IsEqualTo(ProbeStatus.Skipped);
    }

    [Test]
    public async Task Server_gather_reports_an_installed_build_as_a_pass_and_a_missing_one_as_a_warn()
    {
        ServerDiagnosticsResult present = await ServerGatherer(buildId: "12345")
            .GatherAsync(ServerId.New(), CancellationToken.None);
        ServerDiagnosticsResult absent = await ServerGatherer(buildId: null)
            .GatherAsync(ServerId.New(), CancellationToken.None);

        await Assert.That(Check(present.Checks, DiagnosticDomain.SteamCmd).Status).IsEqualTo(ProbeStatus.Pass);
        await Assert.That(Check(absent.Checks, DiagnosticDomain.SteamCmd).Status).IsEqualTo(ProbeStatus.Warn);
    }

    [Test]
    public async Task Server_gather_fails_the_mod_domain_when_an_enabled_mod_is_missing()
    {
        FakeModDiscovery mods = new()
        {
            Result = new ModDiscoveryResult([], [], ["ghostmod"], [new ModCompatFinding(ModCompatKind.EnabledButMissing, "ghostmod", null)]),
        };
        ServerDiagnosticsResult result = await ServerGatherer(mods: mods).GatherAsync(ServerId.New(), CancellationToken.None);

        await Assert.That(Check(result.Checks, DiagnosticDomain.Mod).Status).IsEqualTo(ProbeStatus.Fail);
    }

    [Test]
    public async Task Server_gather_warns_the_compatibility_domain_on_a_duplicate_mod_id()
    {
        FakeModDiscovery mods = new()
        {
            Result = new ModDiscoveryResult([], [], ["dup"], [new ModCompatFinding(ModCompatKind.DuplicateModId, "dup", null)]),
        };
        ServerDiagnosticsResult result = await ServerGatherer(mods: mods).GatherAsync(ServerId.New(), CancellationToken.None);

        await Assert.That(Check(result.Checks, DiagnosticDomain.Compatibility).Status).IsEqualTo(ProbeStatus.Warn);
        await Assert.That(Check(result.Checks, DiagnosticDomain.Mod).Status).IsEqualTo(ProbeStatus.Pass);
    }

    [Test]
    public async Task Server_gather_warns_the_config_domain_when_there_is_no_config_file()
    {
        // The stub data root has no servertest.ini, so the config check warns rather than failing.
        ServerDiagnosticsResult result = await ServerGatherer().GatherAsync(ServerId.New(), CancellationToken.None);

        await Assert.That(Check(result.Checks, DiagnosticDomain.Config).Status).IsEqualTo(ProbeStatus.Warn);
    }

    // --- helpers ---

    private static readonly DiskUsage Healthy = new(UsedBytes: 10, CapacityBytes: 100);
    private static readonly DiskUsage Low = new(UsedBytes: 92, CapacityBytes: 100);

    private static DiagnosticCheckFact Check(IReadOnlyList<DiagnosticCheckFact> checks, DiagnosticDomain domain) =>
        checks.First(c => c.Domain == domain);

    private static string TempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"zw-diag-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static HostDiagnosticsGatherer HostGatherer(FakeContainerRuntime runtime, StubDisk disk, string? dataRoot = null) =>
        new(runtime, disk, Opts(dataRoot ?? TempDir()));

    private static ServerDiagnosticsGatherer ServerGatherer(
        RconHealthResult? rcon = null,
        FakeContainerRuntime? runtime = null,
        bool? portReachable = null,
        string? buildId = null,
        IModDiscovery? mods = null) =>
        new(
            new StubRcon(rcon ?? new RconHealthResult(true, true, null)),
            runtime ?? new FakeContainerRuntime(),
            new StubNetwork(portReachable),
            new StubDisk(Healthy),
            new StubInstallPaths(buildId),
            mods ?? new FakeModDiscovery(),
            new PzConfigParser(),
            new PzConfigValidator(),
            Opts(TempDir()));

    private static IOptions<AgentOptions> Opts(string dataRoot) => Options.Create(new AgentOptions
    {
        PzImageReference = "zwarden/pzserver:pinned",
        NetworkName = "zwarden",
        DataMountRoot = dataRoot,
        DefaultMemoryLimitBytes = 4L * 1024 * 1024 * 1024,
    });

    private sealed class StubRcon(RconHealthResult result) : IRconHealthProbe
    {
        public Task<RconHealthResult> ProbeAsync(ServerId serverId, CancellationToken cancellationToken) =>
            Task.FromResult(result);
    }

    private sealed class StubNetwork(bool? reachable) : INetworkReachabilityProbe
    {
        public Task<bool?> IsUdpPortReachableAsync(string host, int port, CancellationToken cancellationToken) =>
            Task.FromResult(reachable);
    }

    private sealed class StubDisk(DiskUsage usage) : IServerDiskUsageReader
    {
        public DiskUsage Read(string dataDirectory) => usage;
    }

    private sealed class StubInstallPaths(string? buildId) : IServerInstallPaths
    {
        public void WriteUpdateRequest(ServerId serverId, OperationId operationId) { }

        public string? ReadInstalledBuildId(ServerId serverId) => buildId;

        public string GetWorkshopContentRoot(ServerId serverId) => "/pz/server/steamapps/workshop/content/108600";
    }
}
