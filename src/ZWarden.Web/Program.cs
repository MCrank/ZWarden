using BlazorBlueprint.Components;
using ZWarden.Infrastructure.Agents;
using ZWarden.Infrastructure.Audit;
using ZWarden.Infrastructure.Authorization;
using ZWarden.Infrastructure.Identity;
using ZWarden.Infrastructure.Operations;
using ZWarden.Infrastructure.Persistence;
using ZWarden.Infrastructure.Security;
using ZWarden.Infrastructure.Tenancy;
using ZWarden.Web.Agents;
using ZWarden.Web.Operations;
using ZWarden.Web.Components;
using ZWarden.Web.Components.Account;
using ZWarden.Web.Components.Agents;

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

builder.Services.AddSecurityFoundation();          // key ring from the environment (fail-closed, ADR 0015)
builder.Services.AddSessionTenantContext();        // wins over the single-tenant default (S5)
builder.Services.AddTenantFoundation();
builder.Services.AddZWardenPersistence(provider, connectionString);
builder.Services.AddZWardenAuthentication(allowedHosts);
builder.Services.AddZWardenAuthorization();        // F5: decision service, policy provider, handlers
builder.Services.AddZWardenAudit();                 // F6: writer, query, correlation, durable auth sink
builder.Services.AddZWardenEnrollment();            // F9: enrollment issuance/exchange, trust management, verifier
builder.Services.AddZWardenOperations();            // F11: operations engine — coordinator, store, per-server lock (PR-B adds dispatch)
builder.Services.AddAgentControlPlane();            // F10: Agent hub, handshake auth scheme, connection registry + monitor
builder.Services.AddOperationDispatch();            // F11 PR-B: real operation dispatcher over the SignalR connection

WebApplication app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHostFiltering();       // host-header validation at the browser boundary (ADR 0006)
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// The Account sign-out endpoint (must act on the raw HTTP response, not a circuit) - F4 UI #63.
app.MapAccountEndpoints();

// The operator enrollment/trust API, gated by Tenant.Enrollment.Manage (F9).
app.MapEnrollmentEndpoints();

// The SignalR Agent hub (F10) — Agents connect outbound here over WSS, authenticated by the "Agent" scheme.
app.MapAgentHub();

// Apply migrations, seed the default tenant, and (when configured) the first administrator, before
// serving traffic. The security foundation loads its key ring here and fails closed if it is absent.
await app.Services.MigrateAndBootstrapDefaultTenantAsync();

string? adminEmail = builder.Configuration["ZWarden:Admin:Email"];
string? adminPassword = builder.Configuration["ZWarden:Admin:Password"];
if (!string.IsNullOrWhiteSpace(adminEmail) && !string.IsNullOrWhiteSpace(adminPassword))
{
    await AdminBootstrapper.EnsureAdminAsync(app.Services, adminEmail, adminPassword);
}

// F5: seed the tenant's built-in roles and grant the first administrator the Tenant Owner role. Runs
// after the admin seed (so the account exists) and is idempotent; seeds roles even with no admin configured.
await AuthorizationBootstrapper.EnsureSeededAsync(app.Services, adminEmail);

app.Run();

/// <summary>
/// Exposed so the F4 UI endpoint tests can boot the real host through
/// <c>WebApplicationFactory&lt;Program&gt;</c> (issue #63). Top-level statements otherwise emit an
/// internal <c>Program</c> the test project cannot name.
/// </summary>
public partial class Program;
