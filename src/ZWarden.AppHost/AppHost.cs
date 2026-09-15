// ZWarden dev/test orchestration AppHost (#123, ADR 0031). DEV/TEST ONLY — see README.md.
//
// The full inner-loop graph: Postgres + ZWarden.Web (control plane) + ZWarden.Agent (host worker) +
// the wollomatic Docker socket-proxy the Agent's Docker path runs through (ADR 0008). Connection
// strings, endpoints, and the dev-only secrets the hosts fail closed without are all injected, so
// `aspire run` boots a dependency-complete graph that self-enrolls the Agent with no manual steps.
// PR-3 adds the Aspire.Hosting.Testing integration boot.
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

IDistributedApplicationBuilder builder = DistributedApplication.CreateBuilder(args);

// --- Dev-only secrets (throwaway, never production paths; ADR 0015/0031) ---------------------------

// A STABLE, well-known dev postgres password. A persisted data volume (below) bakes in the password
// from its first init and ignores POSTGRES_PASSWORD afterwards, so Aspire's default per-run generated
// password would desync from the volume and fail auth ("password authentication failed for user
// postgres"). Pinning a fixed dev value keeps the volume authenticating across runs, machines, and
// user-secrets resets. Production Postgres is provisioned outside Aspire (F34 owns deployment).
IResourceBuilder<ParameterResource> postgresPassword =
    builder.AddParameter("postgres-password", value: "zwarden-dev-only", secret: true);

// A STABLE, deterministic DEV-ONLY key ring — 32 fixed ASCII bytes, plainly not a production key. Pinned
// (not per-run) so key-ring-encrypted columns (e.g. Identity/MFA token secrets, ADR 0015) in the
// PERSISTENT dev database stay decryptable across runs. Production sets ZW_SECRET_KEYS from a real secret
// and fails closed without one; the dev value never authenticates there.
string devKeyRing = Convert.ToBase64String("zwarden-dev-key-ring-0123456789!"u8.ToArray());

// The well-known dev enrollment secret. Injected into BOTH Web (which seeds a matching redeemable
// enrollment in Development via DevEnrollmentBootstrapper) and the Agent (which presents it once at
// /agent/enroll), so the graph self-enrolls on boot. Fenced to Development on the Web side and refused
// outside it (D-ENROLL guard). Never a production credential.
const string devEnrollmentSecret = "zwe_dev-aspire-do-not-use-in-prod";

// --- Data -----------------------------------------------------------------------------------------

// D-CONN (ADR 0031): the database resource is named "ZWarden" so Aspire injects the environment
// variable ConnectionStrings__ZWarden — exactly the key ZWarden.Web reads via
// GetConnectionString("ZWarden") (Program.cs). WithDataVolume persists the dev database (admin,
// servers, config, enrollment, one-time migrations) across runs; the pinned password makes that reliable.
IResourceBuilder<PostgresDatabaseResource> database = builder
    .AddPostgres("postgres", password: postgresPassword)
    .WithDataVolume()
    .AddDatabase("ZWarden");

// --- wollomatic Docker socket-proxy (D-WOLLO, ADR 0008) -------------------------------------------

// The Agent's Docker path runs through the proxy exactly as in production. The allowlist below is the
// SAME set the F13 drift test enforces (tests/ZWarden.IntegrationTests/AgentDockerRuntimeTests.cs) — it
// is NOT relaxed for dev ergonomics. Distroless image, pinned; it reads the root-owned socket so it runs
// as root, and listens on 2375. (Consequence of denying /images/*: creating a PZ server needs the image
// pre-provisioned on the daemon — the dev graph only needs the Agent's Docker path reachable.)
IResourceBuilder<ContainerResource> wollomatic = builder
    .AddContainer("wollomatic", "wollomatic/socket-proxy", "1.13.1")
    .WithBindMount("/var/run/docker.sock", "/var/run/docker.sock")
    .WithContainerRuntimeArgs("--user", "0:0")
    .WithArgs(
        "-loglevel=INFO",
        "-listenip=0.0.0.0",
        "-allowfrom=0.0.0.0/0",
        @"-allowGET=(/v1\.[0-9]+)?/(_ping|version|info|containers/json|containers/[a-zA-Z0-9_.-]+/(json|logs|stats))",
        @"-allowHEAD=(/v1\.[0-9]+)?/_ping",
        @"-allowPOST=(/v1\.[0-9]+)?/(containers/create|containers/[a-zA-Z0-9_.-]+/(start|stop|restart))",
        "-allowbindmountfrom=/tmp",
        "-watchdoginterval=0")
    .WithEndpoint(targetPort: 2375, scheme: "tcp", name: "docker");

// --- Web (control plane) --------------------------------------------------------------------------

// The "https" launch profile so the Agent (which requires an https/wss control-plane URI) has one to
// reach. The dev secrets Web fails closed without are injected here; OTEL_EXPORTER_OTLP_ENDPOINT is
// injected by Aspire automatically, so our OTel (ADR 0024) lights up in the dashboard with no code change
// (D-SVCDEFAULTS: no stock ServiceDefaults).
IResourceBuilder<ProjectResource> web = builder
    .AddProject<Projects.ZWarden_Web>("web", launchProfileName: "https")
    .WithReference(database)
    .WaitFor(database)
    // Drive the Postgres side of the dual-provider path (ADR 0005): ZWarden__Database__Provider maps to
    // the ZWarden:Database:Provider config key, so startup runs the Postgres migrations.
    .WithEnvironment("ZWarden__Database__Provider", "postgres")
    // Dev-only secret key ring (see above) — throwaway, never a production path.
    .WithEnvironment("ZW_SECRET_KEYS", $"k1:{devKeyRing}")
    .WithEnvironment("ZW_SECRET_ACTIVE_KEY_ID", "k1")
    // A confirmed Tenant-Owner admin so authenticated pages (/servers, /audit, …) are reachable in the
    // dev loop (AdminBootstrapper). Dev-only throwaway credentials, mirroring the run-web skill.
    .WithEnvironment("ZWarden__Admin__Email", "admin@zwarden.test")
    .WithEnvironment("ZWarden__Admin__Password", "Sup3r-Str0ng-P@ss!")
    // D-ENROLL: seed the matching redeemable dev enrollment (Development-only; DevEnrollmentBootstrapper).
    .WithEnvironment("ZWarden__Dev__EnrollmentSecret", devEnrollmentSecret);

// --- Agent (host worker) --------------------------------------------------------------------------

// Config section is "Agent" (env Agent__*). ControlPlaneUri is Web's https endpoint (the Agent appends
// /agent/hub and /agent/enroll itself); it presents the well-known dev enrollment secret once and then
// connects with the per-Agent credential it receives. Its Docker path is pointed at wollomatic, never the
// raw socket. OTLP endpoint is injected automatically, so Agent traces/logs correlate in the dashboard.
builder
    .AddProject<Projects.ZWarden_Agent>("agent")
    .WaitFor(web)
    .WaitFor(wollomatic)
    .WithEnvironment("Agent__ControlPlaneUri", web.GetEndpoint("https"))
    .WithEnvironment("Agent__EnrollmentSecret", devEnrollmentSecret)
    .WithEnvironment("Agent__DockerEndpoint", wollomatic.GetEndpoint("docker"));

builder.Build().Run();
