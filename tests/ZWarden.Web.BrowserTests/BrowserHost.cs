using System.Collections.Concurrent;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using TUnit.Core.Interfaces;
using ZWarden.Application.Configuration;
using ZWarden.Domain.Configuration;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Servers;
using ZWarden.Infrastructure.Authorization;
using ZWarden.Infrastructure.Persistence;
using ZWarden.Infrastructure.Tenancy;
using ZWarden.Web.Tests.Account;

namespace ZWarden.Web.BrowserTests;

/// <summary>
/// One real ZWarden host for the whole browser test session (#299): the composed <c>Program</c> served on Kestrel
/// over a throwaway SQLite file, a signed-in Tenant Owner, and a headless Chromium. There is no Agent, so live panels
/// show their empty states and every action stops at "enqueued"; that's enough for a smoke test. The configuration
/// read (an Agent round-trip) is faked so the config editor renders.
/// </summary>
public sealed class BrowserHost : IAsyncInitializer, IAsyncDisposable
{
    public const string OwnerEmail = "owner@zwarden.test";
    private const string OwnerPassword = "correct horse battery staple";

    private ZWardenWebAppFactory? _factory;
    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private string? _storageState;

    /// <summary>Where the host listens, as <c>http://localhost:{port}</c> (Chromium keeps the always-Secure
    /// cookies on <c>localhost</c> over plain http).</summary>
    public Uri BaseAddress { get; private set; } = default!;

    private readonly FakeConfigReader _configReader = new(ConfigFixtures.SandboxView());

    /// <summary>How many live configuration reads (Agent round-trips) <paramref name="server"/> has had (#312).</summary>
    public int ConfigReads(ServerId server) => _configReader.Reads(server);

    public async Task InitializeAsync()
    {
        _factory = new ZWardenWebAppFactory
        {
            ConfigureTestServicesHook = s => s.AddSingleton<IServerConfigurationReader>(_configReader),
        };
        _factory.UseKestrel(0);
        _factory.StartServer();

        string address = _factory.Services.GetRequiredService<IServer>().Features
            .Get<IServerAddressesFeature>()!.Addresses.First();
        var bound = new Uri(address);
        BaseAddress = new UriBuilder(bound) { Host = "localhost" }.Uri;

        await _factory.CreateConfirmedUserAsync(OwnerEmail, OwnerPassword);
        await AuthorizationBootstrapper.EnsureSeededAsync(_factory.Services, OwnerEmail);

        _playwright = await Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        _storageState = await SignInAsync(_browser);
    }

    /// <summary>Adds a fresh Server (one per test, so one test's in-flight Operation never makes another's busy).</summary>
    public async Task<ServerId> SeedServerAsync(string name)
    {
        await using AsyncServiceScope scope = _factory!.Services.CreateSystemScope();
        ZWardenDbContext db = scope.ServiceProvider.GetRequiredService<ZWardenDbContext>();
        Server server = Server.Import(AgentId.New(), ServerId.New(), name, DateTimeOffset.UtcNow);
        db.Set<Server>().Add(server);
        await db.SaveChangesAsync();
        return server.Id;
    }

    /// <summary>Opens <paramref name="relativeUrl"/> as the signed-in owner in a new browser context, recording every
    /// console error and uncaught page error from the first byte.</summary>
    public async Task<BrowserSession> OpenAsync(string relativeUrl)
    {
        IBrowserContext context = await _browser!.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = BaseAddress.ToString(),
            StorageState = _storageState,
        });
        IPage page = await context.NewPageAsync();
        var session = new BrowserSession(context, page);
        await session.GotoAsync(relativeUrl);
        return session;
    }

    private async Task<string> SignInAsync(IBrowser browser)
    {
        await using IBrowserContext context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = BaseAddress.ToString(),
        });
        IPage page = await context.NewPageAsync();
        await page.GotoAsync("/login");
        await page.FillAsync("#email", OwnerEmail);
        await page.FillAsync("#password", OwnerPassword);
        await page.ClickAsync("button[type=submit]");
        await page.WaitForURLAsync(url => !url.Contains("/login", StringComparison.OrdinalIgnoreCase));
        return await context.StorageStateAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_browser is not null)
        {
            await _browser.DisposeAsync();
        }

        _playwright?.Dispose();
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }
    }

    private sealed class FakeConfigReader(ConfigDocumentView view) : IServerConfigurationReader
    {
        private readonly ConcurrentDictionary<ServerId, int> _reads = new();

        public int Reads(ServerId server) => _reads.GetValueOrDefault(server);

        public Task<ConfigDocumentView> ReadAsync(
            UserId user, ServerId server, PzConfigFile file, CancellationToken cancellationToken = default)
        {
            _reads.AddOrUpdate(server, 1, (_, count) => count + 1);
            return Task.FromResult(view);
        }
    }
}
