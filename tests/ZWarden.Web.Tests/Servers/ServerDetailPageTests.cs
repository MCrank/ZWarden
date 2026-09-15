using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using ZWarden.Application.Mods;
using ZWarden.Domain.Backups;
using ZWarden.Domain.Configuration;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Operations;
using ZWarden.Domain.Servers;
using ZWarden.Infrastructure.Authorization;
using ZWarden.Infrastructure.Persistence;
using ZWarden.PzConfig.Revisions;
using ZWarden.Web.Tests.Account;

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

        string html = await (await client.GetAsync(new Uri($"/servers/{serverId}", UriKind.Relative))).Content.ReadAsStringAsync();

        await Assert.That(html).Contains("data-players-card");
        await Assert.That(html).Contains("data-action=\"refresh\"");
        await Assert.That(html).Contains("data-action=\"kick\"");
        await Assert.That(html).Contains("data-action=\"ban\"");
        await Assert.That(html).Contains("data-action=\"unban\"");
        await Assert.That(html).Contains("data-action=\"remove-from-whitelist\"");
        // The live roster island prerendered with its awaiting state (no roster cached yet).
        await Assert.That(html).Contains("data-roster-awaiting");
        // The static-SSR form binds the username/reason by their model-path field names (BbInput auto-derives, #121).
        await Assert.That(html).Contains("name=\"_actionForm.Username\"");
        await Assert.That(html).Contains("name=\"_actionForm.Reason\"");
        await Assert.That(html).Contains("data-bans-empty");
        client.Dispose();
    }

    [Test]
    public async Task The_refresh_form_posts_and_enqueues_a_list_players_operation()
    {
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOperatorAsync(factory);
        ServerId serverId = await SeedServerAsync(factory, "enumerable");

        string page = await (await client.GetAsync(new Uri($"/servers/{serverId}", UriKind.Relative))).Content.ReadAsStringAsync();
        Dictionary<string, string> form = new(StringComparer.Ordinal)
        {
            ["__RequestVerificationToken"] = ParseHiddenInputs(page)["__RequestVerificationToken"],
            ["_handler"] = "player-action",
            ["_actionForm.Target"] = "refresh",
        };
        HttpResponseMessage post = await client.PostAsync(new Uri($"/servers/{serverId}", UriKind.Relative), new FormUrlEncodedContent(form));

        await Assert.That((int)post.StatusCode).IsLessThan(400);
        await Assert.That(EnqueuedKind(factory, serverId, OperationKind.ListPlayers)).IsTrue();
        client.Dispose();
    }

    [Test]
    public async Task The_kick_form_posts_and_enqueues_a_kick_carrying_the_username()
    {
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOperatorAsync(factory);
        ServerId serverId = await SeedServerAsync(factory, "kickable");

        string page = await (await client.GetAsync(new Uri($"/servers/{serverId}", UriKind.Relative))).Content.ReadAsStringAsync();
        Dictionary<string, string> form = new(StringComparer.Ordinal)
        {
            ["__RequestVerificationToken"] = ParseHiddenInputs(page)["__RequestVerificationToken"],
            ["_handler"] = "player-action",
            ["_actionForm.Target"] = "kick",
            ["_actionForm.Username"] = "Bob",
        };
        HttpResponseMessage post = await client.PostAsync(new Uri($"/servers/{serverId}", UriKind.Relative), new FormUrlEncodedContent(form));

        await Assert.That((int)post.StatusCode).IsLessThan(400);
        using IServiceScope scope = factory.Services.CreateScope();
        ZWardenDbContext db = scope.ServiceProvider.GetRequiredService<ZWardenDbContext>();
        Operation? op = db.Set<Operation>().FirstOrDefault(o => o.ServerId == serverId && o.Kind == OperationKind.KickPlayer);
        await Assert.That(op).IsNotNull();
        await Assert.That(op!.IsMutating).IsFalse();
        await Assert.That(op.CommandPayload).Contains("Bob");
        client.Dispose();
    }

    [Test]
    public async Task The_configuration_card_shows_the_edit_form_and_empty_history_for_a_permitted_operator()
    {
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOperatorAsync(factory);
        ServerId serverId = await SeedServerAsync(factory, "configurable");

        string html = await (await client.GetAsync(new Uri($"/servers/{serverId}", UriKind.Relative))).Content.ReadAsStringAsync();

        await Assert.That(html).Contains("data-config-card");
        await Assert.That(html).Contains("data-action=\"config-apply\"");
        // The file select binds by its full model-path name under static SSR.
        await Assert.That(html).Contains("name=\"_configForm.File\"");
        await Assert.That(html).Contains("data-revisions-empty");
        client.Dispose();
    }

    [Test]
    public async Task The_apply_form_posts_and_enqueues_a_config_apply_carrying_the_edit()
    {
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOperatorAsync(factory);
        ServerId serverId = await SeedServerAsync(factory, "editable");

        string page = await (await client.GetAsync(new Uri($"/servers/{serverId}", UriKind.Relative))).Content.ReadAsStringAsync();
        Dictionary<string, string> form = new(StringComparer.Ordinal)
        {
            ["__RequestVerificationToken"] = ParseHiddenInputs(page)["__RequestVerificationToken"],
            ["_handler"] = "server-config",
            ["_configForm.File"] = "SandboxVars",
            ["_configForm.Path"] = "Zombies",
            ["_configForm.Kind"] = "Number",
            ["_configForm.Value"] = "1",
            ["_configForm.Target"] = "apply",
        };
        HttpResponseMessage post = await client.PostAsync(new Uri($"/servers/{serverId}", UriKind.Relative), new FormUrlEncodedContent(form));

        await Assert.That((int)post.StatusCode).IsLessThan(400);
        using IServiceScope scope = factory.Services.CreateScope();
        ZWardenDbContext db = scope.ServiceProvider.GetRequiredService<ZWardenDbContext>();
        Operation? op = db.Set<Operation>().FirstOrDefault(o => o.ServerId == serverId && o.Kind == OperationKind.ConfigApply);
        await Assert.That(op).IsNotNull();
        await Assert.That(op!.IsMutating).IsTrue();
        await Assert.That(op.CommandPayload).Contains("Zombies");
        client.Dispose();
    }

    [Test]
    public async Task The_history_table_lists_a_recorded_revision()
    {
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOperatorAsync(factory);
        ServerId serverId = await SeedServerAsync(factory, "with-history");
        await SeedRevisionAsync(factory, serverId, PzConfigFile.Ini, "[[\"PublicName\",\"s:First\"]]", DateTimeOffset.UtcNow);

        string html = await (await client.GetAsync(new Uri($"/servers/{serverId}", UriKind.Relative))).Content.ReadAsStringAsync();

        await Assert.That(html).Contains("data-revisions-table");
        await Assert.That(html).Contains("data-revision-row");
        await Assert.That(html).DoesNotContain("data-revisions-empty");
        client.Dispose();
    }

    [Test]
    public async Task The_restore_button_posts_and_enqueues_a_config_apply()
    {
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOperatorAsync(factory);
        ServerId serverId = await SeedServerAsync(factory, "restorable");
        DateTimeOffset t0 = DateTimeOffset.UtcNow;
        ConfigurationRevisionId older = await SeedRevisionAsync(factory, serverId, PzConfigFile.Ini, "[[\"PublicName\",\"s:First\"]]", t0);
        await SeedRevisionAsync(factory, serverId, PzConfigFile.Ini, "[[\"PublicName\",\"s:Second\"]]", t0.AddMinutes(5));

        string page = await (await client.GetAsync(new Uri($"/servers/{serverId}", UriKind.Relative))).Content.ReadAsStringAsync();
        Dictionary<string, string> form = new(StringComparer.Ordinal)
        {
            ["__RequestVerificationToken"] = ParseHiddenInputs(page)["__RequestVerificationToken"],
            ["_handler"] = "config-restore",
            ["_restoreForm.Target"] = $"{PzConfigFile.Ini}|{older}",
        };
        HttpResponseMessage post = await client.PostAsync(new Uri($"/servers/{serverId}", UriKind.Relative), new FormUrlEncodedContent(form));

        await Assert.That((int)post.StatusCode).IsLessThan(400);
        using IServiceScope scope = factory.Services.CreateScope();
        ZWardenDbContext db = scope.ServiceProvider.GetRequiredService<ZWardenDbContext>();
        Operation? op = db.Set<Operation>().FirstOrDefault(o => o.ServerId == serverId && o.Kind == OperationKind.ConfigApply);
        await Assert.That(op).IsNotNull();
        await Assert.That(op!.CommandPayload).Contains("PublicName");
        client.Dispose();
    }

    private static async Task<ConfigurationRevisionId> SeedRevisionAsync(
        ZWardenWebAppFactory factory, ServerId server, PzConfigFile file, string canonicalText, DateTimeOffset at)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ZWardenDbContext db = scope.ServiceProvider.GetRequiredService<ZWardenDbContext>();
        PzValueSnapshot snapshot = PzValueSnapshot.Parse(canonicalText);
        ConfigurationRevision revision = ConfigurationRevision.Record(server, file, snapshot.CanonicalText, snapshot.Hash, at);
        db.Set<ConfigurationRevision>().Add(revision);
        await db.SaveChangesAsync();
        return revision.Id;
    }

    [Test]
    public async Task The_mods_card_shows_for_a_permitted_operator()
    {
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOperatorAsync(factory);
        ServerId serverId = await SeedServerAsync(factory, "mod-managed");

        string html = await (await client.GetAsync(new Uri($"/servers/{serverId}", UriKind.Relative))).Content.ReadAsStringAsync();

        await Assert.That(html).Contains("data-mods-card");
        await Assert.That(html).Contains("data-action=\"mod-refresh\"");
        // The live inventory island prerendered with its awaiting state (no inventory cached yet).
        await Assert.That(html).Contains("data-mods-awaiting");
        client.Dispose();
    }

    [Test]
    public async Task The_mod_refresh_form_posts_and_enqueues_a_discovery()
    {
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOperatorAsync(factory);
        ServerId serverId = await SeedServerAsync(factory, "discoverable");

        string page = await (await client.GetAsync(new Uri($"/servers/{serverId}", UriKind.Relative))).Content.ReadAsStringAsync();
        Dictionary<string, string> form = new(StringComparer.Ordinal)
        {
            ["__RequestVerificationToken"] = ParseHiddenInputs(page)["__RequestVerificationToken"],
            ["_handler"] = "mod-discovery",
        };
        HttpResponseMessage post = await client.PostAsync(new Uri($"/servers/{serverId}", UriKind.Relative), new FormUrlEncodedContent(form));

        await Assert.That((int)post.StatusCode).IsLessThan(400);
        await Assert.That(EnqueuedKind(factory, serverId, OperationKind.ModDiscovery)).IsTrue();
        client.Dispose();
    }

    [Test]
    public async Task The_mod_management_section_shows_awaiting_without_a_cached_inventory()
    {
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOperatorAsync(factory);
        ServerId serverId = await SeedServerAsync(factory, "manageable");

        string html = await (await client.GetAsync(new Uri($"/servers/{serverId}", UriKind.Relative))).Content.ReadAsStringAsync();

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

        string html = await (await client.GetAsync(new Uri($"/servers/{serverId}", UriKind.Relative))).Content.ReadAsStringAsync();

        await Assert.That(html).DoesNotContain("data-mod-manage-awaiting");
        await Assert.That(html).Contains("data-action=\"mod-add\"");
        await Assert.That(html).Contains("name=\"_modManageForm.WorkshopId\"");
        await Assert.That(html).Contains("data-mod-enabled-row");
        await Assert.That(html).Contains("data-action=\"mod-disable\"");
        // ModB is installed but not enabled, so it is offered as an enable candidate.
        await Assert.That(html).Contains("data-mod-enable");
        await Assert.That(html).Contains("data-mod-workshop-row");
        await Assert.That(html).Contains("data-action=\"mod-remove\"");
        await Assert.That(html).Contains("data-action=\"mod-update\"");
        await Assert.That(html).Contains("data-action=\"mod-restart\"");
        client.Dispose();
    }

    [Test]
    public async Task The_add_workshop_form_posts_and_enqueues_a_config_apply_touching_workshop_items()
    {
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOperatorAsync(factory);
        (ServerId serverId, AgentId agent) = await SeedServerAndAgentAsync(factory, "addable");
        SeedInventory(factory, serverId, agent, installed: [], workshop: ["100"], enabled: []);

        string page = await (await client.GetAsync(new Uri($"/servers/{serverId}", UriKind.Relative))).Content.ReadAsStringAsync();
        Dictionary<string, string> form = new(StringComparer.Ordinal)
        {
            ["__RequestVerificationToken"] = ParseHiddenInputs(page)["__RequestVerificationToken"],
            ["_handler"] = "mod-manage",
            ["_modManageForm.WorkshopId"] = "200",
            ["_modManageForm.Command"] = "add",
        };
        HttpResponseMessage post = await client.PostAsync(new Uri($"/servers/{serverId}", UriKind.Relative), new FormUrlEncodedContent(form));

        await Assert.That((int)post.StatusCode).IsLessThan(400);
        Operation? op = FirstOperation(factory, serverId, OperationKind.ConfigApply);
        await Assert.That(op).IsNotNull();
        await Assert.That(op!.IsMutating).IsTrue();
        await Assert.That(op.CommandPayload).Contains("WorkshopItems");
        await Assert.That(op.CommandPayload).Contains("200");
        client.Dispose();
    }

    [Test]
    public async Task The_enable_form_posts_and_enqueues_a_config_apply_touching_the_mods_list()
    {
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOperatorAsync(factory);
        (ServerId serverId, AgentId agent) = await SeedServerAndAgentAsync(factory, "enableable");
        SeedInventory(
            factory, serverId, agent,
            installed: [new InstalledWorkshopItem("100", [new InstalledMod("ModB", null)])],
            workshop: ["100"],
            enabled: []);

        string page = await (await client.GetAsync(new Uri($"/servers/{serverId}", UriKind.Relative))).Content.ReadAsStringAsync();
        Dictionary<string, string> form = new(StringComparer.Ordinal)
        {
            ["__RequestVerificationToken"] = ParseHiddenInputs(page)["__RequestVerificationToken"],
            ["_handler"] = "mod-manage",
            ["_modManageForm.EnableModId"] = "ModB",
            ["_modManageForm.Command"] = "enable",
        };
        HttpResponseMessage post = await client.PostAsync(new Uri($"/servers/{serverId}", UriKind.Relative), new FormUrlEncodedContent(form));

        await Assert.That((int)post.StatusCode).IsLessThan(400);
        Operation? op = FirstOperation(factory, serverId, OperationKind.ConfigApply);
        await Assert.That(op).IsNotNull();
        await Assert.That(op!.CommandPayload).Contains("Mods");
        await Assert.That(op.CommandPayload).Contains("ModB");
        client.Dispose();
    }

    [Test]
    public async Task The_disable_button_posts_and_enqueues_a_config_apply()
    {
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOperatorAsync(factory);
        (ServerId serverId, AgentId agent) = await SeedServerAndAgentAsync(factory, "disableable");
        SeedInventory(
            factory, serverId, agent,
            installed: [new InstalledWorkshopItem("100", [new InstalledMod("ModA", null)])],
            workshop: ["100"],
            enabled: ["ModA", "ModB"]);

        string page = await (await client.GetAsync(new Uri($"/servers/{serverId}", UriKind.Relative))).Content.ReadAsStringAsync();
        Dictionary<string, string> form = new(StringComparer.Ordinal)
        {
            ["__RequestVerificationToken"] = ParseHiddenInputs(page)["__RequestVerificationToken"],
            ["_handler"] = "mod-manage",
            ["_modManageForm.Command"] = "disable|ModB",
        };
        HttpResponseMessage post = await client.PostAsync(new Uri($"/servers/{serverId}", UriKind.Relative), new FormUrlEncodedContent(form));

        await Assert.That((int)post.StatusCode).IsLessThan(400);
        Operation? op = FirstOperation(factory, serverId, OperationKind.ConfigApply);
        await Assert.That(op).IsNotNull();
        await Assert.That(op!.CommandPayload).Contains("Mods");
        client.Dispose();
    }

    [Test]
    public async Task The_update_button_posts_and_enqueues_an_update_server_operation()
    {
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOperatorAsync(factory);
        (ServerId serverId, AgentId agent) = await SeedServerAndAgentAsync(factory, "updatable");
        SeedInventory(factory, serverId, agent, installed: [], workshop: ["100"], enabled: []);

        string page = await (await client.GetAsync(new Uri($"/servers/{serverId}", UriKind.Relative))).Content.ReadAsStringAsync();
        Dictionary<string, string> form = new(StringComparer.Ordinal)
        {
            ["__RequestVerificationToken"] = ParseHiddenInputs(page)["__RequestVerificationToken"],
            ["_handler"] = "mod-manage",
            ["_modManageForm.Command"] = "update",
        };
        HttpResponseMessage post = await client.PostAsync(new Uri($"/servers/{serverId}", UriKind.Relative), new FormUrlEncodedContent(form));

        await Assert.That((int)post.StatusCode).IsLessThan(400);
        await Assert.That(EnqueuedKind(factory, serverId, OperationKind.UpdateServer)).IsTrue();
        client.Dispose();
    }

    [Test]
    public async Task The_restart_button_posts_and_enqueues_a_restart_server_operation()
    {
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOperatorAsync(factory);
        (ServerId serverId, AgentId agent) = await SeedServerAndAgentAsync(factory, "restartable-mods");
        SeedInventory(factory, serverId, agent, installed: [], workshop: ["100"], enabled: []);

        string page = await (await client.GetAsync(new Uri($"/servers/{serverId}", UriKind.Relative))).Content.ReadAsStringAsync();
        Dictionary<string, string> form = new(StringComparer.Ordinal)
        {
            ["__RequestVerificationToken"] = ParseHiddenInputs(page)["__RequestVerificationToken"],
            ["_handler"] = "mod-manage",
            ["_modManageForm.Command"] = "restart",
        };
        HttpResponseMessage post = await client.PostAsync(new Uri($"/servers/{serverId}", UriKind.Relative), new FormUrlEncodedContent(form));

        await Assert.That((int)post.StatusCode).IsLessThan(400);
        await Assert.That(EnqueuedKind(factory, serverId, OperationKind.RestartServer)).IsTrue();
        client.Dispose();
    }

    [Test]
    public async Task The_logs_card_shows_and_prerenders_the_live_island_for_a_permitted_operator()
    {
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOperatorAsync(factory);
        ServerId serverId = await SeedServerAsync(factory, "log-viewable");

        string html = await (await client.GetAsync(new Uri($"/servers/{serverId}", UriKind.Relative))).Content.ReadAsStringAsync();

        await Assert.That(html).Contains("data-logs-card");
        await Assert.That(html).Contains("data-live-logs");
        // The interactive island prerenders its waiting state (no lines buffered yet).
        await Assert.That(html).Contains("data-log-empty");
        client.Dispose();
    }

    [Test]
    public async Task The_backups_card_shows_for_a_permitted_operator()
    {
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOperatorAsync(factory);
        ServerId serverId = await SeedServerAsync(factory, "backup-viewable");

        string html = await (await client.GetAsync(new Uri($"/servers/{serverId}", UriKind.Relative))).Content.ReadAsStringAsync();

        await Assert.That(html).Contains("data-backups-card");
        await Assert.That(html).Contains("data-action=\"backup-create\"");
        await Assert.That(html).Contains("data-backups-empty");
        client.Dispose();
    }

    [Test]
    public async Task The_take_backup_form_posts_and_enqueues_a_mutating_backup_operation()
    {
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOperatorAsync(factory);
        ServerId serverId = await SeedServerAsync(factory, "backup-takeable");

        string page = await (await client.GetAsync(new Uri($"/servers/{serverId}", UriKind.Relative))).Content.ReadAsStringAsync();
        Dictionary<string, string> form = new(StringComparer.Ordinal)
        {
            ["__RequestVerificationToken"] = ParseHiddenInputs(page)["__RequestVerificationToken"],
            ["_handler"] = "backup-manage",
            ["_backupForm.Command"] = "create",
        };
        HttpResponseMessage post = await client.PostAsync(new Uri($"/servers/{serverId}", UriKind.Relative), new FormUrlEncodedContent(form));

        await Assert.That((int)post.StatusCode).IsLessThan(400);
        Operation? op = FirstOperation(factory, serverId, OperationKind.Backup);
        await Assert.That(op).IsNotNull();
        await Assert.That(op!.IsMutating).IsTrue();
        await Assert.That(op.CommandPayload).Contains("Manual");
        client.Dispose();
    }

    [Test]
    public async Task The_delete_button_posts_and_enqueues_a_non_mutating_delete_backup_operation()
    {
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOperatorAsync(factory);
        (ServerId serverId, AgentId agent) = await SeedServerAndAgentAsync(factory, "backup-deletable");
        BackupId backupId = await SeedBackupAsync(factory, serverId, agent, "world-1.tar.gz");

        string page = await (await client.GetAsync(new Uri($"/servers/{serverId}", UriKind.Relative))).Content.ReadAsStringAsync();
        await Assert.That(page).Contains("data-backup-row");
        Dictionary<string, string> form = new(StringComparer.Ordinal)
        {
            ["__RequestVerificationToken"] = ParseHiddenInputs(page)["__RequestVerificationToken"],
            ["_handler"] = "backup-manage",
            ["_backupForm.Command"] = $"delete|{backupId}",
        };
        HttpResponseMessage post = await client.PostAsync(new Uri($"/servers/{serverId}", UriKind.Relative), new FormUrlEncodedContent(form));

        await Assert.That((int)post.StatusCode).IsLessThan(400);
        Operation? op = FirstOperation(factory, serverId, OperationKind.DeleteBackup);
        await Assert.That(op).IsNotNull();
        await Assert.That(op!.IsMutating).IsFalse();
        await Assert.That(op.CommandPayload).Contains("world-1.tar.gz");
        client.Dispose();
    }

    [Test]
    public async Task The_restore_button_posts_and_enqueues_a_mutating_restore_operation()
    {
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOperatorAsync(factory);
        (ServerId serverId, AgentId agent) = await SeedServerAndAgentAsync(factory, "backup-restorable");
        BackupId backupId = await SeedBackupAsync(factory, serverId, agent, "world-1.tar.gz");

        string page = await (await client.GetAsync(new Uri($"/servers/{serverId}", UriKind.Relative))).Content.ReadAsStringAsync();
        await Assert.That(page).Contains("data-action=\"backup-restore\"");
        Dictionary<string, string> form = new(StringComparer.Ordinal)
        {
            ["__RequestVerificationToken"] = ParseHiddenInputs(page)["__RequestVerificationToken"],
            ["_handler"] = "backup-manage",
            ["_backupForm.Command"] = $"restore|{backupId}",
        };
        HttpResponseMessage post = await client.PostAsync(new Uri($"/servers/{serverId}", UriKind.Relative), new FormUrlEncodedContent(form));

        await Assert.That((int)post.StatusCode).IsLessThan(400);
        Operation? op = FirstOperation(factory, serverId, OperationKind.Restore);
        await Assert.That(op).IsNotNull();
        await Assert.That(op!.IsMutating).IsTrue();
        await Assert.That(op.CommandPayload).Contains("world-1.tar.gz");
        await Assert.That(op.CommandPayload).Contains("abc123");
        client.Dispose();
    }

    private static async Task<BackupId> SeedBackupAsync(
        ZWardenWebAppFactory factory, ServerId server, AgentId agent, string archiveName)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ZWardenDbContext db = scope.ServiceProvider.GetRequiredService<ZWardenDbContext>();
        Backup backup = Backup.Record(server, agent, archiveName, 2048, "abc123", BackupReason.Manual, DateTimeOffset.UtcNow);
        db.Set<Backup>().Add(backup);
        await db.SaveChangesAsync();
        return backup.Id;
    }

    private static bool EnqueuedKind(ZWardenWebAppFactory factory, ServerId serverId, OperationKind kind)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ZWardenDbContext db = scope.ServiceProvider.GetRequiredService<ZWardenDbContext>();
        return db.Set<Operation>().Any(o => o.ServerId == serverId && o.Kind == kind);
    }

    private static Operation? FirstOperation(ZWardenWebAppFactory factory, ServerId serverId, OperationKind kind)
    {
        using IServiceScope scope = factory.Services.CreateScope();
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
        using IServiceScope scope = factory.Services.CreateScope();
        ZWardenDbContext db = scope.ServiceProvider.GetRequiredService<ZWardenDbContext>();
        Server server = Server.Import(agent, ServerId.New(), name, DateTimeOffset.UtcNow);
        db.Set<Server>().Add(server);
        await db.SaveChangesAsync();
        return (server.Id, agent);
    }

    private static async Task<HttpClient> SignedInOperatorAsync(ZWardenWebAppFactory factory)
    {
        await factory.CreateConfirmedUserAsync("op@zwarden.test", StrongPassword);
        await AuthorizationBootstrapper.EnsureSeededAsync(factory.Services, "op@zwarden.test");
        HttpClient client = factory.CreateWebClient();
        await LoginAsync(client, "op@zwarden.test", StrongPassword);
        return client;
    }

    private static async Task<ServerId> SeedServerAsync(ZWardenWebAppFactory factory, string name)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ZWardenDbContext db = scope.ServiceProvider.GetRequiredService<ZWardenDbContext>();
        Server server = Server.Import(AgentId.New(), ServerId.New(), name, DateTimeOffset.UtcNow);
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
