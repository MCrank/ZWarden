using System.Security.Claims;
using BlazorBlueprint.Components;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using ZWarden.Domain.Backups;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Operations;
using ZWarden.Domain.Servers;
using ZWarden.Infrastructure.Authorization;
using ZWarden.Infrastructure.Identity;
using ZWarden.Infrastructure.Persistence;
using ZWarden.Infrastructure.Tenancy;
using ZWarden.Web.Components.Pages.Servers;
using ZWarden.Web.Tests.Account;

namespace ZWarden.Web.Tests.Servers;

/// <summary>
/// Renders the interactive Server Detail page in bUnit on top of the real composed host (#299, decision D2). The
/// page's services come from the host's own container through a tenant scope (the scope a circuit's actions open via
/// <c>ActionScopeRunner</c>), so a click goes through the real Application services and lands in the real SQLite
/// database: the same end-to-end assertions the static form-POST tests made. Blueprint's JS is loose.
/// </summary>
internal sealed class ServerDetailHarness : IAsyncDisposable
{
    public const string OperatorEmail = "op@zwarden.test";
    private const string StrongPassword = "correct horse battery staple";

    private readonly AsyncServiceScope _scope;

    private ServerDetailHarness(ZWardenWebAppFactory factory, BunitContext context, AsyncServiceScope scope)
    {
        Factory = factory;
        Context = context;
        _scope = scope;
    }

    public ZWardenWebAppFactory Factory { get; }

    public BunitContext Context { get; }

    /// <summary>Boots the host and signs in <see cref="OperatorEmail"/> as the Tenant Owner (every permission).</summary>
    public static async Task<ServerDetailHarness> StartAsync(Action<IServiceCollection>? configureServices = null)
    {
        var factory = new ZWardenWebAppFactory { ConfigureTestServicesHook = configureServices };
        await factory.CreateConfirmedUserAsync(OperatorEmail, StrongPassword);
        await AuthorizationBootstrapper.EnsureSeededAsync(factory.Services, OperatorEmail);

        (Guid userId, TenantId tenant) = await FindUserAsync(factory, OperatorEmail);
        AsyncServiceScope scope = factory.Services.CreateTenantScope(tenant);

        // Every registration precedes the first resolve (SetRendererInfo resolves the renderer).
        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.AddBunitPersistentComponentState();
        context.AddAuthorization()
            .SetAuthorized(OperatorEmail)
            .SetClaims(
                new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                new Claim(ClaimsPrincipalTenantContext.TenantClaimType, tenant.ToString()));
        context.Services.AddFallbackServiceProvider(scope.ServiceProvider);
        context.SetRendererInfo(new RendererInfo("Server", isInteractive: true));
        return new ServerDetailHarness(factory, context, scope);
    }

    /// <summary>Adds an imported Server to the database.</summary>
    public async Task<ServerId> SeedServerAsync(string name)
    {
        await using AsyncServiceScope scope = Factory.Services.CreateSystemScope();
        ZWardenDbContext db = scope.ServiceProvider.GetRequiredService<ZWardenDbContext>();
        Server server = Server.Import(AgentId.New(), ServerId.New(), name, DateTimeOffset.UtcNow);
        db.Set<Server>().Add(server);
        await db.SaveChangesAsync();
        return server.Id;
    }

    /// <summary>Records a verified backup of <paramref name="serverId"/> on its own Agent.</summary>
    public async Task<BackupId> SeedBackupAsync(ServerId serverId, string archiveName)
    {
        await using AsyncServiceScope scope = Factory.Services.CreateSystemScope();
        ZWardenDbContext db = scope.ServiceProvider.GetRequiredService<ZWardenDbContext>();
        Server server = db.Set<Server>().Single(s => s.Id == serverId);
        Backup backup = Backup.Record(serverId, server.AgentId, archiveName, 2048, "abc123", BackupReason.Manual, DateTimeOffset.UtcNow);
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
