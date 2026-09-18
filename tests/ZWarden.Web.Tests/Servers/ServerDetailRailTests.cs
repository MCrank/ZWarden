using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Operations;
using ZWarden.Domain.Servers;
using ZWarden.Infrastructure.Authorization;
using ZWarden.Infrastructure.Persistence;
using ZWarden.Web.Tests.Account;

namespace ZWarden.Web.Tests.Servers;

/// <summary>
/// #162: the Server Detail page is a sticky header + a grouped vertical icon rail + a full-width content pane.
/// The rail switches sections by the <c>?section=</c> query (static SSR, bookmarkable): only the selected
/// section renders, so exactly one Live* island mounts at a time. Each section keeps its existing per-server,
/// fail-closed authorization; the new header lifecycle controls (F15) enqueue Start/Stop/Restart Operations.
/// Exercised over the real host.
/// </summary>
public sealed class ServerDetailRailTests
{
    private const string StrongPassword = "correct horse battery staple";

    [Test]
    public async Task The_page_renders_the_grouped_server_rail_for_a_permitted_operator()
    {
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOperatorAsync(factory);
        ServerId serverId = await SeedServerAsync(factory, "railed");

        string html = await GetAsync(client, $"/servers/{serverId}");

        await Assert.That(html).Contains("data-server-rail");
        // Overview is the landing section and links to the bare page (no ?section=).
        await Assert.That(html).Contains("data-rail-item=\"overview\"");
        // Every other section the Tenant Owner can reach appears as a rail item that links to its ?section=.
        foreach (string section in new[] { "players", "console", "logs", "config", "mods", "backups", "diagnostics" })
        {
            await Assert.That(html).Contains($"data-rail-item=\"{section}\"");
            await Assert.That(html).Contains($"?section={section}");
        }

        // The rail's grouped structure (issue #162): Operate / Configure / Maintain, plus the reserved
        // Mod Browser slot rendered as a disabled "soon" item under Configure.
        await Assert.That(html).Contains("Operate");
        await Assert.That(html).Contains("Configure");
        await Assert.That(html).Contains("Maintain");
        await Assert.That(html).Contains("data-rail-item=\"modbrowser\"");
        await Assert.That(html).Contains("data-rail-soon");
        client.Dispose();
    }

    [Test]
    public async Task The_default_view_is_overview_with_details_and_live_telemetry()
    {
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOperatorAsync(factory);
        ServerId serverId = await SeedServerAsync(factory, "overviewed");

        string html = await GetAsync(client, $"/servers/{serverId}");

        await Assert.That(html).Contains("data-section=\"overview\"");
        await Assert.That(html).Contains("Live telemetry");
        // With query-param switching, an unselected section is not rendered — only the active one.
        await Assert.That(html).DoesNotContain("data-players-card");
        client.Dispose();
    }

    [Test]
    public async Task The_players_section_renders_only_when_selected()
    {
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOperatorAsync(factory);
        ServerId serverId = await SeedServerAsync(factory, "player-section");

        string html = await GetAsync(client, $"/servers/{serverId}?section=players");

        await Assert.That(html).Contains("data-section=\"players\"");
        await Assert.That(html).Contains("data-players-card");
        // The live roster island still prerenders its awaiting state inside the selected section.
        await Assert.That(html).Contains("data-roster-awaiting");
        client.Dispose();
    }

    [Test]
    public async Task An_unknown_section_falls_back_to_overview()
    {
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOperatorAsync(factory);
        ServerId serverId = await SeedServerAsync(factory, "fallback");

        string html = await GetAsync(client, $"/servers/{serverId}?section=not-a-real-section");

        await Assert.That(html).Contains("data-section=\"overview\"");
        await Assert.That(html).Contains("Live telemetry");
        client.Dispose();
    }

    [Test]
    public async Task The_lifecycle_controls_render_in_the_header_for_a_permitted_operator()
    {
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOperatorAsync(factory);
        ServerId serverId = await SeedServerAsync(factory, "lifecycle-visible");

        // The sticky header renders above the rail regardless of the active section.
        string html = await GetAsync(client, $"/servers/{serverId}");

        await Assert.That(html).Contains("data-action=\"server-start\"");
        await Assert.That(html).Contains("data-action=\"server-stop\"");
        await Assert.That(html).Contains("data-action=\"server-restart\"");
        client.Dispose();
    }

    [Test]
    public async Task The_start_control_posts_and_enqueues_a_mutating_start_server_operation()
    {
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOperatorAsync(factory);
        ServerId serverId = await SeedServerAsync(factory, "startable");

        string page = await GetAsync(client, $"/servers/{serverId}");
        Dictionary<string, string> form = new(StringComparer.Ordinal)
        {
            ["__RequestVerificationToken"] = ParseHiddenInputs(page)["__RequestVerificationToken"],
            ["_handler"] = "server-lifecycle",
            ["_lifecycleForm.Command"] = "start",
        };
        HttpResponseMessage post = await client.PostAsync(new Uri($"/servers/{serverId}", UriKind.Relative), new FormUrlEncodedContent(form));

        await Assert.That((int)post.StatusCode).IsLessThan(400);
        Operation? op = FirstOperation(factory, serverId, OperationKind.StartServer);
        await Assert.That(op).IsNotNull();
        await Assert.That(op!.IsMutating).IsTrue();
        client.Dispose();
    }

    [Test]
    public async Task The_restart_control_posts_and_enqueues_a_restart_server_operation()
    {
        await using ZWardenWebAppFactory factory = new();
        HttpClient client = await SignedInOperatorAsync(factory);
        ServerId serverId = await SeedServerAsync(factory, "restartable-header");

        string page = await GetAsync(client, $"/servers/{serverId}");
        Dictionary<string, string> form = new(StringComparer.Ordinal)
        {
            ["__RequestVerificationToken"] = ParseHiddenInputs(page)["__RequestVerificationToken"],
            ["_handler"] = "server-lifecycle",
            ["_lifecycleForm.Command"] = "restart",
        };
        HttpResponseMessage post = await client.PostAsync(new Uri($"/servers/{serverId}", UriKind.Relative), new FormUrlEncodedContent(form));

        await Assert.That((int)post.StatusCode).IsLessThan(400);
        await Assert.That(FirstOperation(factory, serverId, OperationKind.RestartServer)).IsNotNull();
        client.Dispose();
    }

    private static async Task<string> GetAsync(HttpClient client, string relativeUrl) =>
        await (await client.GetAsync(new Uri(relativeUrl, UriKind.Relative))).Content.ReadAsStringAsync();

    private static Operation? FirstOperation(ZWardenWebAppFactory factory, ServerId serverId, OperationKind kind)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ZWardenDbContext db = scope.ServiceProvider.GetRequiredService<ZWardenDbContext>();
        return db.Set<Operation>().FirstOrDefault(o => o.ServerId == serverId && o.Kind == kind);
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
