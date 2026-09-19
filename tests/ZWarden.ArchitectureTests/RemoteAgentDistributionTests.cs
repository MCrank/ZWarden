namespace ZWarden.ArchitectureTests;

/// <summary>
/// F35 (ADR 0038): an offline guard that the remote-Agent Docker Compose distribution stays present and
/// coherent. A remote Agent is the SAME ZWarden.Agent component on a second host, reaching an existing control
/// plane OUTBOUND over wss with no privileged inbound port (criterion 14). This runs on every PR so the shipped
/// <c>deploy/compose/remote-agent/</c> stack — its Agent-and-wollomatic-only topology, its outbound-wss wiring,
/// and the wollomatic allowlist it shares verbatim with F13/F34 — cannot silently drift.
///
/// Textual assertions (not a YAML object model) match the sibling <see cref="ComposeDistributionTests"/> style.
/// </summary>
public class RemoteAgentDistributionTests
{
    private static string RemoteDir() => Path.Combine(RepoRoot(), "deploy", "compose", "remote-agent");

    private static async Task<string> ComposeYamlAsync() =>
        await File.ReadAllTextAsync(Path.Combine(RemoteDir(), "compose.yaml"));

    [Test]
    public async Task It_ships_only_the_agent_and_its_socket_proxy_no_control_plane()
    {
        string compose = await ComposeYamlAsync();

        // The host worker and the proxy its Docker path runs through — and nothing else. The control plane
        // (Caddy + Web) already runs on another host, so it must NOT be duplicated here.
        await Assert.That(compose).Contains("agent:");
        await Assert.That(compose).Contains("wollomatic:");
        await Assert.That(compose).DoesNotContain("caddy:");
        await Assert.That(compose).DoesNotContain("web:");
    }

    [Test]
    public async Task The_agent_reaches_the_control_plane_outbound_over_wss_on_the_public_domain()
    {
        string compose = await ComposeYamlAsync();

        // Outbound wss to the REMOTE public control plane (F8 requires https/wss); resolved over public DNS,
        // NOT an in-stack Caddy alias (there is no Caddy here). Its Docker path runs through wollomatic.
        await Assert.That(compose).Contains("Agent__ControlPlaneUri=wss://${ZWARDEN_DOMAIN}");
        await Assert.That(compose).Contains("Agent__DockerEndpoint=tcp://wollomatic:2375");
        // Operator-driven, single-use enrollment (F9): the secret comes from .env, unset by default.
        await Assert.That(compose).Contains("Agent__EnrollmentSecret=${ZWARDEN_ENROLLMENT_SECRET");
    }

    [Test]
    public async Task No_service_publishes_a_host_port_so_the_host_needs_no_inbound_rule()
    {
        string compose = await ComposeYamlAsync();

        // Criterion 14: the whole point is no privileged inbound port on the Agent host. Nothing publishes.
        await Assert.That(compose).DoesNotContain("ports:");
        // wollomatic's 2375 is Agent-private (ADR 0008), never host-published.
        await Assert.That(compose).DoesNotContain("2375:2375");
    }

    [Test]
    public async Task The_agent_reuses_the_f34_agent_image_and_dockerfile()
    {
        string compose = await ComposeYamlAsync();

        // The same component, not a new one: it builds from the same Agent Dockerfile / image tag as F34.
        await Assert.That(compose).Contains("src/ZWarden.Agent/Dockerfile");
        await Assert.That(compose).Contains("zwarden/agent:latest");
    }

    [Test]
    public async Task Identity_and_trust_persist_so_a_restart_never_re_enrols()
    {
        string compose = await ComposeYamlAsync();

        // F9: the per-Agent credential lives on a named volume, so a container restart reuses it (no re-enrol).
        await Assert.That(compose).Contains("Agent__IdentityFilePath=");
        await Assert.That(compose).Contains("Agent__TrustFilePath=");
        await Assert.That(compose).Contains("agent_state:/var/lib/zwarden");
    }

    [Test]
    public async Task Wollomatic_ships_the_canonical_allowlist_verbatim()
    {
        string compose = await ComposeYamlAsync();

        // The SAME allowlist the F13 drift tests and the F34 reference compose enforce — NOT relaxed for a
        // remote host. Any edit here must be matched there, and vice versa (ADR 0008 as amended by F16).
        await Assert.That(compose).Contains("-allowGET=(/v1\\.[0-9]+)?/(_ping|version|info|containers/json|containers/[a-zA-Z0-9_.-]+/(json|logs|stats))");
        await Assert.That(compose).Contains("-allowHEAD=(/v1\\.[0-9]+)?/_ping");
        await Assert.That(compose).Contains("-allowPOST=(/v1\\.[0-9]+)?/(containers/create|containers/[a-zA-Z0-9_.-]+/(start|stop|restart))");
        // Bind sources constrained to the persistent PZ data root (ADR 0008 amended by #184; was /tmp).
        await Assert.That(compose).Contains("-allowbindmountfrom=/srv/zwarden");
        await Assert.That(compose).DoesNotContain("-allowbindmountfrom=/tmp");
        // And still scoped to the internal subnet, never the world.
        await Assert.That(compose).DoesNotContain("-allowfrom=0.0.0.0/0");
    }

    [Test]
    public async Task It_prepares_and_shares_the_persistent_pz_data_root_like_the_reference_stack()
    {
        string compose = await ComposeYamlAsync();

        // #184: identical data-plane wiring to the co-located stack — persistent host binds at the same path,
        // a one-shot init that sets the shared Agent/PZ ownership, the Agent on the PZ network, run 10001:10000.
        await Assert.That(compose).Contains("Agent__DataMountRoot=/srv/zwarden/pz-data");
        await Assert.That(compose).Contains("/srv/zwarden/pz-data:/srv/zwarden/pz-data");
        await Assert.That(compose).Contains("pz-data-init:");
        await Assert.That(compose).Contains("chown 10001:10000");
        await Assert.That(compose).Contains("chmod 2775");
        await Assert.That(compose).Contains("user: \"10001:10000\"");
        await Assert.That(compose).Contains("zwarden-pz");
    }

    [Test]
    public async Task The_docker_proxy_network_is_internal_only()
    {
        string compose = await ComposeYamlAsync();

        // wollomatic must have no route to the Internet (ADR 0008): its network is `internal: true`.
        await Assert.That(compose).Contains("internal: true");
    }

    [Test]
    public async Task The_env_template_documents_the_domain_and_the_one_time_enrollment_secret()
    {
        string envExample = await File.ReadAllTextAsync(Path.Combine(RemoteDir(), ".env.example"));

        await Assert.That(envExample).Contains("ZWARDEN_DOMAIN=");
        await Assert.That(envExample).Contains("ZWARDEN_ENROLLMENT_SECRET=");
        // A remote Agent holds no key ring — the control plane's secrets must NOT appear here.
        await Assert.That(envExample).DoesNotContain("ZW_SECRET_KEYS=");
    }

    [Test]
    public async Task Dotenv_is_git_ignored_and_an_env_bootstrap_ships_for_both_host_families()
    {
        string gitignore = await File.ReadAllTextAsync(Path.Combine(RemoteDir(), ".gitignore"));
        await Assert.That(gitignore).Contains(".env");

        await Assert.That(File.Exists(Path.Combine(RemoteDir(), "bootstrap-env.sh"))).IsTrue();
        await Assert.That(File.Exists(Path.Combine(RemoteDir(), "bootstrap-env.ps1"))).IsTrue();
    }

    [Test]
    public async Task The_remote_agent_guide_documents_enrol_verify_and_private_mode_ca_trust()
    {
        string guide = await File.ReadAllTextAsync(Path.Combine(RepoRoot(), "docs", "deployment", "remote-agent.md"));

        await Assert.That(guide).Contains("docker compose up -d");
        // Enrol on the control plane, then confirm the host shows up in the operator inventory.
        await Assert.That(guide).Contains("/hosts");
        await Assert.That(guide).Contains("enrol");
        // The one genuinely non-obvious gotcha: a Private (tls internal) control plane needs its CA trusted.
        await Assert.That(guide).Contains("Private");
        await Assert.That(guide).Contains("CA");
    }

    private static string RepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ZWarden.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Could not locate the repository root (ZWarden.slnx).");
    }
}
