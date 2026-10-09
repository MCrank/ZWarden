using System.Security.Claims;
using BlazorBlueprint.Components;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ZWarden.Application.Mods;
using ZWarden.Application.Servers;
using ZWarden.Domain.Backups;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Mods;
using ZWarden.Domain.Operations;
using ZWarden.Domain.Servers;
using ZWarden.Infrastructure.Authorization;
using ZWarden.Infrastructure.Identity;
using ZWarden.Infrastructure.Persistence;
using ZWarden.Infrastructure.Tenancy;
using ZWarden.Web.Components.Pages.Servers;
using ZWarden.Web.Tests.Account;

namespace ZWarden.Web.Tests;

/// <summary>
/// Renders an interactive page (Server Detail, Settings) in bUnit on top of the real composed host (#299, D2). The
/// page's services come from the host's own container through a tenant scope (the scope a circuit's actions open via
/// <c>ActionScopeRunner</c>), so a click goes through the real Application services and lands in the real SQLite
/// database: the same end-to-end assertions the static form-POST tests made. Blueprint's JS is loose.
/// </summary>
internal sealed class InteractivePageHarness : IAsyncDisposable
{
    public const string OperatorEmail = "op@zwarden.test";
    private const string StrongPassword = "correct horse battery staple";

    private readonly AsyncServiceScope _scope;

    private InteractivePageHarness(
        ZWardenWebAppFactory factory, BunitContext context, BunitPersistentComponentState state, AsyncServiceScope scope)
    {
        Factory = factory;
        Context = context;
        State = state;
        _scope = scope;
    }

    public ZWardenWebAppFactory Factory { get; }

    public BunitContext Context { get; }

    /// <summary>The persisted component state: what a prerender or a pausing circuit wrote, and what a new one reads.</summary>
    public BunitPersistentComponentState State { get; }

    /// <summary>Boots the host and signs in <see cref="OperatorEmail"/> as the Tenant Owner (every permission). With
    /// <paramref name="prerendering"/> the page renders as the static prerender does, not in a circuit.</summary>
    public static async Task<InteractivePageHarness> StartAsync(
        Action<IServiceCollection>? configureServices = null, bool prerendering = false)
    {
        var factory = new ZWardenWebAppFactory { ConfigureTestServicesHook = configureServices };
        await factory.CreateConfirmedUserAsync(OperatorEmail, StrongPassword);
        await AuthorizationBootstrapper.EnsureSeededAsync(factory.Services, OperatorEmail);

        (Guid userId, TenantId tenant) = await FindUserAsync(factory, OperatorEmail);
        AsyncServiceScope scope = factory.Services.CreateTenantScope(tenant);

        // Every registration precedes the first resolve (SetRendererInfo resolves the renderer).
        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        BunitPersistentComponentState state = context.AddBunitPersistentComponentState();
        context.AddAuthorization()
            .SetAuthorized(OperatorEmail)
            .SetClaims(
                new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                new Claim(ClaimsPrincipalTenantContext.TenantClaimType, tenant.ToString()));
        context.Services.AddFallbackServiceProvider(scope.ServiceProvider);
        context.SetRendererInfo(new RendererInfo(prerendering ? "Static" : "Server", isInteractive: !prerendering));
        return new InteractivePageHarness(factory, context, state, scope);
    }

    /// <summary>Adds an imported Server to the database, on a host port pair when one is given.</summary>
    public async Task<ServerId> SeedServerAsync(string name, int? gamePort = null, int? queryPort = null)
    {
        await using AsyncServiceScope scope = Factory.Services.CreateSystemScope();
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

    /// <summary>Records <paramref name="serverId"/>'s mod inventory as its own Agent last reported it.</summary>
    public void SeedInventory(
        ServerId serverId,
        IReadOnlyList<InstalledWorkshopItem> installed,
        IReadOnlyList<string> workshop,
        IReadOnlyList<string> enabled)
    {
        using AsyncServiceScope scope = Factory.Services.CreateSystemScope();
        AgentId agent = scope.ServiceProvider.GetRequiredService<ZWardenDbContext>().Set<Server>().Single(s => s.Id == serverId).AgentId;
        Factory.Services.GetRequiredService<IModInventoryCache>()
            .Record(new ModInventory(serverId, agent, installed, workshop, enabled, [], DateTimeOffset.UtcNow));
    }

    /// <summary>Persists <paramref name="serverId"/>'s #290 mod state: what it booted with, what is configured now,
    /// and its tracked Workshop items (description guesses, <c>mod.info</c> ids, on disk or not).</summary>
    public async Task SeedModStateAsync(
        ServerId serverId,
        (string[] Workshop, string[] Mods) booted,
        (string[] Workshop, string[] Mods) configured,
        params (string WorkshopId, string[] Guessed, string[] Observed, bool OnDisk)[] items)
    {
        DateTimeOffset at = DateTimeOffset.UtcNow.AddMinutes(-10);
        await using AsyncServiceScope scope = Factory.Services.CreateSystemScope();
        ZWardenDbContext db = scope.ServiceProvider.GetRequiredService<ZWardenDbContext>();
        ServerModState state = ServerModState.For(serverId);
        state.MarkBooted(at);
        state.ObserveConfig(booted.Workshop, booted.Mods, at.AddSeconds(5));
        state.ObserveConfig(configured.Workshop, configured.Mods, at.AddSeconds(10));
        db.Set<ServerModState>().Add(state);

        foreach ((string workshopId, string[] guessed, string[] observed, bool onDisk) in items)
        {
            ServerWorkshopItem item = ServerWorkshopItem.Track(serverId, workshopId);
            item.ApplyMetadata($"Item {workshopId}", null, null, null, [], [.. guessed.Select(ModId)], at);
            item.ObserveDisk(onDisk, [.. observed.Select(ModId)], at);
            db.Set<ServerWorkshopItem>().Add(item);
        }

        await db.SaveChangesAsync();

        static PzModId ModId(string id) => PzModId.TryCreate(id, out PzModId valid) ? valid : throw new ArgumentException(id);
    }

    /// <summary>Makes Steam's version of a tracked item newer than its copy on disk (#275), with fresh Steam details so
    /// no refresh is due.</summary>
    public async Task SeedWorkshopUpdateAsync(ServerId serverId, string workshopId)
    {
        await using AsyncServiceScope scope = Factory.Services.CreateSystemScope();
        ZWardenDbContext db = scope.ServiceProvider.GetRequiredService<ZWardenDbContext>();
        ServerWorkshopItem item = await db.Set<ServerWorkshopItem>()
            .SingleAsync(i => i.ServerId == serverId && i.WorkshopId == workshopId);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        item.ApplyMetadata(item.Title, item.PreviewUrl, item.SizeBytes, now.AddHours(-1), item.Tags,
            [.. item.GuessedModIds.Select(ModId)], now);
        item.ObserveDisk(item.OnDisk, [.. item.ObservedModIds.Select(ModId)], now, installedUpdatedAt: now.AddDays(-1));
        await db.SaveChangesAsync();

        static PzModId ModId(string id) => PzModId.TryCreate(id, out PzModId valid) ? valid : throw new ArgumentException(id);
    }

    /// <summary>Records newly observed configured lists for <paramref name="serverId"/>, as the discovery that follows
    /// a finished config apply does (#292).</summary>
    public async Task ObserveModConfigAsync(ServerId serverId, string[] workshop, string[] mods)
    {
        await using AsyncServiceScope scope = Factory.Services.CreateSystemScope();
        ZWardenDbContext db = scope.ServiceProvider.GetRequiredService<ZWardenDbContext>();
        ServerModState state = await db.Set<ServerModState>().SingleAsync(s => s.ServerId == serverId);
        state.ObserveConfig(workshop, mods, DateTimeOffset.UtcNow);
        await db.SaveChangesAsync();
    }

    /// <summary>Records a boot of <paramref name="serverId"/> and the discovery after it, so what is configured now
    /// becomes what it booted with (#292: rows turn Active after Restart to apply).</summary>
    public async Task RecordBootAsync(ServerId serverId)
    {
        await using AsyncServiceScope scope = Factory.Services.CreateSystemScope();
        ZWardenDbContext db = scope.ServiceProvider.GetRequiredService<ZWardenDbContext>();
        ServerModState state = await db.Set<ServerModState>().SingleAsync(s => s.ServerId == serverId);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        state.MarkBooted(now);
        state.ObserveConfig([.. state.ConfiguredWorkshopIds], [.. state.ConfiguredModIds], now.AddSeconds(1));
        await db.SaveChangesAsync();
    }

    /// <summary>Records the host capacity <paramref name="serverId"/>'s Agent last reported.</summary>
    public void SeedHostCapacity(ServerId serverId, long total, long committed, long ownLimit, long ownHeap, long reserved)
    {
        using AsyncServiceScope scope = Factory.Services.CreateSystemScope();
        AgentId agent = scope.ServiceProvider.GetRequiredService<ZWardenDbContext>().Set<Server>().Single(s => s.Id == serverId).AgentId;
        Factory.Services.GetRequiredService<IHostCapacityCache>()
            .Record(new HostCapacity(agent, total, committed, ownLimit, ownHeap, reserved, DateTimeOffset.UtcNow));
    }

    /// <summary>The <c>CommandPayload</c> of the first Operation of <paramref name="kind"/> for the Server.</summary>
    public string? Payload(ServerId serverId, OperationKind kind) => FirstOperation(serverId, kind)?.CommandPayload;

    /// <summary>Records a verified backup of <paramref name="serverId"/> on its own Agent.</summary>
    public async Task<BackupId> SeedBackupAsync(ServerId serverId, string archiveName, string? warning = null)
    {
        await using AsyncServiceScope scope = Factory.Services.CreateSystemScope();
        ZWardenDbContext db = scope.ServiceProvider.GetRequiredService<ZWardenDbContext>();
        Server server = db.Set<Server>().Single(s => s.Id == serverId);
        Backup backup = Backup.Record(
            serverId, server.AgentId, archiveName, 2048, "abc123", BackupReason.Manual, DateTimeOffset.UtcNow, warning: warning);
        db.Set<Backup>().Add(backup);
        await db.SaveChangesAsync();
        return backup.Id;
    }

    /// <summary>Records <paramref name="state"/> as the Agent-observed run-state (it enables the matching header
    /// buttons).</summary>
    public async Task SetRunStateAsync(ServerId serverId, ServerRunState state)
    {
        await using AsyncServiceScope scope = Factory.Services.CreateSystemScope();
        ZWardenDbContext db = scope.ServiceProvider.GetRequiredService<ZWardenDbContext>();
        Server server = db.Set<Server>().Single(s => s.Id == serverId);
        server.RecordObservedState(state, DateTimeOffset.UtcNow);
        await db.SaveChangesAsync();
    }

    /// <summary>Renders <typeparamref name="TPage"/> as the page at <paramref name="url"/>.</summary>
    public IRenderedComponent<TPage> RenderPage<TPage>(string url)
        where TPage : IComponent
    {
        Context.Services.GetRequiredService<NavigationManager>().NavigateTo(url);
        return Context.Render<TPage>();
    }

    /// <summary>Renders <c>/servers/{id}</c> with an optional <c>?section=</c> (plus any extra query, e.g.
    /// <c>&amp;file=SandboxVars</c>) and waits until the page has loaded the Server.</summary>
    public IRenderedComponent<ServerDetail> Render(ServerId serverId, string? section = null, string extraQuery = "")
    {
        string query = section is null ? string.Empty : $"?section={section}{extraQuery}";
        Context.Services.GetRequiredService<NavigationManager>().NavigateTo($"/servers/{serverId}{query}");
        IRenderedComponent<ServerDetail> cut = Context.Render<ServerDetail>(p => p.Add(c => c.Id, serverId.ToString()));
        cut.WaitForAssertion(() =>
        {
            if (cut.FindAll("[data-server-rail], [data-server-missing]").Count == 0)
            {
                throw new InvalidOperationException("The page has not loaded the server yet.");
            }
        });
        return cut;
    }

    /// <summary>Types <paramref name="value"/> into the <c>BbInput</c> with <paramref name="id"/>. A BbInput reports its
    /// value through its own JS module (on blur or Enter), not a DOM event bUnit can raise, so this invokes the
    /// component's <c>ValueChanged</c> exactly as that module does.</summary>
    public static async Task TypeAsync<TComponent>(IRenderedComponent<TComponent> cut, string id, string value)
        where TComponent : IComponent
    {
        IRenderedComponent<BbInput> input = ((IRenderedComponent<IComponent>)cut).FindComponents<BbInput>().Single(c => c.Instance.Id == id);
        await cut.InvokeAsync(() => input.Instance.ValueChanged.InvokeAsync(value));
    }

    /// <summary>The first Operation of <paramref name="kind"/> enqueued for <paramref name="serverId"/>, if any.</summary>
    public Operation? FirstOperation(ServerId serverId, OperationKind kind)
    {
        using AsyncServiceScope scope = Factory.Services.CreateSystemScope();
        ZWardenDbContext db = scope.ServiceProvider.GetRequiredService<ZWardenDbContext>();
        return db.Set<Operation>().FirstOrDefault(o => o.ServerId == serverId && o.Kind == kind);
    }

    private static async Task<(Guid UserId, TenantId Tenant)> FindUserAsync(ZWardenWebAppFactory factory, string email)
    {
        await using AsyncServiceScope scope = factory.Services.CreateSystemScope();
        UserManager<ApplicationUser> users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        ApplicationUser user = await users.FindByEmailAsync(email)
            ?? throw new InvalidOperationException($"No user {email}.");
        return (user.UserId.Value, user.TenantId);
    }

    public async ValueTask DisposeAsync()
    {
        await Context.DisposeAsync();
        await _scope.DisposeAsync();
        await Factory.DisposeAsync();
    }
}
