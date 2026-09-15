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

// D-CONN (ADR 0031): the database resource is named "ZWarden" so Aspire injects the environment
// variable ConnectionStrings__ZWarden — exactly the key ZWarden.Web reads via
// GetConnectionString("ZWarden") (Program.cs). No connection string is hand-set in dev config.
// WithDataVolume keeps the dev database (and its one-time migrations) across `aspire run` sessions.
IResourceBuilder<PostgresDatabaseResource> database = builder
    .AddPostgres("postgres")
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
