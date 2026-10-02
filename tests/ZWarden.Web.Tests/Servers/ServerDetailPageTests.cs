using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ZWarden.Application.Configuration;
using ZWarden.Application.Mods;
using ZWarden.Application.Servers;
using ZWarden.Domain.Authorization;
using ZWarden.Domain.Backups;
using ZWarden.Domain.Configuration;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Operations;
using ZWarden.Domain.Servers;
using ZWarden.Infrastructure.Authorization;
using ZWarden.Infrastructure.Identity;
using ZWarden.Infrastructure.Persistence;
using ZWarden.PzConfig.Revisions;
using ZWarden.Web.Tests.Account;
using ZWarden.Infrastructure.Tenancy;

namespace ZWarden.Web.Tests.Servers;

/// <summary>
/// F19: the player-management surface on the <c>/servers/{id}</c> detail page. The Players card shows for an
/// operator holding the per-server Player.* permissions (fail-closed, gated in the page and re-checked in the
/// service), embeds the live roster island, and its static-SSR forms bind and enqueue non-mutating player
/// Operations end to end (no circuit). Exercised over the real host.
/// </summary>
public sealed class ServerDetailPageTests
{
    private const string StrongPassword = "correct horse battery staple";

    [Test]
    public async Task The_players_card_shows_the_actions_and_roster_for_a_permitted_operator()
    {
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOperatorAsync(factory);
        ServerId serverId = await SeedServerAsync(factory, "player-managed");

        string html = await (await client.GetAsync(new Uri($"/servers/{serverId}?section=players", UriKind.Relative))).Content.ReadAsStringAsync();

        await Assert.That(html).Contains("data-players-card");
        await Assert.That(html).Contains("data-action=\"refresh\"");
        await Assert.That(html).Contains("data-action=\"kick\"");
        await Assert.That(html).Contains("data-action=\"ban\"");
        await Assert.That(html).Contains("data-action=\"unban\"");
        await Assert.That(html).Contains("data-action=\"remove-from-whitelist\"");
        // The live roster island prerendered with its awaiting state (no roster cached yet).
        await Assert.That(html).Contains("data-roster-awaiting");
        // The username and reason inputs (circuit-bound since #299; the actions are PlayersSectionTests).
        await Assert.That(html).Contains("id=\"player-username\"");
        await Assert.That(html).Contains("id=\"player-reason\"");
        await Assert.That(html).Contains("data-bans-empty");
        client.Dispose();
    }

    [Test]
    public async Task The_diagnostics_card_shows_the_export_button_for_a_permitted_operator()
    {
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOperatorAsync(factory);
        ServerId serverId = await SeedServerAsync(factory, "exportable");

        string html = await (await client.GetAsync(new Uri($"/servers/{serverId}?section=diagnostics", UriKind.Relative))).Content.ReadAsStringAsync();

        await Assert.That(html).Contains("data-diagnostics-card");
        await Assert.That(html).Contains("data-action=\"export-support-package\"");
        // A native POST to the export endpoint so the browser downloads the returned ZIP.
        await Assert.That(html).Contains($"/api/servers/{serverId}/diagnostics/support-package");
        client.Dispose();
    }

    [Test]
    public async Task The_header_shows_an_in_flight_stop_and_disables_the_lifecycle_buttons()
    {
        // #249: the header is rendered from the observed state AND the in-flight lifecycle Operation, so the page
        // after a Stop already says STOPPING (the container reports Running for the whole stop grace). Since #299 the
        // circuit keeps it current (HeaderTests), not live-status.js.
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOperatorAsync(factory);
        ServerId serverId = await SeedServerAsync(factory, "stopping");
        await client.PostAsync(new Uri($"/api/servers/{serverId}/stop", UriKind.Relative), content: null);

        string html = await (await client.GetAsync(new Uri($"/servers/{serverId}", UriKind.Relative))).Content.ReadAsStringAsync();

        await Assert.That(html).DoesNotContain("data-live-status=");
        await Assert.That(html).Contains("data-status-busy=\"true\"");
        await Assert.That(html).Contains("STOPPING");
        await Assert.That(System.Text.RegularExpressions.Regex.IsMatch(
            html, "<button[^>]*data-action=\"server-start\"[^>]*>")).IsTrue();
        foreach (string action in new[] { "server-start", "server-stop", "server-restart" })
        {
            System.Text.RegularExpressions.Match button = System.Text.RegularExpressions.Regex.Match(
                html, $"<button[^>]*data-action=\"{action}\"[^>]*>");
            await Assert.That(button.Value).Contains("disabled");
        }

        client.Dispose();
    }

    [Test]
    public async Task The_header_shows_the_in_flight_operations_progress_as_text()
    {
        // #254: the restart countdown's status line sits beside RESTARTING. It is Agent-supplied, so it renders as
        // encoded text, never markup.
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOperatorAsync(factory);
        ServerId serverId = await SeedServerAsync(factory, "countdown");
        await client.PostAsync(new Uri($"/api/servers/{serverId}/restart", UriKind.Relative), content: null);
        await ServerLifecycleEndpointsTests.ReportProgressAsync(factory, serverId, "Restarting in 240 seconds <b>now</b>");

        string html = await (await client.GetAsync(new Uri($"/servers/{serverId}", UriKind.Relative))).Content.ReadAsStringAsync();

        await Assert.That(html).Contains("data-status-detail");
        await Assert.That(html).Contains("Restarting in 240 seconds &lt;b&gt;now&lt;/b&gt;");
        await Assert.That(html).DoesNotContain("<b>now</b>");
        client.Dispose();
    }

    [Test]
    public async Task The_mods_card_shows_for_a_permitted_operator()
    {
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOperatorAsync(factory);
        ServerId serverId = await SeedServerAsync(factory, "mod-managed");

        string html = await (await client.GetAsync(new Uri($"/servers/{serverId}?section=mods", UriKind.Relative))).Content.ReadAsStringAsync();

        await Assert.That(html).Contains("data-mods-card");
        await Assert.That(html).Contains("data-action=\"mod-refresh\"");
        // The live inventory island prerendered with its awaiting state (no inventory cached yet).
        await Assert.That(html).Contains("data-mods-awaiting");
        client.Dispose();
    }

    [Test]
    public async Task The_mod_management_section_shows_awaiting_without_a_cached_inventory()
    {
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOperatorAsync(factory);
        ServerId serverId = await SeedServerAsync(factory, "manageable");

        string html = await (await client.GetAsync(new Uri($"/servers/{serverId}?section=mods", UriKind.Relative))).Content.ReadAsStringAsync();

        await Assert.That(html).Contains("data-mod-manage");
        // No inventory observed yet, so the actionable controls are withheld until discovery runs.
        await Assert.That(html).Contains("data-mod-manage-awaiting");
        client.Dispose();
    }

    [Test]
    public async Task The_mod_management_controls_render_from_a_cached_inventory()
    {
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOperatorAsync(factory);
        (ServerId serverId, AgentId agent) = await SeedServerAndAgentAsync(factory, "with-inventory");
        SeedInventory(
            factory, serverId, agent,
            installed:
            [
                new InstalledWorkshopItem("100", [new InstalledMod("ModA", null)]),
                new InstalledWorkshopItem("200", [new InstalledMod("ModB", null)]),
            ],
            workshop: ["100", "200"],
            enabled: ["ModA"]);

        string html = await (await client.GetAsync(new Uri($"/servers/{serverId}?section=mods", UriKind.Relative))).Content.ReadAsStringAsync();

        await Assert.That(html).DoesNotContain("data-mod-manage-awaiting");
        await Assert.That(html).Contains("data-action=\"mod-add\"");
        await Assert.That(html).Contains("id=\"mod-workshop-id\"");
        await Assert.That(html).Contains("data-mod-enabled-row");
        await Assert.That(html).Contains("data-action=\"mod-disable\"");
        // ModB is installed but not enabled, so it is offered as an enable candidate.
        await Assert.That(html).Contains("data-mod-enable");
        await Assert.That(html).Contains("data-mod-workshop-row");
        await Assert.That(html).Contains("data-action=\"mod-remove\"");
        // #273: one restart button applies mod changes and pulls Workshop updates; there is no separate "update".
        await Assert.That(html).DoesNotContain("data-action=\"mod-update\"");
        await Assert.That(html).Contains("data-action=\"mod-restart\"");
        await Assert.That(html).Contains("Restart to apply &amp; update mods");
        await Assert.That(html).Contains("checksum");
        client.Dispose();
    }


    [Test]
    public async Task The_graceful_restart_control_shows_for_a_permitted_operator()
    {
        // #114/#213: the always-visible restart panel lets the operator pick a countdown and warn players.
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOperatorAsync(factory);
        ServerId serverId = await SeedServerAsync(factory, "gracefully-restartable");

        string html = await (await client.GetAsync(new Uri($"/servers/{serverId}", UriKind.Relative))).Content.ReadAsStringAsync();

        await Assert.That(html).Contains("data-graceful-restart");
        await Assert.That(html).Contains("data-action=\"graceful-restart\"");
        await Assert.That(html).Contains("id=\"graceful-message\"");
        // #213: the adjustable countdown preset selector is present.
        await Assert.That(html).Contains("id=\"graceful-countdown\"");
        client.Dispose();
    }

    [Test]
    public async Task The_logs_card_shows_and_prerenders_the_live_island_for_a_permitted_operator()
    {
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOperatorAsync(factory);
        ServerId serverId = await SeedServerAsync(factory, "log-viewable");

        string html = await (await client.GetAsync(new Uri($"/servers/{serverId}?section=logs", UriKind.Relative))).Content.ReadAsStringAsync();

        await Assert.That(html).Contains("data-logs-card");
        await Assert.That(html).Contains("data-live-logs");
        // The interactive island prerenders its waiting state (no lines buffered yet).
        await Assert.That(html).Contains("data-log-empty");
        client.Dispose();
    }

    [Test]
    public async Task The_console_card_shows_and_prerenders_the_output_island_for_a_permitted_operator()
    {
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOperatorAsync(factory);
        ServerId serverId = await SeedServerAsync(factory, "console-runnable");

        string html = await (await client.GetAsync(new Uri($"/servers/{serverId}?section=console", UriKind.Relative))).Content.ReadAsStringAsync();

        await Assert.That(html).Contains("data-console-card");
        await Assert.That(html).Contains("data-action=\"run-console\"");
        // The command input binds by its full model-path name under static SSR (BbInput auto-derives, #121).
        await Assert.That(html).Contains("name=\"_consoleForm.Input\"");
        // The interactive output island prerenders its empty state (no output cached yet).
        await Assert.That(html).Contains("data-console-empty");
        client.Dispose();
    }

    [Test]
    public async Task The_backups_card_shows_for_a_permitted_operator()
    {
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOperatorAsync(factory);
        ServerId serverId = await SeedServerAsync(factory, "backup-viewable");

        string html = await (await client.GetAsync(new Uri($"/servers/{serverId}?section=backups", UriKind.Relative))).Content.ReadAsStringAsync();

        await Assert.That(html).Contains("data-backups-card");
        await Assert.That(html).Contains("data-action=\"backup-create\"");
        await Assert.That(html).Contains("data-backups-empty");
        client.Dispose();
    }

    private static async Task<BackupId> SeedBackupAsync(
        ZWardenWebAppFactory factory, ServerId server, AgentId agent, string archiveName)
    {
        using AsyncServiceScope scope = factory.Services.CreateSystemScope();
        ZWardenDbContext db = scope.ServiceProvider.GetRequiredService<ZWardenDbContext>();
        Backup backup = Backup.Record(server, agent, archiveName, 2048, "abc123", BackupReason.Manual, DateTimeOffset.UtcNow);
        db.Set<Backup>().Add(backup);
        await db.SaveChangesAsync();
        return backup.Id;
    }

    private static bool EnqueuedKind(ZWardenWebAppFactory factory, ServerId serverId, OperationKind kind)
    {
        using AsyncServiceScope scope = factory.Services.CreateSystemScope();
        ZWardenDbContext db = scope.ServiceProvider.GetRequiredService<ZWardenDbContext>();
        return db.Set<Operation>().Any(o => o.ServerId == serverId && o.Kind == kind);
    }

    private static string? EnqueuedPayload(ZWardenWebAppFactory factory, ServerId serverId, OperationKind kind)
    {
        using AsyncServiceScope scope = factory.Services.CreateSystemScope();
        ZWardenDbContext db = scope.ServiceProvider.GetRequiredService<ZWardenDbContext>();
        return db.Set<Operation>()
            .Where(o => o.ServerId == serverId && o.Kind == kind)
            .Select(o => o.CommandPayload)
            .FirstOrDefault();
    }

    private static Operation? FirstOperation(ZWardenWebAppFactory factory, ServerId serverId, OperationKind kind)
    {
        using AsyncServiceScope scope = factory.Services.CreateSystemScope();
        ZWardenDbContext db = scope.ServiceProvider.GetRequiredService<ZWardenDbContext>();
        return db.Set<Operation>().FirstOrDefault(o => o.ServerId == serverId && o.Kind == kind);
    }

    private static void SeedInventory(
        ZWardenWebAppFactory factory,
        ServerId server,
        AgentId agent,
        IReadOnlyList<InstalledWorkshopItem> installed,
        IReadOnlyList<string> workshop,
        IReadOnlyList<string> enabled)
    {
        IModInventoryCache cache = factory.Services.GetRequiredService<IModInventoryCache>();
        cache.Record(new ModInventory(server, agent, installed, workshop, enabled, [], DateTimeOffset.UtcNow));
    }

    private static async Task<(ServerId Server, AgentId Agent)> SeedServerAndAgentAsync(ZWardenWebAppFactory factory, string name)
    {
        AgentId agent = AgentId.New();
        using AsyncServiceScope scope = factory.Services.CreateSystemScope();
        ZWardenDbContext db = scope.ServiceProvider.GetRequiredService<ZWardenDbContext>();
        Server server = Server.Import(agent, ServerId.New(), name, DateTimeOffset.UtcNow);
        db.Set<Server>().Add(server);
        await db.SaveChangesAsync();
        return (server.Id, agent);
    }

    [Test]
    public async Task The_last_failed_action_and_its_reason_show_under_the_header()
    {
        // #266: a refused Recreate is not a silent no-op — the page says what failed and why.
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOperatorAsync(factory);
        ServerId serverId = await SeedServerAsync(factory, "refused", gamePort: 17000, queryPort: 17001);
        using (AsyncServiceScope scope = factory.Services.CreateSystemScope())
        {
            ZWardenDbContext db = scope.ServiceProvider.GetRequiredService<ZWardenDbContext>();
            Operation op = Operation.Enqueue(AgentId.New(), OperationKind.RecreateServer, isMutating: true, "k", DateTimeOffset.UtcNow, serverId);
            op.MarkDispatched(DateTimeOffset.UtcNow.AddMinutes(5), DateTimeOffset.UtcNow);
            op.Fail("Host port 16261/udp is already published by another container on this host.", DateTimeOffset.UtcNow);
            db.Add(op);
            await db.SaveChangesAsync();
        }

        string html = await (await client.GetAsync(new Uri($"/servers/{serverId}", UriKind.Relative))).Content.ReadAsStringAsync();

        await Assert.That(html).Contains("data-last-failure-reason>Host port 16261/udp is already published");
        await Assert.That(html).Contains("data-last-failure-action>Recreate</span>");
        await Assert.That(html).Contains("data-last-failure-dismiss");
        client.Dispose();
    }

    [Test]
    public async Task No_failure_alert_is_visible_when_nothing_has_failed()
    {
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOperatorAsync(factory);
        ServerId serverId = await SeedServerAsync(factory, "fine");

        string html = await (await client.GetAsync(new Uri($"/servers/{serverId}", UriKind.Relative))).Content.ReadAsStringAsync();

        // Not rendered at all: the circuit renders it once a failure appears (#299; HeaderTests).
        await Assert.That(html).DoesNotContain("data-last-failure");
        client.Dispose();
    }

    [Test]
    public async Task The_overview_shows_the_branch_and_that_updates_stay_on_it()
    {
        // #258: the branch is fixed at create, so the overview says which one and what an update does.
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOperatorAsync(factory);
        Server server = Server.Register(AgentId.New(), "pinned", DateTimeOffset.UtcNow, branch: "42.19");
        using (AsyncServiceScope scope = factory.Services.CreateSystemScope())
        {
            ZWardenDbContext db = scope.ServiceProvider.GetRequiredService<ZWardenDbContext>();
            db.Set<Server>().Add(server);
            await db.SaveChangesAsync();
        }

        string html = await (await client.GetAsync(new Uri($"/servers/{server.Id}", UriKind.Relative))).Content.ReadAsStringAsync();

        await Assert.That(Regex.IsMatch(html, "data-server-branch[^>]*>\\s*42.19")).IsTrue();
        await Assert.That(html).Contains("Updates stay on 42.19");
        await Assert.That(html).Contains("data-server-version");
        client.Dispose();
    }

    [Test]
    public async Task The_overview_shows_what_the_last_game_update_did()
    {
        // #273: the newest successful update's result line ("build A → B"), under Version. Agent-derived text, so it
        // renders encoded.
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOperatorAsync(factory);
        ServerId serverId = await SeedServerAsync(factory, "updated");
        using (AsyncServiceScope scope = factory.Services.CreateSystemScope())
        {
            ZWardenDbContext db = scope.ServiceProvider.GetRequiredService<ZWardenDbContext>();
            DateTimeOffset now = DateTimeOffset.UtcNow;
            Operation op = Operation.Enqueue(AgentId.New(), OperationKind.UpdateServer, isMutating: true, "upd", now, serverId);
            op.MarkDispatched(now.AddMinutes(5), now);
            op.ReportProgress(100, "Updated from Steam build 24909836 to 25485538.", now.AddMinutes(5), now);
            op.Succeed(now);
            db.Set<Operation>().Add(op);
            await db.SaveChangesAsync();
        }

        string html = await (await client.GetAsync(new Uri($"/servers/{serverId}", UriKind.Relative))).Content.ReadAsStringAsync();

        await Assert.That(html).Contains("data-last-update");
        await Assert.That(html).Contains("Updated from Steam build 24909836 to 25485538.");
        client.Dispose();
    }

    [Test]
    public async Task The_overview_has_no_last_update_line_before_any_update()
    {
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOperatorAsync(factory);
        ServerId serverId = await SeedServerAsync(factory, "never-updated");

        string html = await (await client.GetAsync(new Uri($"/servers/{serverId}", UriKind.Relative))).Content.ReadAsStringAsync();

        await Assert.That(html).DoesNotContain("data-last-update");
        client.Dispose();
    }

    [Test]
    public async Task The_change_ports_control_shows_the_current_pair_for_a_permitted_owner()
    {
        // #229: an owner holds Server.Recreate, so the overview offers the data-preserving recreate on a new pair.
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOperatorAsync(factory);
        ServerId serverId = await SeedServerAsync(factory, "movable", gamePort: 16261, queryPort: 16262);

        string html = await (await client.GetAsync(new Uri($"/servers/{serverId}", UriKind.Relative))).Content.ReadAsStringAsync();

        await Assert.That(html).Contains("data-recreate");
        await Assert.That(html).Contains("id=\"recreate-port\"");
        await Assert.That(html).Contains("id=\"recreate-heap\"");
        await Assert.That(html).Contains("id=\"recreate-countdown\"");
        await Assert.That(html).Contains("data-action=\"recreate\"");
        client.Dispose();
    }

    // --- #271: delete (the dialog and its actions are OverviewSectionTests) -------------------------------------

    [Test]
    public async Task An_owner_sees_the_delete_control()
    {
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOperatorAsync(factory);
        ServerId serverId = await SeedServerAsync(factory, "doomed", gamePort: 16261, queryPort: 16262);

        string html = await (await client.GetAsync(new Uri($"/servers/{serverId}", UriKind.Relative))).Content.ReadAsStringAsync();

        await Assert.That(html).Contains("data-delete-server");
        await Assert.That(html).Contains("data-action=\"delete-open\"");
        // The confirmation is a circuit dialog now; nothing of the old native <dialog> remains.
        await Assert.That(html).DoesNotContain("data-zw-dialog");
        client.Dispose();
    }

    [Test]
    public async Task An_operator_role_without_server_delete_does_not_see_the_delete_control()
    {
        await using ZWardenWebAppFactory factory = new();
        await factory.CreateConfirmedUserAsync("owner@zwarden.test", StrongPassword);
        await AuthorizationBootstrapper.EnsureSeededAsync(factory.Services, "owner@zwarden.test");
        await factory.CreateConfirmedUserAsync("operator@zwarden.test", StrongPassword);
        using (AsyncServiceScope scope = factory.Services.CreateSystemScope())
        {
            ZWardenDbContext db = scope.ServiceProvider.GetRequiredService<ZWardenDbContext>();
            ApplicationUser user = (await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>()
                .FindByEmailAsync("operator@zwarden.test"))!;
            Role operatorRole = await db.Set<Role>().SingleAsync(r => r.BuiltIn == BuiltInRoleKind.Operator);
            db.Set<RoleAssignment>().Add(RoleAssignment.TenantWide(operatorRole.TenantId, UserId.FromGuid(user.Id), operatorRole.Id));
            await db.SaveChangesAsync();
        }

        ServerId serverId = await SeedServerAsync(factory, "kept", gamePort: 16261, queryPort: 16262);
        HttpClient client = factory.CreateWebClient();
        await LoginAsync(client, "operator@zwarden.test", StrongPassword);

        string html = await (await client.GetAsync(new Uri($"/servers/{serverId}", UriKind.Relative))).Content.ReadAsStringAsync();
        // The control is withheld; the lifecycle service refuses a delete without Server.Delete all the same
        // (ServerLifecycleTests).
        await Assert.That(html).DoesNotContain("data-delete-server");
        client.Dispose();
    }

    [Test]
    public async Task An_operator_role_without_server_recreate_does_not_see_the_change_ports_control()
    {
        // D2 (#229): Operator runs the server day to day but does not re-provision it.
        await using ZWardenWebAppFactory factory = new();
        await factory.CreateConfirmedUserAsync("owner@zwarden.test", StrongPassword);
        await AuthorizationBootstrapper.EnsureSeededAsync(factory.Services, "owner@zwarden.test");
        await factory.CreateConfirmedUserAsync("operator@zwarden.test", StrongPassword);
        using (AsyncServiceScope scope = factory.Services.CreateSystemScope())
        {
            ZWardenDbContext db = scope.ServiceProvider.GetRequiredService<ZWardenDbContext>();
            ApplicationUser user = (await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>()
                .FindByEmailAsync("operator@zwarden.test"))!;
            Role operatorRole = await db.Set<Role>().SingleAsync(r => r.BuiltIn == BuiltInRoleKind.Operator);
            db.Set<RoleAssignment>().Add(RoleAssignment.TenantWide(operatorRole.TenantId, UserId.FromGuid(user.Id), operatorRole.Id));
            await db.SaveChangesAsync();
        }

        ServerId serverId = await SeedServerAsync(factory, "operated", gamePort: 16261, queryPort: 16262);
        HttpClient client = factory.CreateWebClient();
        await LoginAsync(client, "operator@zwarden.test", StrongPassword);

        string html = await (await client.GetAsync(new Uri($"/servers/{serverId}", UriKind.Relative))).Content.ReadAsStringAsync();

        await Assert.That(html).Contains("data-graceful-restart"); // the operator does see the restart panel …
        await Assert.That(html).DoesNotContain("data-recreate");   // … but not the recreate control.
        client.Dispose();
    }
    private static async Task<HttpClient> SignedInOperatorAsync(ZWardenWebAppFactory factory)
    {
        await factory.CreateConfirmedUserAsync("op@zwarden.test", StrongPassword);
        await AuthorizationBootstrapper.EnsureSeededAsync(factory.Services, "op@zwarden.test");
        HttpClient client = factory.CreateWebClient();
        await LoginAsync(client, "op@zwarden.test", StrongPassword);
        return client;
    }

    private static async Task<ServerId> SeedServerAsync(
        ZWardenWebAppFactory factory, string name, int? gamePort = null, int? queryPort = null)
    {
        using AsyncServiceScope scope = factory.Services.CreateSystemScope();
        ZWardenDbContext db = scope.ServiceProvider.GetRequiredService<ZWardenDbContext>();
        Server server = Server.Import(AgentId.New(), ServerId.New(), name, DateTimeOffset.UtcNow);
        if (gamePort is int game && queryPort is int query)
        {
            server.RecordContainer($"pz-{name}", game, query);
        }

        db.Set<Server>().Add(server);
        await db.SaveChangesAsync();
        return server.Id;
    }

    private static async Task LoginAsync(HttpClient client, string email, string password)
    {
        HttpResponseMessage page = await client.GetAsync(new Uri("/login", UriKind.Relative));
        string html = await page.Content.ReadAsStringAsync();
        Dictionary<string, string> form = ParseHiddenInputs(html);
        form["Input.Email"] = email;
        form["Input.Password"] = password;
        await client.PostAsync(new Uri("/login", UriKind.Relative), new FormUrlEncodedContent(form));
    }

    private static Dictionary<string, string> ParseHiddenInputs(string html)
    {
        Dictionary<string, string> inputs = new(StringComparer.Ordinal);
        foreach (Match tag in Regex.Matches(html, "<input\\b[^>]*?type=\"hidden\"[^>]*?>"))
        {
            Match name = Regex.Match(tag.Value, "name=\"([^\"]+)\"");
            Match value = Regex.Match(tag.Value, "value=\"([^\"]*)\"");
            if (name.Success)
            {
                inputs[name.Groups[1].Value] = value.Success ? WebUtility.HtmlDecode(value.Groups[1].Value) : string.Empty;
            }
        }

        return inputs;
    }
}
