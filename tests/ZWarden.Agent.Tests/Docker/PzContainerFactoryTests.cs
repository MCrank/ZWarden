using Docker.DotNet.Models;
using ZWarden.Agent.Docker;
using ZWarden.Domain.Ids;

namespace ZWarden.Agent.Tests.Docker;

/// <summary>
/// F13 S4: the create-body invariants (ADR 0008, research §5.3). No socket proxy can constrain the create
/// body, so each of the twelve invariants is asserted here — and item 1 (never privileged) protects the
/// <b>host</b>, not merely other containers. The factory stamps the owning Agent id from the Agent's own
/// identity, never from the caller. Docker model properties are nullable; the factory always sets them, so the
/// tests use the null-forgiving operator at those known-set accesses.
/// </summary>
public class PzContainerFactoryTests
{
    private static readonly AgentId Self = AgentId.New();
    private static readonly ServerId Server = ServerId.New();
    private const string PinnedImage = "ghcr.io/mcrank/zwarden-pzserver@sha256:abc";
    private const long MemoryLimit = 6L * 1024 * 1024 * 1024;

    private static PzContainerSpec ValidSpec(
        string? image = null, string? network = null, string? mount = null, string? serverMount = null, long? memory = null) =>
        new(
            Server,
            "zwarden-srv-abc",
            image ?? PinnedImage,
            network ?? "zwarden-pz",
            mount ?? "/srv/zwarden/servers/abc/data",
            serverMount ?? "/srv/zwarden/servers/abc.server",
            PortStrideAllocator.ForStride(0),
            memory ?? MemoryLimit);

    private static CreateContainerParameters Build(PzContainerSpec spec) =>
        new PzContainerFactory(new FixedAgentIdentity(Self)).Build(spec);

    private static HostConfig Host(PzContainerSpec spec) => Build(spec).HostConfig!;

    [Test]
    public async Task Invariant1_the_container_is_never_privileged()
    {
        await Assert.That(Host(ValidSpec()).Privileged).IsFalse();
    }

    [Test]
    public async Task Invariant2_no_capabilities_are_added_and_all_are_dropped()
    {
        HostConfig host = Host(ValidSpec());

        await Assert.That(host.CapAdd!).IsEmpty();
        await Assert.That(host.CapDrop!).Contains("ALL");
    }

    [Test]
    public async Task Invariant3_no_new_privileges_is_set()
    {
        await Assert.That(Host(ValidSpec()).SecurityOpt!).Contains("no-new-privileges:true");
    }

    [Test]
    public async Task Invariant4_the_network_is_the_named_zwarden_network()
    {
        await Assert.That(Host(ValidSpec()).NetworkMode).IsEqualTo("zwarden-pz");
    }

    [Test]
    public async Task Invariant5_no_host_namespaces_are_shared()
    {
        // The Docker model defaults these to empty; "unset, and never host" is the invariant.
        HostConfig host = Host(ValidSpec());

        foreach (string? mode in new[] { host.PidMode, host.IpcMode, host.UsernsMode, host.UTSMode, host.CgroupnsMode })
        {
            await Assert.That(string.IsNullOrEmpty(mode)).IsTrue();
            await Assert.That(mode).IsNotEqualTo("host");
        }
    }

    [Test]
    public async Task Invariant6_no_devices_are_passed_through()
    {
        await Assert.That(Host(ValidSpec()).Devices!).IsEmpty();
    }

    [Test]
    public async Task Invariant7_the_writable_binds_are_exactly_data_and_server()
    {
        HostConfig host = Host(ValidSpec());

        await Assert.That(host.Binds!).IsEmpty();
        await Assert.That(host.Mounts!).Count().IsEqualTo(2);

        Mount data = host.Mounts!.Single(m => m.Target == "/pz/data");
        await Assert.That(data.Type).IsEqualTo("bind");
        await Assert.That(data.Source).IsEqualTo("/srv/zwarden/servers/abc/data");
        await Assert.That(data.ReadOnly).IsFalse();

        Mount server = host.Mounts!.Single(m => m.Target == "/pz/server");
        await Assert.That(server.Type).IsEqualTo("bind");
        await Assert.That(server.Source).IsEqualTo("/srv/zwarden/servers/abc.server");
        await Assert.That(server.ReadOnly).IsFalse();
    }

    [Test]
    public async Task Invariant7_the_runtime_is_an_ephemeral_exec_tmpfs()
    {
        // SteamCMD's self-updating client + the stdin FIFO need writable, exec-capable storage, but nothing
        // under /pz/runtime must survive a recreate (F17). It is a tmpfs, never a bind, never a persistent mount.
        HostConfig host = Host(ValidSpec());

        await Assert.That(host.Tmpfs!.ContainsKey("/pz/runtime")).IsTrue();
        await Assert.That(host.Tmpfs!["/pz/runtime"]).Contains("exec");
        await Assert.That(host.Mounts!.Any(m => m.Target == "/pz/runtime")).IsFalse();
    }

    [Test]
    public async Task Invariant8_the_root_filesystem_is_read_only()
    {
        await Assert.That(Host(ValidSpec()).ReadonlyRootfs).IsTrue();
    }

    [Test]
    public async Task Invariant9_the_image_is_the_pinned_reference_verbatim()
    {
        await Assert.That(Build(ValidSpec()).Image).IsEqualTo(PinnedImage);
    }

    [Test]
    public async Task Invariant10_the_full_canonical_label_set_is_stamped_with_this_agents_id()
    {
        IDictionary<string, string> labels = Build(ValidSpec()).Labels!;

        await Assert.That(labels[CanonicalLabels.Managed]).IsEqualTo(CanonicalLabels.ManagedValue);
        await Assert.That(labels[CanonicalLabels.Runtime]).IsEqualTo(CanonicalLabels.RuntimeValue);
        await Assert.That(labels[CanonicalLabels.SchemaVersion]).IsEqualTo(CanonicalLabels.SchemaVersionValue);
        await Assert.That(labels[CanonicalLabels.ServerId]).IsEqualTo(Server.ToString());
        await Assert.That(labels[CanonicalLabels.AgentId]).IsEqualTo(Self.ToString());
    }

    [Test]
    public async Task Invariant11_the_user_is_non_root_and_a_memory_limit_is_set()
    {
        CreateContainerParameters p = Build(ValidSpec());

        await Assert.That(p.User).IsEqualTo("10000:10000");
        await Assert.That(p.HostConfig!.Memory).IsEqualTo(MemoryLimit);
    }

    [Test]
    public async Task Invariant12_only_the_two_udp_ports_are_bound_and_rcon_is_not()
    {
        CreateContainerParameters p = Build(ValidSpec());

        await Assert.That(p.ExposedPorts!.ContainsKey("16261/udp")).IsTrue();
        await Assert.That(p.ExposedPorts!.ContainsKey("16262/udp")).IsTrue();
        await Assert.That(p.ExposedPorts!.ContainsKey("27015/tcp")).IsFalse();

        IDictionary<string, IList<PortBinding>> bindings = p.HostConfig!.PortBindings!;
        await Assert.That(bindings["16261/udp"][0].HostPort).IsEqualTo("16261");
        await Assert.That(bindings["16262/udp"][0].HostPort).IsEqualTo("16262");
        await Assert.That(bindings.ContainsKey("27015/tcp")).IsFalse();
    }

    [Test]
    public async Task A_floating_latest_tag_is_rejected()
    {
        await Assert.That(() => Build(ValidSpec(image: "zwarden-pzserver:latest"))).Throws<ArgumentException>();
    }

    [Test]
    [Arguments("host")]
    [Arguments("none")]
    [Arguments("container:other")]
    public async Task A_dangerous_network_is_rejected(string network)
    {
        await Assert.That(() => Build(ValidSpec(network: network))).Throws<ArgumentException>();
    }

    [Test]
    public async Task A_relative_mount_source_is_rejected()
    {
        await Assert.That(() => Build(ValidSpec(mount: "relative/path"))).Throws<ArgumentException>();
    }

    [Test]
    public async Task A_relative_server_mount_source_is_rejected()
    {
        await Assert.That(() => Build(ValidSpec(serverMount: "relative/server"))).Throws<ArgumentException>();
    }

    [Test]
    public async Task A_non_positive_memory_limit_is_rejected()
    {
        await Assert.That(() => Build(ValidSpec(memory: 0))).Throws<ArgumentException>();
    }
}
