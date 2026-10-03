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
using ZWarden.Web.Components.Pages.Servers;
using ZWarden.Web.Tests.Account;
using ZWarden.Infrastructure.Tenancy;

namespace ZWarden.Web.Tests.Servers;

/// <summary>
/// #110 PR-C: the adaptive Mod Browser section on the Server Detail rail. Keyless preview (paste an id or collection
/// URL → cards → Install) is always available; the free-text search grid lights up only when the tenant has a Steam
/// Web API key. Install (#291) writes the item and its description's mod ids in one apply, with a part picker for a
/// multi-mod item. All Workshop names/ids are untrusted and
/// escaped at render (trust-boundaries §8). The rendered page is checked over the real host; the actions run as
/// circuit handlers in bUnit on the same host (#299). The Steam-facing seams are faked so no test makes a live call
/// (F12 rule).
/// </summary>
public sealed class ServerDetailModBrowserTests
{
    private const string StrongPassword = "correct horse battery staple";

    [Test]
    public async Task The_rail_shows_the_mod_browser_link_for_a_viewer()
    {
        await using ZWardenWebAppFactory factory = new() { ConfigureTestServicesHook = Fakes(out _, out _, out _) };
        HttpClient client = await SignedInOperatorAsync(factory);
        ServerId serverId = await SeedServerAsync(factory, "mb-rail");

        string html = await GetAsync(client, $"/servers/{serverId}");

        await Assert.That(html).Contains("data-rail-item=\"modbrowser\"");
        await Assert.That(html).Contains("?section=modbrowser");
        client.Dispose();
    }

    [Test]
    public async Task The_section_renders_the_keyless_lookup_and_hides_search_without_a_key()
    {
        await using ZWardenWebAppFactory factory = new() { ConfigureTestServicesHook = Fakes(out _, out _, out FakeSettings settings) };
        settings.Available = false;
        HttpClient client = await SignedInOperatorAsync(factory);
        ServerId serverId = await SeedServerAsync(factory, "mb-keyless");

        string html = await GetAsync(client, $"/servers/{serverId}?section=modbrowser");

        await Assert.That(html).Contains("data-modbrowser-card");
        await Assert.That(html).Contains("data-modbrowser-lookup");
        await Assert.That(html).Contains("data-action=\"modbrowser-resolve\"");
        // Keyless: the search box is not rendered; the "add a key" note is.
        await Assert.That(html).Contains("data-modbrowser-search-off");
        await Assert.That(html).DoesNotContain("data-action=\"modbrowser-search\"");
        client.Dispose();
    }

    [Test]
    public async Task The_search_box_renders_when_a_key_is_configured()
    {
        await using ZWardenWebAppFactory factory = new() { ConfigureTestServicesHook = Fakes(out _, out _, out FakeSettings settings) };
        settings.Available = true;
        HttpClient client = await SignedInOperatorAsync(factory);
        ServerId serverId = await SeedServerAsync(factory, "mb-keyed");

        string html = await GetAsync(client, $"/servers/{serverId}?section=modbrowser");

        await Assert.That(html).Contains("data-modbrowser-search");
        await Assert.That(html).Contains("data-action=\"modbrowser-search\"");
        await Assert.That(html).DoesNotContain("data-modbrowser-search-off");
        client.Dispose();
    }

    [Test]
    public async Task Preview_resolves_a_pasted_reference_and_renders_a_card()
    {
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync(Fakes(out FakePreview preview, out _, out _));
        preview.Result = WorkshopPreview.OfItem(
            new WorkshopItemMetadata("2392709985", Found: true, Title: "Brita Weapon Pack", SizeBytes: 123456));
        ServerId serverId = await harness.SeedServerAsync("mb-preview");

        IRenderedComponent<ServerDetail> cut = await PreviewAsync(harness, serverId, "2392709985");

        await Assert.That(cut.Markup).Contains("data-modbrowser-item");
        await Assert.That(cut.Markup).Contains("2392709985");
        await Assert.That(cut.Markup).Contains("Brita Weapon Pack");
    }

    [Test]
    public async Task A_hostile_workshop_title_is_rendered_escaped()
    {
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync(Fakes(out FakePreview preview, out _, out _));
        preview.Result = WorkshopPreview.OfItem(new WorkshopItemMetadata("111", Found: true, Title: "<script>alert(1)</script>"));
        ServerId serverId = await harness.SeedServerAsync("mb-xss");

        IRenderedComponent<ServerDetail> cut = await PreviewAsync(harness, serverId, "111");

        await Assert.That(cut.Markup).Contains("&lt;script&gt;");
        await Assert.That(cut.Markup).DoesNotContain("<script>alert(1)");
    }

    [Test]
    public async Task An_unresolvable_input_shows_a_message()
    {
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync(Fakes(out FakePreview preview, out _, out _));
        preview.Result = WorkshopPreview.Unresolvable;
        ServerId serverId = await harness.SeedServerAsync("mb-unresolvable");
        IRenderedComponent<ServerDetail> cut = harness.Render(serverId, "modbrowser");

        await InteractivePageHarness.TypeAsync(cut, "modbrowser-input", "not-a-workshop-link");
        await cut.Find("[data-modbrowser-lookup]").Closest("form")!.SubmitAsync();

        cut.WaitForState(() => cut.FindAll("[data-modbrowser-unresolvable]").Count == 1);
    }

    [Test]
    public async Task Install_of_a_one_mod_item_writes_workshop_items_and_its_mod_id_in_one_apply()
    {
        // #291: the description's one "Mod ID:" is enabled with the item, so one restart loads it.
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync(Fakes(out FakePreview preview, out _, out _));
        preview.Result = WorkshopPreview.OfItem(
            new WorkshopItemMetadata("200", Found: true, Title: "New Pack", Description: "Great pack.\nMod ID: NewPack"));
        ServerId serverId = await harness.SeedServerAsync("mb-install");
        // Install recomputes the lists from the last observed inventory, so one must exist.
        harness.SeedInventory(serverId, installed: [], workshop: ["100"], enabled: ["A"]);
        IRenderedComponent<ServerDetail> cut = await PreviewAsync(harness, serverId, "200");

        await cut.Find("[data-modbrowser-item][data-workshop-id='200'] [data-action=mod-install]").ClickAsync(new());

        cut.WaitForState(() => harness.FirstOperation(serverId, OperationKind.ConfigApply) is not null);
        ConfigApplyPayload payload = ConfigApplyPayload.FromJson(harness.FirstOperation(serverId, OperationKind.ConfigApply)!.CommandPayload!);
        await Assert.That(payload.Edits.Select(e => $"{e.Path}={e.Value}")).IsEquivalentTo(["WorkshopItems=100;200", "Mods=A;NewPack"]);
        cut.WaitForState(() => cut.FindAll("[data-mod-install-message]").Count == 1);
        await Assert.That(cut.Find("[data-mod-install-message]").TextContent).Contains("NewPack");
        // The preview stays on screen across the install (no page reload).
        await Assert.That(cut.Markup).Contains("New Pack");
    }

    [Test]
    public async Task A_multi_mod_item_opens_the_part_picker_and_installs_only_the_ticked_parts()
    {
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync(Fakes(out FakePreview preview, out _, out _));
        preview.Result = WorkshopPreview.OfItem(new WorkshopItemMetadata(
            "300", Found: true, Title: "Trait Pack", Description: "Mod ID: Core\nMod ID: Extra\nMod ID: Patch"));
        ServerId serverId = await harness.SeedServerAsync("mb-picker");
        harness.SeedInventory(serverId, installed: [], workshop: [], enabled: []);
        IRenderedComponent<ServerDetail> cut = await PreviewAsync(harness, serverId, "300");

        await cut.Find("[data-workshop-id='300'] [data-action=mod-install]").ClickAsync(new());
        cut.WaitForState(() => cut.FindAll("[data-mod-install-part]").Count == 3);
        // Nothing is written until the operator confirms; every part starts ticked. Untick "Extra".
        await Assert.That(harness.FirstOperation(serverId, OperationKind.ConfigApply)).IsNull();
        await cut.Find("[data-mod-install-part][data-mod-id='Extra'] [role=checkbox]").ClickAsync(new());
        await cut.Find("[data-action=mod-install-confirm]").ClickAsync(new());

        cut.WaitForState(() => harness.FirstOperation(serverId, OperationKind.ConfigApply) is not null);
        ConfigApplyPayload payload = ConfigApplyPayload.FromJson(harness.FirstOperation(serverId, OperationKind.ConfigApply)!.CommandPayload!);
        await Assert.That(payload.Edits.Select(e => $"{e.Path}={e.Value}")).IsEquivalentTo(["WorkshopItems=300", "Mods=Core;Patch"]);
    }

    [Test]
    public async Task An_item_whose_page_lists_no_mod_ids_is_added_and_asks_for_parts_after_the_restart()
    {
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync(Fakes(out FakePreview preview, out _, out _));
        preview.Result = WorkshopPreview.OfItem(new WorkshopItemMetadata("400", Found: true, Title: "Map", Description: "A map."));
        ServerId serverId = await harness.SeedServerAsync("mb-noids");
        harness.SeedInventory(serverId, installed: [], workshop: [], enabled: ["A"]);
        IRenderedComponent<ServerDetail> cut = await PreviewAsync(harness, serverId, "400");

        await cut.Find("[data-workshop-id='400'] [data-action=mod-install]").ClickAsync(new());

        cut.WaitForState(() => harness.FirstOperation(serverId, OperationKind.ConfigApply) is not null);
        ConfigApplyPayload payload = ConfigApplyPayload.FromJson(harness.FirstOperation(serverId, OperationKind.ConfigApply)!.CommandPayload!);
        await Assert.That(payload.Edits.Select(e => $"{e.Path}={e.Value}")).IsEquivalentTo(["WorkshopItems=400"]);
        cut.WaitForState(() => cut.FindAll("[data-mod-install-message]").Count == 1);
        await Assert.That(cut.Find("[data-mod-install-message]").TextContent).Contains("choose its parts");
    }

    [Test]
    public async Task Search_renders_result_cards_when_keyed()
    {
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync(Fakes(out _, out FakeSearch search, out FakeSettings settings));
        settings.Available = true;
        search.Result = WorkshopSearchResults.From([new WorkshopSearchResult("500", Title: "Hydrocraft", Subscriptions: 9001)]);
        ServerId serverId = await harness.SeedServerAsync("mb-search");
        IRenderedComponent<ServerDetail> cut = harness.Render(serverId, "modbrowser");

        await InteractivePageHarness.TypeAsync(cut, "modbrowser-query", "hydro");
        await cut.Find("[data-modbrowser-search] form").SubmitAsync();

        cut.WaitForState(() => cut.FindAll("[data-modbrowser-search-results]").Count == 1);
        await Assert.That(cut.Markup).Contains("Hydrocraft");
        await Assert.That(cut.Markup).Contains("500");
    }

    [Test]
    public async Task An_installed_item_preview_shows_compatibility_chips()
    {
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync(Fakes(out FakePreview preview, out _, out _));
        preview.Result = WorkshopPreview.OfItem(new WorkshopItemMetadata("100", Found: true, Title: "Installed Pack"));
        ServerId serverId = await harness.SeedServerAsync("mb-compat");
        // The item is on disk with a declared PZ version — the card enriches from the observed inventory.
        harness.SeedInventory(
            serverId,
            installed: [new InstalledWorkshopItem("100", [new InstalledMod("ModA", "Mod A", PzVersion: "41.78")])],
            workshop: ["100"],
            enabled: []);

        IRenderedComponent<ServerDetail> cut = await PreviewAsync(harness, serverId, "100");

        await Assert.That(cut.Markup).Contains("data-item-compat");
        await Assert.That(cut.Markup).Contains("data-compat-pz");
        await Assert.That(cut.Markup).Contains("41.78");
        // Already in WorkshopItems= — the card shows that state instead of an Install button.
        await Assert.That(cut.Markup).Contains("data-item-configured");
    }

    // Opens the Mod Browser, pastes the reference and previews it; waits for the result cards.
    private static async Task<IRenderedComponent<ServerDetail>> PreviewAsync(InteractivePageHarness harness, ServerId serverId, string input)
    {
        IRenderedComponent<ServerDetail> cut = harness.Render(serverId, "modbrowser");
        await InteractivePageHarness.TypeAsync(cut, "modbrowser-input", input);
        await cut.Find("[data-modbrowser-lookup]").Closest("form")!.SubmitAsync();
        cut.WaitForState(() => cut.FindAll("[data-modbrowser-results]").Count == 1);
        return cut;
    }

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

        public Task<WorkshopPreview> ResolveAsync(
            UserId actor, ServerId server, string input, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result);
    }

    private sealed class FakeSearch : IWorkshopSearchService
    {
        public WorkshopSearchResults Result { get; set; } = WorkshopSearchResults.Unavailable;

        public Task<WorkshopSearchResults> SearchAsync(string query, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result);
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
