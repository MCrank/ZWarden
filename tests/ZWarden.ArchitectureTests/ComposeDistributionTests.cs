namespace ZWarden.ArchitectureTests;

/// <summary>
/// F34 (ADR 0037): an offline guard that the reference Docker Compose distribution stays present and coherent.
/// The end-to-end boot is proven by the networked smoke test (schedule/dispatch only, PR-B); this runs on every
/// PR so the shipped <c>deploy/compose/</c> stack — its topology, the non-root images, the health-ordered
/// startup, the SQLite wiring, and the wollomatic allowlist — cannot silently disappear or drift.
///
/// Textual assertions (not a YAML object model) match the sibling <see cref="HttpsReferenceDeploymentTests"/>
/// style: cheap, dependency-free, and enough to catch the deletions and edits that would break a deployment.
/// </summary>
public class ComposeDistributionTests
{
    private static string ComposeDir() => Path.Combine(RepoRoot(), "deploy", "compose");

    private static async Task<string> ComposeYamlAsync() =>
        await File.ReadAllTextAsync(Path.Combine(ComposeDir(), "compose.yaml"));

    [Test]
    public async Task Compose_defines_the_four_long_lived_services()
    {
        string compose = await ComposeYamlAsync();

        await Assert.That(compose).Contains("caddy:");
        await Assert.That(compose).Contains("web:");
        await Assert.That(compose).Contains("agent:");
        await Assert.That(compose).Contains("wollomatic:");
    }

    [Test]
    public async Task Only_caddy_publishes_ports_web_and_wollomatic_are_never_host_exposed()
    {
        string compose = await ComposeYamlAsync();

        // Caddy is the sole front door (ADR 0035): it publishes 80/443.
        await Assert.That(compose).Contains("\"80:80\"");
        await Assert.That(compose).Contains("\"443:443\"");
        // Web is reached only through Caddy (PRD §45) — it must not host-publish its 8080.
        await Assert.That(compose).DoesNotContain("8080:8080");
        // wollomatic's 2375 is Agent-private (ADR 0008) — never published to the host.
        await Assert.That(compose).DoesNotContain("2375:2375");
    }

    [Test]
    public async Task Web_and_agent_build_from_their_dockerfiles()
    {
        string compose = await ComposeYamlAsync();

        await Assert.That(compose).Contains("src/ZWarden.Web/Dockerfile");
        await Assert.That(compose).Contains("src/ZWarden.Agent/Dockerfile");
    }

    [Test]
    public async Task Sqlite_is_the_base_mode_persisted_to_a_named_volume()
    {
        string compose = await ComposeYamlAsync();

        // Default provider is SQLite (base mode, D-2); Postgres is the PR-B overlay, not the base.
        await Assert.That(compose).Contains("ZWarden__Database__Provider=sqlite");
        await Assert.That(compose).DoesNotContain("ZWarden__Database__Provider=postgres");
        // The database file lives on a persisted volume, not the container's writable layer.
        await Assert.That(compose).Contains("Data Source=/data/zwarden.db");
    }

    [Test]
    public async Task The_fail_closed_key_ring_is_wired_from_the_environment()
    {
        string compose = await ComposeYamlAsync();

        // ADR 0015: Web fails closed without the key ring — it is sourced from .env, never baked in.
        await Assert.That(compose).Contains("ZW_SECRET_KEYS=${ZW_SECRET_KEYS}");
        await Assert.That(compose).Contains("ZW_SECRET_ACTIVE_KEY_ID=${ZW_SECRET_ACTIVE_KEY_ID}");
    }

    [Test]
    public async Task The_colocated_agent_reaches_the_control_plane_over_wss_through_caddy()
    {
        string compose = await ComposeYamlAsync();

        // D-8: the Agent's control-plane URI is wss through Caddy (F8 validator requires https/wss), and its
        // Docker path runs through wollomatic, never the raw socket (ADR 0008).
        await Assert.That(compose).Contains("Agent__ControlPlaneUri=wss://${ZWARDEN_DOMAIN}");
        await Assert.That(compose).Contains("Agent__DockerEndpoint=tcp://wollomatic:2375");
        // Enrollment is operator-driven via the F33 wizard (D-4): the secret comes from .env, unset by default.
        await Assert.That(compose).Contains("Agent__EnrollmentSecret=${ZWARDEN_ENROLLMENT_SECRET");
    }

    [Test]
    public async Task Wollomatic_ships_the_canonical_allowlist_verbatim()
    {
        string compose = await ComposeYamlAsync();

        // The SAME allowlist the F13 drift tests enforce (AgentDockerRuntimeTests) — NOT relaxed for the
        // deployment. Any edit here must be matched there, and vice versa (ADR 0008 as amended by F16).
        await Assert.That(compose).Contains("-allowGET=(/v1\\.[0-9]+)?/(_ping|version|info|containers/json|containers/[a-zA-Z0-9_.-]+/(json|logs|stats))");
        await Assert.That(compose).Contains("-allowHEAD=(/v1\\.[0-9]+)?/_ping");
        await Assert.That(compose).Contains("-allowPOST=(/v1\\.[0-9]+)?/(containers/create|containers/[a-zA-Z0-9_.-]+/(start|stop|restart))");
        // Bind sources are constrained to the persistent PZ data root (ADR 0008 amended by #184; was /tmp).
        await Assert.That(compose).Contains("-allowbindmountfrom=/srv/zwarden");
        await Assert.That(compose).DoesNotContain("-allowbindmountfrom=/tmp");
    }

    [Test]
    public async Task Agent_shares_the_persistent_pz_data_root_as_host_binds_at_the_same_path()
    {
        string compose = await ComposeYamlAsync();

        // #184: the PZ data/backup roots are a PERSISTENT host path (off /tmp), bind-mounted into the Agent at
        // the SAME path both sides so the paths the Agent hands the daemon resolve as host bind sources.
        await Assert.That(compose).Contains("Agent__DataMountRoot=/srv/zwarden/pz-data");
        await Assert.That(compose).Contains("Agent__BackupRoot=/srv/zwarden/pz-backups");
        await Assert.That(compose).Contains("/srv/zwarden/pz-data:/srv/zwarden/pz-data");
        await Assert.That(compose).Contains("/srv/zwarden/pz-backups:/srv/zwarden/pz-backups");
        // The data root must NOT be a named volume (that would not resolve as a host bind source).
        await Assert.That(compose).DoesNotContain("Agent__DataMountRoot=/tmp");
    }

    [Test]
    public async Task The_pz_data_roots_are_prepared_with_shared_agent_and_game_server_ownership()
    {
        string compose = await ComposeYamlAsync();

        // #184: a one-shot init prepares the shared tree — owned by the Agent uid with the PZ game-server gid,
        // setgid + group-writable (2775) so both can read/write it. The Agent waits for it to complete.
        await Assert.That(compose).Contains("pz-data-init:");
        await Assert.That(compose).Contains("chown 10001:10000");
        await Assert.That(compose).Contains("chmod 2775");
        await Assert.That(compose).Contains("service_completed_successfully");
        // The Agent runs as the Agent uid with the PZ game-server gid so the shared tree works through the group.
        await Assert.That(compose).Contains("user: \"10001:10000\"");
    }

    [Test]
    public async Task Agent_joins_the_pz_game_server_network_for_provisioning_and_rcon()
    {
        string compose = await ComposeYamlAsync();

        // #184: nothing else attaches to zwarden-pz, so the Agent must — both to make Compose actually create
        // the network and so the Agent can reach each PZ container's RCON port (F18/F19/F28).
        await Assert.That(compose).Contains("zwarden-pz");
    }

    [Test]
    public async Task Wollomatic_allowfrom_is_scoped_to_the_internal_network_not_the_world()
    {
        string compose = await ComposeYamlAsync();

        // Prod tightening over the dev graph's 0.0.0.0/0: only the docker-proxy subnet may reach the proxy.
        await Assert.That(compose).DoesNotContain("-allowfrom=0.0.0.0/0");
        await Assert.That(compose).Contains("-allowfrom=");
    }

    [Test]
    public async Task The_docker_proxy_network_is_internal_only()
    {
        string compose = await ComposeYamlAsync();

        // wollomatic must have no route to the Internet (ADR 0008): its network is `internal: true`.
        await Assert.That(compose).Contains("internal: true");
    }

    [Test]
    public async Task Startup_is_health_ordered_on_the_web_liveness_probe()
    {
        string compose = await ComposeYamlAsync();

        // Web carries a healthcheck; its dependents (Caddy, the Agent) wait for it to be healthy, not merely
        // started (D-5). Leaf services nothing depends on (Caddy, the Agent) need no healthcheck of their own.
        await Assert.That(compose).Contains("healthcheck:");
        await Assert.That(compose).Contains("condition: service_healthy");
    }

    [Test]
    public async Task Web_exposes_an_unauthenticated_liveness_endpoint_for_its_container_probe()
    {
        string compose = await ComposeYamlAsync();

        // D-5: the container liveness probe hits /healthz — a dependency-free endpoint distinct from F16's
        // authenticated PZ-server health model.
        await Assert.That(compose).Contains("/healthz");
    }

    [Test]
    public async Task Env_example_documents_every_operator_supplied_setting()
    {
        string envExample = await File.ReadAllTextAsync(Path.Combine(ComposeDir(), ".env.example"));

        await Assert.That(envExample).Contains("ZWARDEN_DOMAIN=");
        await Assert.That(envExample).Contains("ZW_SECRET_KEYS=");
        await Assert.That(envExample).Contains("ZW_SECRET_ACTIVE_KEY_ID=");
        await Assert.That(envExample).Contains("ZWARDEN_ENROLLMENT_SECRET=");
    }

    [Test]
    public async Task Dotenv_is_git_ignored_so_generated_secrets_never_get_committed()
    {
        string gitignore = await File.ReadAllTextAsync(Path.Combine(ComposeDir(), ".gitignore"));

        await Assert.That(gitignore).Contains(".env");
    }

    [Test]
    public async Task Secrets_bootstrap_ships_for_both_posix_and_windows_hosts()
    {
        await Assert.That(File.Exists(Path.Combine(ComposeDir(), "bootstrap-secrets.sh"))).IsTrue();
        await Assert.That(File.Exists(Path.Combine(ComposeDir(), "bootstrap-secrets.ps1"))).IsTrue();
    }

    [Test]
    public async Task Postgres_overlay_adds_the_database_service_and_flips_the_provider()
    {
        string overlay = await File.ReadAllTextAsync(Path.Combine(ComposeDir(), "compose.postgres.yaml"));

        // PostgreSQL mode is an override file (D-2), not a profile: it adds the postgres service and overrides
        // only Web's provider + connection string.
        await Assert.That(overlay).Contains("postgres:");
        await Assert.That(overlay).Contains("ZWarden__Database__Provider=postgres");
        // The Npgsql connection string points Web at the postgres service, sourcing the password from .env.
        await Assert.That(overlay).Contains("Host=postgres");
        await Assert.That(overlay).Contains("${POSTGRES_PASSWORD}");
        // Web must wait for the database to be healthy, not merely started.
        await Assert.That(overlay).Contains("condition: service_healthy");
    }

    [Test]
    public async Task Postgres_overlay_keeps_the_database_off_the_public_internet()
    {
        string overlay = await File.ReadAllTextAsync(Path.Combine(ComposeDir(), "compose.postgres.yaml"));

        // The database is never host-published, and it sits on an internal-only network.
        await Assert.That(overlay).DoesNotContain("5432:5432");
        await Assert.That(overlay).Contains("internal: true");
        // A real readiness probe so the health-gated ordering means something.
        await Assert.That(overlay).Contains("pg_isready");
    }

    [Test]
    public async Task Deployment_guide_documents_both_database_modes_and_the_quick_start()
    {
        string guide = await DeploymentGuideAsync();

        await Assert.That(guide).Contains("bootstrap-secrets");
        await Assert.That(guide).Contains("docker compose up -d");
        await Assert.That(guide).Contains("compose.postgres.yaml");
        // The post-bring-up path: the operator finishes in the F33 wizard.
        await Assert.That(guide).Contains("First-Run");
    }

    [Test]
    public async Task Deployment_guide_documents_upgrades_and_private_mode_agent_ca_trust()
    {
        string guide = await DeploymentGuideAsync();

        await Assert.That(guide).Contains("Upgrade");
        // The one genuinely non-obvious gotcha (D-8): the co-located Agent must trust Caddy's CA in Private mode.
        await Assert.That(guide).Contains("Private");
        await Assert.That(guide).Contains("CA");
    }

    private static async Task<string> DeploymentGuideAsync() =>
        await File.ReadAllTextAsync(Path.Combine(RepoRoot(), "docs", "deployment", "compose-reference.md"));

    [Test]
    public async Task The_app_images_run_non_root()
    {
        string webDockerfile = await File.ReadAllTextAsync(Path.Combine(RepoRoot(), "src", "ZWarden.Web", "Dockerfile"));
        string agentDockerfile = await File.ReadAllTextAsync(Path.Combine(RepoRoot(), "src", "ZWarden.Agent", "Dockerfile"));

        // PRD 24 / defense-in-depth: neither control-plane image runs as root.
        await Assert.That(webDockerfile).Contains("USER ");
        await Assert.That(agentDockerfile).Contains("USER ");
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
