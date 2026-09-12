using BlazorBlueprint.Components;
using ZWarden.Infrastructure.Authorization;
using ZWarden.Infrastructure.Identity;
using ZWarden.Infrastructure.Persistence;
using ZWarden.Infrastructure.Security;
using ZWarden.Infrastructure.Tenancy;
using ZWarden.Web.Components;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

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
