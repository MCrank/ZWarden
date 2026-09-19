using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using ZWarden.Application.Mods;
using ZWarden.Application.Workshop;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Operations;
using ZWarden.Domain.Security;
using ZWarden.Domain.Servers;
using ZWarden.Infrastructure.Authorization;
using ZWarden.Infrastructure.Persistence;
using ZWarden.Web.Tests.Account;

namespace ZWarden.Web.Tests.Servers;

/// <summary>
/// #110 PR-C: the adaptive Mod Browser section on the Server Detail rail. Keyless preview (paste an id or collection
/// URL → cards → Install) is always available; the free-text search grid lights up only when the tenant has a Steam
/// Web API key. Install hands off to F22's <c>AddWorkshopItemAsync</c>. All Workshop names/ids are untrusted and
/// escaped at render (trust-boundaries §8). Exercised over the real host, with the Steam-facing seams faked so no
/// test makes a live call (F12 rule).
/// </summary>
public sealed class ServerDetailModBrowserTests
{
    private const string StrongPassword = "correct horse battery staple";

    [Test]
    public async Task The_rail_shows_the_mod_browser_link_for_a_viewer()
    {
        await using ZWardenWebAppFactory factory = NewFactory(out _, out _, out _);
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
        await using ZWardenWebAppFactory factory = NewFactory(out _, out _, out FakeSettings settings);
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
        await using ZWardenWebAppFactory factory = NewFactory(out _, out _, out FakeSettings settings);
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
        await using ZWardenWebAppFactory factory = NewFactory(out FakePreview preview, out _, out _);
        preview.Result = WorkshopPreview.OfItem(
            new WorkshopItemMetadata("2392709985", Found: true, Title: "Brita Weapon Pack", SizeBytes: 123456));
        HttpClient client = await SignedInOperatorAsync(factory);
        ServerId serverId = await SeedServerAsync(factory, "mb-preview");

        string html = await PostSectionAsync(client, serverId, "modbrowser-preview", new()
        {
            ["_browserForm.Input"] = "2392709985",
            ["_browserForm.Command"] = "resolve",
        });

        await Assert.That(html).Contains("data-modbrowser-results");
        await Assert.That(html).Contains("data-modbrowser-item");
        await Assert.That(html).Contains("2392709985");
        await Assert.That(html).Contains("Brita Weapon Pack");
        client.Dispose();
    }

    [Test]
    public async Task A_hostile_workshop_title_is_rendered_escaped()
    {
        await using ZWardenWebAppFactory factory = NewFactory(out FakePreview preview, out _, out _);
        preview.Result = WorkshopPreview.OfItem(
            new WorkshopItemMetadata("111", Found: true, Title: "<script>alert(1)</script>"));
        HttpClient client = await SignedInOperatorAsync(factory);
        ServerId serverId = await SeedServerAsync(factory, "mb-xss");

        string html = await PostSectionAsync(client, serverId, "modbrowser-preview", new()
        {
            ["_browserForm.Input"] = "111",
            ["_browserForm.Command"] = "resolve",
        });

        await Assert.That(html).Contains("&lt;script&gt;");
        await Assert.That(html).DoesNotContain("<script>alert(1)");
        client.Dispose();
    }

    [Test]
    public async Task An_unresolvable_input_shows_a_message()
    {
        await using ZWardenWebAppFactory factory = NewFactory(out FakePreview preview, out _, out _);
        preview.Result = WorkshopPreview.Unresolvable;
        HttpClient client = await SignedInOperatorAsync(factory);
        ServerId serverId = await SeedServerAsync(factory, "mb-unresolvable");

        string html = await PostSectionAsync(client, serverId, "modbrowser-preview", new()
        {
            ["_browserForm.Input"] = "not-a-workshop-link",
            ["_browserForm.Command"] = "resolve",
        });

        await Assert.That(html).Contains("data-modbrowser-unresolvable");
        client.Dispose();
    }

    [Test]
    public async Task Install_from_a_preview_card_enqueues_a_config_apply_touching_workshop_items()
    {
        await using ZWardenWebAppFactory factory = NewFactory(out _, out _, out _);
        HttpClient client = await SignedInOperatorAsync(factory);
        (ServerId serverId, AgentId agent) = await SeedServerAndAgentAsync(factory, "mb-install");
        // AddWorkshopItem recomputes WorkshopItems= from the last observed inventory, so one must exist.
        SeedInventory(factory, serverId, agent, installed: [], workshop: ["100"]);

        string html = await PostSectionAsync(client, serverId, "modbrowser-preview", new()
        {
            ["_browserForm.Input"] = string.Empty,
            ["_browserForm.Command"] = "install|200",
        });

        Operation? op = FirstOperation(factory, serverId, OperationKind.ConfigApply);
        await Assert.That(op).IsNotNull();
        await Assert.That(op!.CommandPayload).Contains("WorkshopItems");
        await Assert.That(op.CommandPayload).Contains("200");
        await Assert.That(html).Contains("data-modbrowser-message");
        client.Dispose();
    }

    [Test]
    public async Task Search_renders_result_cards_when_keyed()
    {
        await using ZWardenWebAppFactory factory = NewFactory(out _, out FakeSearch search, out FakeSettings settings);
        settings.Available = true;
        search.Result = WorkshopSearchResults.From(
            [new WorkshopSearchResult("500", Title: "Hydrocraft", Subscriptions: 9001)]);
        HttpClient client = await SignedInOperatorAsync(factory);
        ServerId serverId = await SeedServerAsync(factory, "mb-search");

        string html = await PostSectionAsync(client, serverId, "modbrowser-search", new()
        {
            ["_searchForm.Query"] = "hydro",
            ["_searchForm.Command"] = "search",
        });

        await Assert.That(html).Contains("data-modbrowser-search-results");
        await Assert.That(html).Contains("Hydrocraft");
        await Assert.That(html).Contains("500");
        client.Dispose();
    }

    [Test]
    public async Task An_installed_item_preview_shows_compatibility_chips()
    {
        await using ZWardenWebAppFactory factory = NewFactory(out FakePreview preview, out _, out _);
        preview.Result = WorkshopPreview.OfItem(new WorkshopItemMetadata("100", Found: true, Title: "Installed Pack"));
        HttpClient client = await SignedInOperatorAsync(factory);
        (ServerId serverId, AgentId agent) = await SeedServerAndAgentAsync(factory, "mb-compat");
        // The item is on disk with a declared PZ version — the card enriches from the observed inventory.
        SeedInventory(
            factory, serverId, agent,
            installed: [new InstalledWorkshopItem("100", [new InstalledMod("ModA", "Mod A", PzVersion: "41.78")])],
            workshop: ["100"]);

        string html = await PostSectionAsync(client, serverId, "modbrowser-preview", new()
        {
            ["_browserForm.Input"] = "100",
            ["_browserForm.Command"] = "resolve",
        });

        await Assert.That(html).Contains("data-item-compat");
        await Assert.That(html).Contains("data-compat-pz");
        await Assert.That(html).Contains("41.78");
        // Already in WorkshopItems= — the card shows that state instead of an Install button.
        await Assert.That(html).Contains("data-item-configured");
        client.Dispose();
    }

    private static ZWardenWebAppFactory NewFactory(
        out FakePreview preview, out FakeSearch search, out FakeSettings settings)
    {
        FakePreview p = new();
        FakeSearch s = new();
        FakeSettings st = new();
        preview = p;
        search = s;
        settings = st;
        return new ZWardenWebAppFactory
        {
            ConfigureTestServicesHook = services =>
            {
                services.AddSingleton<IWorkshopMetadataService>(p);
                services.AddSingleton<IWorkshopSearchService>(s);
                services.AddSingleton<IWorkshopSettingsService>(st);
            },
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

    private static async Task<string> PostSectionAsync(
        HttpClient client, ServerId serverId, string handler, Dictionary<string, string> fields)
    {
        string page = await GetAsync(client, $"/servers/{serverId}?section=modbrowser");
        Dictionary<string, string> form = new(StringComparer.Ordinal)
        {
            ["__RequestVerificationToken"] = ParseHiddenInputs(page)["__RequestVerificationToken"],
            ["_handler"] = handler,
        };
        foreach ((string key, string value) in fields)
        {
            form[key] = value;
        }

        HttpResponseMessage post = await client.PostAsync(
            new Uri($"/servers/{serverId}?section=modbrowser", UriKind.Relative), new FormUrlEncodedContent(form));
        return await post.Content.ReadAsStringAsync();
    }

    private static Operation? FirstOperation(ZWardenWebAppFactory factory, ServerId serverId, OperationKind kind)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ZWardenDbContext db = scope.ServiceProvider.GetRequiredService<ZWardenDbContext>();
        return db.Set<Operation>().FirstOrDefault(o => o.ServerId == serverId && o.Kind == kind);
    }

    private static void SeedInventory(
        ZWardenWebAppFactory factory, ServerId server, AgentId agent,
        IReadOnlyList<InstalledWorkshopItem> installed, IReadOnlyList<string> workshop)
    {
        IModInventoryCache cache = factory.Services.GetRequiredService<IModInventoryCache>();
        cache.Record(new ModInventory(server, agent, installed, workshop, [], [], DateTimeOffset.UtcNow));
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

    private static async Task<(ServerId Server, AgentId Agent)> SeedServerAndAgentAsync(
        ZWardenWebAppFactory factory, string name)
    {
        AgentId agent = AgentId.New();
        using IServiceScope scope = factory.Services.CreateScope();
        ZWardenDbContext db = scope.ServiceProvider.GetRequiredService<ZWardenDbContext>();
        Server server = Server.Import(agent, ServerId.New(), name, DateTimeOffset.UtcNow);
        db.Set<Server>().Add(server);
        await db.SaveChangesAsync();
        return (server.Id, agent);
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
