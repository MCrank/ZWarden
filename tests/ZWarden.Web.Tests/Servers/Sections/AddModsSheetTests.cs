using System.Net;
using System.Text.RegularExpressions;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using ZWarden.Application.Configuration;
using ZWarden.Application.Mods;
using ZWarden.Application.Workshop;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Operations;
using ZWarden.Domain.Security;
using ZWarden.Domain.Servers;
using ZWarden.Infrastructure.Authorization;
using ZWarden.Infrastructure.Persistence;
using ZWarden.Infrastructure.Tenancy;
using ZWarden.Web.Components.Pages.Servers;
using ZWarden.Web.Tests.Account;

namespace ZWarden.Web.Tests.Servers.Sections;

/// <summary>
/// #292: the Mods section's "+ Add mods" sheet, which replaces the #110 Mod Browser rail section. One box takes a
/// pasted Workshop link or id (keyless preview, always available) or — with a Steam Web API key — free-text search
/// (ADR 0044). Each card installs through #291's one-click Install (the item and its description's mod ids in one
/// apply, a part picker for a multi-mod item, required items offered), shows "Added ✓" once the item is on the
/// server, and blocks a Build 41-only item. Workshop text is untrusted and escaped at render (trust-boundaries §8).
/// The Steam-facing seams are faked so no test makes a live call (F12 rule).
/// </summary>
public sealed class AddModsSheetTests
{
    private const string StrongPassword = "correct horse battery staple";

    [Test]
    public async Task The_rail_has_one_mods_entry_and_no_mod_browser()
    {
        await using ZWardenWebAppFactory factory = new() { ConfigureTestServicesHook = Fakes(out _, out _, out _) };
        HttpClient client = await SignedInOperatorAsync(factory);
        ServerId serverId = await SeedServerAsync(factory, "add-rail");

        string html = await GetAsync(client, $"/servers/{serverId}");

        await Assert.That(html).Contains("data-rail-item=\"mods\"");
        await Assert.That(html).DoesNotContain("data-rail-item=\"modbrowser\"");
        client.Dispose();
    }

    [Test]
    public async Task The_old_mod_browser_link_lands_on_mods_with_the_sheet_open()
    {
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync(Fakes(out _, out _, out _));
        ServerId serverId = await harness.SeedServerAsync("add-legacy");

        IRenderedComponent<ServerDetail> cut = harness.Render(serverId, "modbrowser");

        cut.WaitForState(() => cut.FindAll("[data-add-mods-sheet]").Count == 1);
        await Assert.That(cut.FindAll("[data-mods-card]").Count).IsEqualTo(1);
    }

    [Test]
    public async Task The_add_button_opens_the_sheet_and_done_closes_it()
    {
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync(Fakes(out _, out _, out _));
        ServerId serverId = await harness.SeedServerAsync("add-open");
        IRenderedComponent<ServerDetail> cut = harness.Render(serverId, "mods");
        await Assert.That(cut.FindAll("[data-add-mods-sheet]")).IsEmpty();

        await cut.Find("[data-action=add-mods-open]").ClickAsync(new());
        cut.WaitForState(() => cut.FindAll("[data-add-mods-sheet]").Count == 1);
        await cut.Find("[data-action=add-mods-done]").ClickAsync(new());

        cut.WaitForState(() => cut.FindAll("[data-add-mods-sheet]").Count == 0);
    }

    [Test]
    public async Task Without_a_key_the_sheet_takes_links_and_says_how_to_search_by_name()
    {
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync(Fakes(out _, out _, out FakeSettings settings));
        settings.Available = false;
        ServerId serverId = await harness.SeedServerAsync("add-keyless");
        IRenderedComponent<ServerDetail> cut = await OpenAsync(harness, serverId);

        await Assert.That(cut.FindAll("[data-add-mods-search-off]").Count).IsEqualTo(1);
        await InteractivePageHarness.TypeAsync(cut, "add-mods-input", "hydrocraft");
        await cut.Find("[data-add-mods-find]").Closest("form")!.SubmitAsync();

        cut.WaitForState(() => cut.FindAll("[data-add-mods-note]").Count == 1);
        await Assert.That(cut.Find("[data-add-mods-note]").TextContent).Contains("isn't a Workshop link or id");
    }

    [Test]
    public async Task A_pasted_id_previews_a_card()
    {
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync(Fakes(out FakePreview preview, out _, out _));
        preview.Result = WorkshopPreview.OfItem(
            new WorkshopItemMetadata("2392709985", Found: true, Title: "Brita Weapon Pack", SizeBytes: 123456));
        ServerId serverId = await harness.SeedServerAsync("add-preview");

        IRenderedComponent<ServerDetail> cut = await FindAsync(harness, serverId, "2392709985");

        await Assert.That(cut.Find("[data-add-mods-card][data-workshop-id='2392709985']").TextContent).Contains("Brita Weapon Pack");
        await Assert.That(preview.LastInput).IsEqualTo("2392709985");
    }

    [Test]
    public async Task A_hostile_workshop_title_is_rendered_escaped()
    {
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync(Fakes(out FakePreview preview, out _, out _));
        preview.Result = WorkshopPreview.OfItem(new WorkshopItemMetadata("111", Found: true, Title: "<script>alert(1)</script>"));
        ServerId serverId = await harness.SeedServerAsync("add-xss");

        IRenderedComponent<ServerDetail> cut = await FindAsync(harness, serverId, "111");

        await Assert.That(cut.Markup).Contains("&lt;script&gt;");
        await Assert.That(cut.Markup).DoesNotContain("<script>alert(1)");
    }

    [Test]
    public async Task Install_of_a_one_mod_item_writes_workshop_items_and_its_mod_id_in_one_apply()
    {
        // #291: the description's one "Mod ID:" is enabled with the item, so one restart loads it.
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync(Fakes(out FakePreview preview, out _, out _));
        preview.Result = WorkshopPreview.OfItem(
            new WorkshopItemMetadata("200", Found: true, Title: "New Pack", Description: "Great pack.\nMod ID: NewPack"));
        ServerId serverId = await harness.SeedServerAsync("add-install");
        // Install recomputes the lists from the last observed inventory, so one must exist.
        harness.SeedInventory(serverId, installed: [], workshop: ["100"], enabled: ["A"]);
        IRenderedComponent<ServerDetail> cut = await FindAsync(harness, serverId, "200");

        await cut.Find("[data-add-mods-card][data-workshop-id='200'] [data-action=mod-install]").ClickAsync(new());

        cut.WaitForState(() => harness.FirstOperation(serverId, OperationKind.ConfigApply) is not null);
        await Assert.That(Edits(harness, serverId)).IsEquivalentTo(["WorkshopItems=100;200", "Mods=A;NewPack"]);
        // The card turns to "Added ✓" and keeps the confirmation; the results stay on screen.
        cut.WaitForState(() => cut.FindAll("[data-workshop-id='200'] [data-add-mods-added]").Count == 1);
        await Assert.That(cut.Find("[data-mod-install-message]").TextContent).Contains("NewPack");
        await Assert.That(cut.FindAll("[data-workshop-id='200'] [data-action=mod-install]")).IsEmpty();
    }

    [Test]
    public async Task A_multi_mod_item_says_so_and_installs_only_the_ticked_parts()
    {
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync(Fakes(out FakePreview preview, out _, out _));
        preview.Result = WorkshopPreview.OfItem(new WorkshopItemMetadata(
            "300", Found: true, Title: "Trait Pack", Description: "Mod ID: Core\nMod ID: Extra\nMod ID: Patch"));
        ServerId serverId = await harness.SeedServerAsync("add-picker");
        harness.SeedInventory(serverId, installed: [], workshop: [], enabled: []);
        IRenderedComponent<ServerDetail> cut = await FindAsync(harness, serverId, "300");

        await Assert.That(cut.Find("[data-add-mods-multipart]").TextContent).Contains("Contains 3 mods");
        await cut.Find("[data-workshop-id='300'] [data-action=mod-install]").ClickAsync(new());
        cut.WaitForState(() => cut.FindAll("[data-mod-install-part]").Count == 3);
        // Nothing is written until the operator confirms; every part starts ticked. Untick "Extra".
        await Assert.That(harness.FirstOperation(serverId, OperationKind.ConfigApply)).IsNull();
        await cut.Find("[data-mod-install-part][data-mod-id='Extra'] [role=checkbox]").ClickAsync(new());
        await cut.Find("[data-action=mod-install-confirm]").ClickAsync(new());

        cut.WaitForState(() => harness.FirstOperation(serverId, OperationKind.ConfigApply) is not null);
        await Assert.That(Edits(harness, serverId)).IsEquivalentTo(["WorkshopItems=300", "Mods=Core;Patch"]);
    }

    [Test]
    public async Task Required_items_are_offered_ticked_and_installed_in_the_same_apply()
    {
        // #291 D5 (search key configured): the item requires a library; both land in one apply, each with the
        // mod id its own description lists.
        Action<IServiceCollection> fakes = Fakes(out FakePreview preview, out _, out _);
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync(services =>
        {
            fakes(services);
            services.AddSingleton<IWorkshopDependencyService>(new FakeDependencies(
                new WorkshopItemMetadata("900", Found: true, Title: "Core Library", Description: "Mod ID: CoreLib")));
        });
        preview.Result = WorkshopPreview.OfItem(
            new WorkshopItemMetadata("200", Found: true, Title: "New Pack", Description: "Mod ID: NewPack"));
        ServerId serverId = await harness.SeedServerAsync("add-deps");
        harness.SeedInventory(serverId, installed: [], workshop: [], enabled: []);
        IRenderedComponent<ServerDetail> cut = await FindAsync(harness, serverId, "200");

        await cut.Find("[data-workshop-id='200'] [data-action=mod-install]").ClickAsync(new());
        cut.WaitForState(() => cut.FindAll("[data-mod-install-dependency][data-workshop-id='900']").Count == 1);
        await Assert.That(cut.Find("[data-mod-install-dependency]").TextContent).Contains("Core Library");
        await cut.Find("[data-action=mod-install-confirm]").ClickAsync(new());

        cut.WaitForState(() => harness.FirstOperation(serverId, OperationKind.ConfigApply) is not null);
        await Assert.That(Edits(harness, serverId)).IsEquivalentTo(["WorkshopItems=200;900", "Mods=NewPack;CoreLib"]);
    }

    [Test]
    public async Task An_item_whose_page_lists_no_mod_ids_is_added_and_asks_for_parts_after_the_restart()
    {
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync(Fakes(out FakePreview preview, out _, out _));
        preview.Result = WorkshopPreview.OfItem(new WorkshopItemMetadata("400", Found: true, Title: "Map", Description: "A map."));
        ServerId serverId = await harness.SeedServerAsync("add-noids");
        harness.SeedInventory(serverId, installed: [], workshop: [], enabled: ["A"]);
        IRenderedComponent<ServerDetail> cut = await FindAsync(harness, serverId, "400");

        await cut.Find("[data-workshop-id='400'] [data-action=mod-install]").ClickAsync(new());

        cut.WaitForState(() => harness.FirstOperation(serverId, OperationKind.ConfigApply) is not null);
        await Assert.That(Edits(harness, serverId)).IsEquivalentTo(["WorkshopItems=400"]);
        cut.WaitForState(() => cut.FindAll("[data-mod-install-message]").Count == 1);
        await Assert.That(cut.Find("[data-mod-install-message]").TextContent).Contains("choose its parts");
    }

    [Test]
    public async Task With_a_key_free_text_searches_and_a_build_41_only_hit_is_blocked()
    {
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync(Fakes(out FakePreview preview, out FakeSearch search, out FakeSettings settings));
        settings.Available = true;
        search.Result = WorkshopSearchResults.From(
        [
            new WorkshopSearchResult("500", Title: "Hydrocraft", Subscriptions: 9001, Tags: ["Build 42"]),
            new WorkshopSearchResult("501", Title: "Old Hydrocraft", Tags: ["Build 41"]),
        ]);
        ServerId serverId = await harness.SeedServerAsync("add-search");

        IRenderedComponent<ServerDetail> cut = await FindAsync(harness, serverId, "hydro");

        await Assert.That(search.LastQuery).IsEqualTo("hydro");
        await Assert.That(preview.LastInput).IsNull();
        await Assert.That(cut.Find("[data-add-mods-card][data-workshop-id='500']").TextContent).Contains("9,001 subscribers");
        await Assert.That(cut.FindAll("[data-workshop-id='500'] [data-action=mod-install]").Count).IsEqualTo(1);
        await Assert.That(cut.Find("[data-workshop-id='501'] [data-add-mods-b41]").TextContent).Contains("Build 41 only");
        await Assert.That(cut.Find("[data-workshop-id='501'] [data-action=mod-install-blocked]").HasAttribute("disabled")).IsTrue();
        await Assert.That(cut.FindAll("[data-workshop-id='501'] [data-action=mod-install]")).IsEmpty();
    }

    [Test]
    public async Task An_item_already_on_the_server_shows_added()
    {
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync(Fakes(out FakePreview preview, out _, out _));
        preview.Result = WorkshopPreview.OfItem(new WorkshopItemMetadata("100", Found: true, Title: "Installed Pack"));
        ServerId serverId = await harness.SeedServerAsync("add-added");
        harness.SeedInventory(
            serverId, installed: [new InstalledWorkshopItem("100", [new InstalledMod("ModA", null)])], workshop: ["100"], enabled: ["ModA"]);

        IRenderedComponent<ServerDetail> cut = await FindAsync(harness, serverId, "100");

        await Assert.That(cut.FindAll("[data-workshop-id='100'] [data-add-mods-added]").Count).IsEqualTo(1);
        await Assert.That(cut.FindAll("[data-workshop-id='100'] [data-action=mod-install]")).IsEmpty();
    }

    // Opens Mods with the sheet open (?add=1).
    private static async Task<IRenderedComponent<ServerDetail>> OpenAsync(InteractivePageHarness harness, ServerId serverId)
    {
        IRenderedComponent<ServerDetail> cut = harness.Render(serverId, "mods", "&add=1");
        cut.WaitForState(() => cut.FindAll("[data-add-mods-sheet]").Count == 1);
        await Task.CompletedTask;
        return cut;
    }

    // Opens the sheet, types the input and submits it; waits for the result cards.
    private static async Task<IRenderedComponent<ServerDetail>> FindAsync(InteractivePageHarness harness, ServerId serverId, string input)
    {
        IRenderedComponent<ServerDetail> cut = await OpenAsync(harness, serverId);
        await InteractivePageHarness.TypeAsync(cut, "add-mods-input", input);
        await cut.Find("[data-add-mods-find]").Closest("form")!.SubmitAsync();
        cut.WaitForState(() => cut.FindAll("[data-add-mods-results]").Count == 1);
        return cut;
    }

    private static IEnumerable<string> Edits(InteractivePageHarness harness, ServerId serverId) =>
        ConfigApplyPayload.FromJson(harness.FirstOperation(serverId, OperationKind.ConfigApply)!.CommandPayload!).Edits
            .Select(e => $"{e.Path}={e.Value}");

    private static Action<IServiceCollection> Fakes(out FakePreview preview, out FakeSearch search, out FakeSettings settings)
    {
        FakePreview p = new();
        FakeSearch s = new();
        FakeSettings st = new();
        preview = p;
        search = s;
        settings = st;
        return services =>
        {
            services.AddSingleton<IWorkshopMetadataService>(p);
            services.AddSingleton<IWorkshopSearchService>(s);
            services.AddSingleton<IWorkshopSettingsService>(st);
        };
    }

    private sealed class FakePreview : IWorkshopMetadataService
    {
        public WorkshopPreview Result { get; set; } = WorkshopPreview.Unresolvable;

        public string? LastInput { get; private set; }

        public Task<WorkshopPreview> ResolveAsync(
            UserId actor, ServerId server, string input, CancellationToken cancellationToken = default)
        {
            // ModInstallControl also resolves the item it installs; record only the sheet's own lookups.
            LastInput ??= input;
            return Task.FromResult(Result);
        }
    }

    private sealed class FakeDependencies(params WorkshopItemMetadata[] required) : IWorkshopDependencyService
    {
        public Task<IReadOnlyList<WorkshopItemMetadata>> GetRequiredItemsAsync(
            UserId actor, ServerId server, string workshopId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<WorkshopItemMetadata>>(required);
    }

    private sealed class FakeSearch : IWorkshopSearchService
    {
        public WorkshopSearchResults Result { get; set; } = WorkshopSearchResults.Unavailable;

        public string? LastQuery { get; private set; }

        public Task<WorkshopSearchResults> SearchAsync(string query, CancellationToken cancellationToken = default)
        {
            LastQuery = query;
            return Task.FromResult(Result);
        }
    }

    private sealed class FakeSettings : IWorkshopSettingsService
    {
        public bool Available { get; set; }

        public Task<bool> IsSearchAvailableAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Available);

        public Task SetApiKeyAsync(UserId actor, SecretString apiKey, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task ClearApiKeyAsync(UserId actor, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<SecretString?> GetActiveApiKeyAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<SecretString?>(null);
    }

    private static async Task<string> GetAsync(HttpClient client, string relativeUrl) =>
        await (await client.GetAsync(new Uri(relativeUrl, UriKind.Relative))).Content.ReadAsStringAsync();

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
        using AsyncServiceScope scope = factory.Services.CreateSystemScope();
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
