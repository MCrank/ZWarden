using BlazorBlueprint.Components;
using ZWarden.Infrastructure.Agents;
using ZWarden.Infrastructure.Audit;
using ZWarden.Infrastructure.Backups;
using ZWarden.Infrastructure.Authorization;
using ZWarden.Infrastructure.Configuration;
using ZWarden.Infrastructure.Console;
using ZWarden.Infrastructure.Identity;
using ZWarden.Infrastructure.Mods;
using ZWarden.Infrastructure.Operations;
using ZWarden.Infrastructure.Persistence;
using ZWarden.Infrastructure.Workshop;
using ZWarden.Infrastructure.Players;
using ZWarden.Infrastructure.Security;
using ZWarden.Infrastructure.Servers;
using ZWarden.Infrastructure.Setup;
using ZWarden.Infrastructure.Tenancy;
using ZWarden.Web.Agents;
using ZWarden.Web.Observability;
using ZWarden.Web.Operations;
using ZWarden.Web.Components;
using ZWarden.Web.Components.Account;
using ZWarden.Web.Components.Agents;
using ZWarden.Web.Components.Audit;
using ZWarden.Web.Components.Console;
using ZWarden.Web.Components.Diagnostics;
using ZWarden.Web.Components.Operations;
using ZWarden.Web.Components.Players;
using ZWarden.Web.Components.Servers;
using ZWarden.Web.Diagnostics;
using ZWarden.Web.Hosting;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Cascades the authentication state to components (drives AuthorizeRouteView / [Authorize] on the
// static-rendered Account pages, F4 UI #63).
builder.Services.AddCascadingAuthenticationState();

// The local-URL-guarded redirect helper the static Account pages use to sign in and bounce (#63).
builder.Services.AddScoped<IdentityRedirectManager>();

// Blazor Blueprint services (ADR 0003): ToastService, DialogService, and the
// primitive services underneath them.
builder.Services.AddBlazorBlueprintComponents();

// F4 is the first persisting feature, so it wires the F2/F3/F3A foundations into the host, in order:
// security (the key ring the DbContext protects token secrets with), then the session-derived tenant
// context BEFORE AddTenantFoundation so it wins the TryAdd, then persistence and Identity.
ZWardenDbProvider provider = ZWardenDbProviderExtensions.ParseProvider(
    builder.Configuration["ZWarden:Database:Provider"] ?? "sqlite");
string connectionString = builder.Configuration.GetConnectionString("ZWarden") ?? "Data Source=zwarden.db";
string[] allowedHosts = builder.Configuration.GetSection("ZWarden:AllowedHosts").Get<string[]>() ?? ["localhost"];

// #160: the instance's display name — deploy-time config (ZWarden:Instance:Name), read by the shell chrome
// and the Settings page. Bound with its default when the section is absent, so a stock install shows "ZWarden".
builder.Services.Configure<ZWarden.Web.Configuration.InstanceOptions>(
    builder.Configuration.GetSection(ZWarden.Web.Configuration.InstanceOptions.SectionName));

builder.Services.AddSecurityFoundation();          // key ring from the environment (fail-closed, ADR 0015)
builder.Services.AddSessionTenantContext();        // wins over the single-tenant default (S5)
builder.Services.AddTenantFoundation();
builder.Services.AddZWardenPersistence(provider, connectionString);
builder.Services.AddZWardenAuthentication(allowedHosts);
builder.Services.AddZWardenAuthorization();        // F5: decision service, policy provider, handlers
builder.Services.AddZWardenSetup();                 // F33: first-run setup state (InstallState singleton) + gate signal
builder.Services.AddZWardenAudit();                 // F6: writer, query, correlation, durable auth sink
builder.Services.AddZWardenEnrollment();            // F9: enrollment issuance/exchange, trust management, verifier
builder.Services.AddZWardenOperations();            // F11: operations engine — coordinator, store, per-server lock (PR-B adds dispatch)
builder.Services.AddZWardenServers();               // F14: Server inventory + import, snapshot reconciler, discovery cache
builder.Services.AddZWardenPlayers();               // F19: player management (kick/ban/unban/whitelist) — non-mutating RCON operations
builder.Services.AddZWardenConsole();               // F28: remote administrative console — non-mutating arbitrary-RCON operations + output cache
builder.Services.AddZWardenConfiguration();         // F20b: configuration revisions — repository + completion-time revision recorder
builder.Services.AddZWardenMods();                  // F21: Workshop/mod discovery — inventory cache + discovery service (read-only)
builder.Services.AddZWardenWorkshop();              // #110: keyless Steam Workshop metadata client (control-plane egress, no key)
builder.Services.AddZWardenBackups();               // F24: backups — take/delete Operations, pre-op API seam, completion ingest, read surface
builder.Services.AddZWardenDiagnostics(builder.Configuration); // F29: read-only diagnostics engine — in-process DB/TLS/Web/Agent domains (Agent-side domains land in PR-B/PR-C)
builder.Services.AddAgentControlPlane();            // F10: Agent hub, handshake auth scheme, connection registry + monitor
builder.Services.AddOperationDispatch();            // F11 PR-B: real operation dispatcher over the SignalR connection
builder.Services.AddZWardenTelemetry(builder.Configuration, builder.Environment); // F16: OpenTelemetry baseline (opt-in OTLP)
builder.Services.AddProxyForwardedHeaders();       // F32: trust the reference Caddy ingress' X-Forwarded-* (ADR 0035)

WebApplication app = builder.Build();

// F32: apply the forwarded scheme/host/client-IP FIRST, so the rest of the pipeline (host filtering, HTTPS
// redirection, auth cookies) sees the request as the client made it to the TLS-terminating ingress (ADR 0035).
app.UseForwardedHeaders();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHostFiltering();       // host-header validation at the browser boundary (ADR 0006)
app.UseHttpsRedirection();

// F33: until first-run setup is complete, redirect browser navigations to the /setup wizard. Runs BEFORE
// authentication/authorization so an un-set-up install routes even [Authorize] pages to /setup rather than
// /login; framework assets, the SignalR circuit, the Agent hub, and the wizard's own pages pass through so
// setup can proceed. Once complete the gate short-circuits on a process-wide signal at zero cost (ADR 0036).
app.UseSetupGate();

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

// F34: an unauthenticated, dependency-free container liveness probe for the reference Compose healthcheck
// (ADR 0037). Deliberately distinct from F16's authenticated PZ-server health model — this only answers
// "is the Web process serving?", is allow-listed past the first-run gate, and never touches the database.
app.MapGet("/healthz", () => Results.Text("healthy")).AllowAnonymous();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// The Account sign-out endpoint (must act on the raw HTTP response, not a circuit) - F4 UI #63.
app.MapAccountEndpoints();

// The operator enrollment/trust API, gated by Tenant.Enrollment.Manage (F9).
app.MapEnrollmentEndpoints();

// The operator operations API (enqueue a diagnostic ping, read operation state), gated by Agent.Manage (F11).
app.MapOperationEndpoints();

// The operator Server inventory + import API (F14): list is authenticated + self-filtering, import/discovery
// gated by Server.Register.
app.MapServerEndpoints();
app.MapPlayerEndpoints();
app.MapConsoleEndpoints();
app.MapDiagnosticsEndpoints();

// The audit-trail CSV export (#161), gated by the same Audit.View policy as the viewer page.
app.MapAuditEndpoints();

// The SignalR Agent hub (F10) — Agents connect outbound here over WSS, authenticated by the "Agent" scheme.
app.MapAgentHub();

// Apply migrations, seed the default tenant, and (when configured) the first administrator, before
// serving traffic. The security foundation loads its key ring here and fails closed if it is absent.
await app.Services.MigrateAndBootstrapDefaultTenantAsync();

// F33: seed the empty install-state singleton so the first-run gate always has a row to read (ADR 0036).
await SetupBootstrapper.EnsureInstallStateAsync(app.Services);

string? adminEmail = builder.Configuration["ZWarden:Admin:Email"];
string? adminPassword = builder.Configuration["ZWarden:Admin:Password"];
if (!string.IsNullOrWhiteSpace(adminEmail) && !string.IsNullOrWhiteSpace(adminPassword))
{
    await AdminBootstrapper.EnsureAdminAsync(app.Services, adminEmail, adminPassword);

    // A headless deploy that seeds its administrator from configuration is already set up — mark setup
    // complete so it never lands on the first-run wizard (F33 D-1).
    await SetupBootstrapper.MarkSetupCompleteAsync(app.Services);
}

// F5: seed the tenant's built-in roles and grant the first administrator the Tenant Owner role. Runs
// after the admin seed (so the account exists) and is idempotent; seeds roles even with no admin configured.
await AuthorizationBootstrapper.EnsureSeededAsync(app.Services, adminEmail);

// DEV ONLY (ADR 0031, #123): when the Aspire AppHost injects a dev enrollment secret in Development, seed
// a well-known redeemable enrollment so the orchestrated Agent self-enrolls on boot with no manual step.
// Fails closed if that secret is ever configured outside Development — the well-known credential is never
// a production authentication path. No-op in production, where the key is unset.
await DevEnrollmentBootstrapper.EnsureDevEnrollmentAsync(
    app.Services,
    app.Environment.IsDevelopment(),
    builder.Configuration["ZWarden:Dev:EnrollmentSecret"],
    lifetime: TimeSpan.FromHours(24));

app.Run();

/// <summary>
/// Exposed so the F4 UI endpoint tests can boot the real host through
/// <c>WebApplicationFactory&lt;Program&gt;</c> (issue #63). Top-level statements otherwise emit an
/// internal <c>Program</c> the test project cannot name.
/// </summary>
public partial class Program;
