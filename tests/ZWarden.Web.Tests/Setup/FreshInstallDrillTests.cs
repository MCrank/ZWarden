using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ZWarden.Infrastructure.Persistence;
using ZWarden.Web.Tests.Account;

namespace ZWarden.Web.Tests.Setup;

/// <summary>
/// F40 release gate — the fresh-install drill (PRD §59). A brand-new install (an empty database, setup not yet
/// completed) must come up correctly under its own bootstrap: the Program startup migrates the empty database
/// to head (F33/F34), the container-liveness endpoint answers before setup, and the first-run gate routes the
/// operator to the wizard. This composes the migration, liveness and gate behaviours into one "a clean install
/// boots" assertion. Tier-1 offline (the real host over WebApplicationFactory; a fresh temp SQLite file per run).
/// </summary>
public class FreshInstallDrillTests
{
    [Test]
    public async Task A_fresh_install_migrates_the_empty_database_to_head_on_boot()
    {
        await using ZWardenWebAppFactory factory = new() { CompleteSetupOnStart = false };
        _ = factory.Services; // force the host (and its migrate-and-bootstrap startup) to run

        using IServiceScope scope = factory.Services.CreateScope();
        ZWardenDbContext context = scope.ServiceProvider.GetRequiredService<ZWardenDbContext>();

        // The bootstrap applied migrations (not EnsureCreated): every migration is applied and none is pending.
        await Assert.That((await context.Database.GetAppliedMigrationsAsync()).Any()).IsTrue();
        await Assert.That(await context.Database.GetPendingMigrationsAsync()).IsEmpty();
    }

    [Test]
    public async Task A_fresh_install_serves_healthz_and_gates_the_root_to_the_setup_wizard()
    {
        await using ZWardenWebAppFactory factory = new() { CompleteSetupOnStart = false };
        HttpClient client = factory.CreateWebClient();

        // Container liveness is ungated and answers even before setup (dependents wait on this, F34 D-5).
        HttpResponseMessage health = await client.GetAsync("/healthz");
        await Assert.That(health.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(await health.Content.ReadAsStringAsync()).IsEqualTo("healthy");

        // Until setup is complete, every ordinary page routes to the wizard (F33 first-run gate, ADR 0036).
        HttpResponseMessage root = await client.GetAsync("/");
        await Assert.That(root.StatusCode).IsEqualTo(HttpStatusCode.Redirect);
        await Assert.That(root.Headers.Location!.ToString()).Contains("/setup");
    }
}
