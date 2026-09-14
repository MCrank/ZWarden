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
        // The static-SSR form binds the username by its model-path field name.
        await Assert.That(html).Contains("name=\"_actionForm.Username\"");
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

    private static bool EnqueuedKind(ZWardenWebAppFactory factory, ServerId serverId, OperationKind kind)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ZWardenDbContext db = scope.ServiceProvider.GetRequiredService<ZWardenDbContext>();
        return db.Set<Operation>().Any(o => o.ServerId == serverId && o.Kind == kind);
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
