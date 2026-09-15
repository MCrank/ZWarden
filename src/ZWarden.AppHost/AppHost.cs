// ZWarden dev/test orchestration AppHost (#123, ADR 0031). DEV/TEST ONLY — see README.md.
//
// PR-1 (spike) models the smallest graph that proves the wiring: Postgres + ZWarden.Web, with the
// connection string, provider switch, and the dev-only secrets Web fails closed without all injected
// by Aspire so the dev inner loop needs no hand-set config. PR-2 adds the Agent and the wollomatic
// socket-proxy; PR-3 adds the integration-test boot.
using System.Security.Cryptography;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

IDistributedApplicationBuilder builder = DistributedApplication.CreateBuilder(args);

// A STABLE, well-known dev postgres password. A persisted data volume (below) bakes in the password
// from its first init and ignores POSTGRES_PASSWORD afterwards, so Aspire's default per-run generated
// password would desync from the volume and fail auth ("password authentication failed for user
// postgres"). Pinning a fixed dev value keeps the volume authenticating across runs, machines, and
// user-secrets resets. Dev-only throwaway, never a production credential (same category as the dev
// admin below); production Postgres is provisioned outside Aspire (ADR 0031 — F34 owns deployment).
IResourceBuilder<ParameterResource> postgresPassword =
    builder.AddParameter("postgres-password", value: "zwarden-dev-only", secret: true);

// D-CONN (ADR 0031): the database resource is named "ZWarden" so Aspire injects the environment
// variable ConnectionStrings__ZWarden — exactly the key ZWarden.Web reads via
// GetConnectionString("ZWarden") (Program.cs). No connection string is hand-set in dev config.
// WithDataVolume persists the dev database (admin, servers, config, one-time migrations) across runs;
// the pinned password above is what makes that persistence reliable.
//
// Caveat for PR-2: the dev key ring below is still generated PER RUN, which is fine here because PR-1
// stores nothing encrypted-at-rest with it (Identity password hashes use their own hasher). Once PR-2
// (D-ENROLL) persists key-ring-encrypted secrets, the key ring must become stable too, or a persisted
// DB carries columns the next run cannot decrypt. PR-2 pins both as a coherent dev-secret set.
IResourceBuilder<PostgresDatabaseResource> database = builder
    .AddPostgres("postgres", password: postgresPassword)
    .WithDataVolume()
    .AddDatabase("ZWarden");

// A fresh dev-only secret key ring, generated per run — NOT a committed secret. ZWarden.Web fails
// closed without ZW_SECRET_KEYS + ZW_SECRET_ACTIVE_KEY_ID (ADR 0015 / KeyRingLoader), so the inner
// loop must supply throwaway values, exactly as the run-web skill does. A per-run key is fine for the
// spike (nothing encrypted at rest is carried across runs yet); PR-2's dev bootstrap (D-ENROLL) is
// where stable, Aspire-parameter-backed dev secrets land.
string devKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

builder.AddProject<Projects.ZWarden_Web>("web")
    .WithReference(database)
    .WaitFor(database)
    // Drive the Postgres side of the dual-provider path (ADR 0005): ZWarden__Database__Provider maps
    // to the ZWarden:Database:Provider config key, so startup runs the Postgres migrations.
    .WithEnvironment("ZWarden__Database__Provider", "postgres")
    // Dev-only secret key ring (see above) — throwaway, never a production path.
    .WithEnvironment("ZW_SECRET_KEYS", $"k1:{devKey}")
    .WithEnvironment("ZW_SECRET_ACTIVE_KEY_ID", "k1")
    // A confirmed Tenant-Owner admin so authenticated pages (/servers, /audit, …) are reachable in
    // the dev loop (AdminBootstrapper). Dev-only throwaway credentials, mirroring the run-web skill.
    .WithEnvironment("ZWarden__Admin__Email", "admin@zwarden.test")
    .WithEnvironment("ZWarden__Admin__Password", "Sup3r-Str0ng-P@ss!");

builder.Build().Run();
