using Microsoft.Extensions.DependencyInjection;
using ZWarden.Application.Workshop;

namespace ZWarden.Infrastructure.Workshop;

/// <summary>
/// Composition seam for the keyless Workshop metadata client (#110). Registers a typed <see cref="System.Net.Http.HttpClient"/>
/// pinned to Valve's API host with a bounded response buffer and a short timeout, plus the in-memory cache the client
/// reuses. The egress is deliberately control-plane only (never the Agent) and needs no secret; the opt-in publisher
/// key that unlocks search is a later, separately-wired concern (#110 PR-B).
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

        return services;
    }
}
