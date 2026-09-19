using Microsoft.Extensions.DependencyInjection;
using ZWarden.Application.Workshop;

namespace ZWarden.Infrastructure.Workshop;

/// <summary>
/// Composition seam for the Workshop metadata client and the optional, per-tenant search key (#110). Registers
/// a typed <see cref="System.Net.Http.HttpClient"/> pinned to Valve's API host with a bounded response buffer
/// and a short timeout, the in-memory cache the client reuses, and the tenant-scoped
/// <see cref="IWorkshopSettingsService"/> that owns the encrypted search key (ADR 0044). The egress is
/// deliberately control-plane only (never the Agent). Call it after <c>AddZWardenAuthorization</c>,
/// <c>AddZWardenAudit</c>, and the security foundation (the settings service resolves the fail-closed
/// <c>IPermissionChecker</c>, the <c>IAuditWriter</c>, the request-scoped <c>ZWardenDbContext</c>, and
/// <c>ISecretProtector</c>).
/// </summary>
public static class WorkshopServiceCollectionExtensions
{
    private const string SteamApiBaseAddress = "https://api.steampowered.com/";

    public static IServiceCollection AddZWardenWorkshop(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddMemoryCache();
        services.AddHttpClient<IWorkshopMetadataClient, WorkshopMetadataClient>(client =>
        {
            client.BaseAddress = new Uri(SteamApiBaseAddress);
            client.Timeout = TimeSpan.FromSeconds(10);

            // Metadata responses are small; cap the buffer as an untrusted-response guard (~4 MB).
            client.MaxResponseContentBufferSize = 4 * 1024 * 1024;
        });

        // F110 PR-B: the optional, per-tenant search key. Request-scoped, like the other tenant-scoped services.
        services.AddScoped<WorkshopIntegrationSettingsRepository>();
        services.AddScoped<IWorkshopSettingsService, WorkshopSettingsService>();

        // F110 PR-B: key-gated Workshop search (QueryFiles). Its own typed client so its timeout/buffer are
        // independent of the keyless metadata client; it pulls the decrypted key per request from the settings
        // service (never cached in plaintext). Same control-plane-only egress to Valve's API host.
        services.AddHttpClient<IWorkshopSearchService, WorkshopSearchClient>(client =>
        {
            client.BaseAddress = new Uri(SteamApiBaseAddress);
            client.Timeout = TimeSpan.FromSeconds(10);
            client.MaxResponseContentBufferSize = 4 * 1024 * 1024;
        });

        return services;
    }
}
