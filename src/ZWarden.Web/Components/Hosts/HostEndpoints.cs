using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using ZWarden.Application.Agents;
using ZWarden.Application.Servers;
using ZWarden.Domain.Authorization;
using ZWarden.Domain.Ids;
using ZWarden.Infrastructure.Identity;

namespace ZWarden.Web.Components.Hosts;

/// <summary>
/// The Hosts page's live telemetry (#170), polled by <c>live-status.js</c> on <c>/hosts</c> to move the card meters in
/// place. Gated by the tenant-wide <c>Agent.View</c> policy (the page's own gate), and <see cref="IAgentInventory"/>
/// re-checks it and returns only this tenant's Hosts — the cache is read only for those AgentIds, and it is keyed by
/// the reporting connection, so a report can't surface under another Host. Read from memory: no Agent round-trip, so
/// a viewer adds no Agent load. Never cached.
/// </summary>
public static class HostEndpoints
{
    public static IEndpointRouteBuilder MapHostEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet("/api/hosts/telemetry", async (ClaimsPrincipal principal, UserManager<ApplicationUser> users,
            IAgentInventory inventory, IHostCapacityCache capacity, TimeProvider clock, HttpContext http,
            CancellationToken ct) =>
        {
            if (!Guid.TryParse(users.GetUserId(principal), out Guid id))
            {
                return Results.Forbid();
            }

            IReadOnlyList<HostSummary> hosts = await inventory.ListHostsAsync(UserId.FromGuid(id), ct).ConfigureAwait(false);
            DateTimeOffset now = clock.GetUtcNow();
            http.Response.Headers.CacheControl = "no-store";
            return Results.Ok(hosts
                .Select(h => (h.Id, Telemetry: HostTelemetry.For(capacity.GetLatest(h.Id), h.IsConnected, now)))
                .Where(e => e.Telemetry is not null)
                .Select(e => Body(e.Id, e.Telemetry!)));
        }).RequireAuthorization(Permissions.AgentView.Name);

        return endpoints;
    }

    // The wire shape live-status.js reads; every value is observed data, written with textContent only.
    private static object Body(AgentId id, HostTelemetry t) => new
    {
        id = id.ToString(),
        cpuPercent = t.CpuPercent,
        memoryUsedBytes = t.MemoryUsedBytes,
        memoryTotalBytes = t.MemoryTotalBytes,
        memoryText = t.MemoryText,
        diskUsedBytes = t.DiskUsedBytes,
        diskTotalBytes = t.DiskTotalBytes,
        diskText = t.DiskText,
        cpuCoresText = t.CpuCoresText,
        loadText = t.LoadText,
        memoryAvailableText = t.MemoryAvailableText,
        diskUsedText = t.DiskUsedText,
        age = t.Age,
        stale = t.Stale,
    };
}
