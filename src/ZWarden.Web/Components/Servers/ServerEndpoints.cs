using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using ZWarden.Application.Agents;
using ZWarden.Application.Backups;
using ZWarden.Application.Operations;
using ZWarden.Application.Servers;
using ZWarden.Domain.Authorization;
using ZWarden.Domain.Backups;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Operations;
using ZWarden.Domain.Servers;
using ZWarden.Infrastructure.Identity;
using ZWarden.Web.Time;

namespace ZWarden.Web.Components.Servers;

/// <summary>
/// The operator-facing Server inventory + import surface (F14). Reads are gated by <b>authentication only</b>
/// and self-filter server-by-server in the service (a server-scoped <c>Server.View</c> policy checked with no
/// resource would deny outright, ADR 0018) — so the list returns exactly what the caller may view, fail-closed.
/// Import and discovery are gated by the tenant-wide <c>Server.Register</c> policy (F5) and re-checked in the
/// service (defence in depth). JSON endpoints protected against CSRF by the SameSite=Lax auth cookie; the rich
/// dashboard is the Blazor page. All Agent-reported text is untrusted (trust-boundaries.md §3) — escaped at
/// render by the UI.
/// </summary>
public static class ServerEndpoints
{
    public static IEndpointRouteBuilder MapServerEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        // The inventory list: any authenticated operator; the service returns only Servers the caller may view.
        endpoints.MapGet("/api/servers", async (
            ClaimsPrincipal principal,
            UserManager<ApplicationUser> users,
            IServerInventory inventory,
            CancellationToken cancellationToken) =>
        {
            IReadOnlyList<ServerSummary> list =
                await inventory.ListVisibleAsync(Actor(principal, users), cancellationToken).ConfigureAwait(false);
            return Results.Ok(list.Select(ToDto));
        }).RequireAuthorization();

        RouteGroupBuilder register = endpoints
            .MapGroup("/api")
            .RequireAuthorization(Permissions.ServerRegister.Name);

        // The discovered-but-unregistered containers on a host — the import picker's offerings.
        register.MapGet("/agents/{id}/discovered", async (
            string id,
            IServerInventory inventory,
            CancellationToken cancellationToken) =>
        {
            if (!AgentId.TryParse(id, out AgentId agentId))
            {
                return Results.BadRequest();
            }

            IReadOnlyList<DiscoveredServer> discovered =
                await inventory.ListDiscoveredUnregisteredAsync(agentId, cancellationToken).ConfigureAwait(false);
            return Results.Ok(discovered.Select(d => new
            {
                serverId = d.ServerId.ToString(),
                runState = d.RunState.ToString(),
            }));
        });

        register.MapPost("/servers", async (
            RegisterServerRequest? request,
            ClaimsPrincipal principal,
            UserManager<ApplicationUser> users,
            IServerInventory inventory,
            CancellationToken cancellationToken) =>
        {
            if (request is null
                || !AgentId.TryParse(request.AgentId, out AgentId agentId)
                || string.IsNullOrWhiteSpace(request.Name))
            {
                return Results.BadRequest(new { error = "invalid_request" });
            }

            ServerRegisterResult result = await inventory
                .RegisterAsync(
                    Actor(principal, users),
                    new NewServerRequest(
                        agentId, request.Name.Trim(), request.GamePort, request.HeapSizeBytes, request.Settings,
                        request.AcknowledgeOvercommit),
                    cancellationToken)
                .ConfigureAwait(false);

            if (result.Succeeded)
            {
                // Accepted: the record exists; provisioning runs asynchronously as the returned Operation.
                return Results.Accepted(value: new
                {
                    serverId = result.Server!.Value.ToString(),
                    operationId = result.Operation!.Value.ToString(),
                });
            }

            return result.Failure switch
            {
                ServerRegisterFailure.NotAuthorized =>
                    Results.Json(new { error = "not_authorized" }, statusCode: StatusCodes.Status403Forbidden),
                ServerRegisterFailure.AgentNotFound =>
                    Results.Json(new { error = "agent_not_found" }, statusCode: StatusCodes.Status404NotFound),
                ServerRegisterFailure.InvalidPort => Results.BadRequest(new { error = "invalid_port" }),
                ServerRegisterFailure.PortInUse =>
                    Results.Json(new { error = "port_in_use" }, statusCode: StatusCodes.Status409Conflict),
                ServerRegisterFailure.InvalidHeap => Results.BadRequest(new { error = "invalid_heap" }),
                ServerRegisterFailure.InvalidSettings => Results.BadRequest(new { error = "invalid_settings" }),
                ServerRegisterFailure.OverCapacity =>
                    Results.Json(new { error = "over_capacity" }, statusCode: StatusCodes.Status409Conflict),
                _ => Results.BadRequest(new { error = "register_failed" }),
            };
        });

        // Lifecycle (F15): start / stop / restart. Authenticated at the edge; the service is the fail-closed
        // server-scoped gate (Server.Start/Stop/Restart against the specific Server) — a server-scoped policy
        // checked at the endpoint with no resource would deny outright (ADR 0018, the F14 D3 pattern). Each is a
        // fresh intent enqueued as a mutating, server-scoped Operation; the caller polls /api/operations/{id}.
        RouteGroupBuilder lifecycle = endpoints.MapGroup("/api/servers").RequireAuthorization();

        // The fleet board's live status (#253): every Server the caller may view (the inventory's fail-closed
        // Server.View filter), each resolved against the tenant's in-flight mutating Operations — read once for the
        // whole fleet, not once per row. Polled by live-status.js on /servers. Never cached. Each entry also carries the
        // Server's fleet facts (#257, FleetFacts — the same projection the first render uses): players and their age,
        // uptime, version, the CPU/memory meters and the KPI flags, read from the in-memory ownership-guarded metrics
        // cache — no Agent round-trip, so a page viewer adds no Agent or PZ load. Uptime and the sample age are
        // formatted here against the server clock, so a skewed browser clock can't distort them.
        lifecycle.MapGet("/status", async (ClaimsPrincipal principal, UserManager<ApplicationUser> users,
            IServerInventory inventory, IOperationStore operations, IServerMetricsCache metrics,
            IAgentConnectionRegistry connections, TimeProvider clock, HttpContext http, CancellationToken ct) =>
        {
            IReadOnlyList<ServerSummary> servers = await inventory.ListVisibleAsync(Actor(principal, users), ct).ConfigureAwait(false);
            IReadOnlyDictionary<ServerId, Operation> activeByServer = ServerLiveStatus.ActiveByServer(
                servers.Count == 0 ? [] : await operations.ListActiveAsync(ct).ConfigureAwait(false));
            DateTimeOffset now = clock.GetUtcNow();

            http.Response.Headers.CacheControl = "no-store";
            return Results.Ok(servers.Select(s => FleetStatusBody(
                s,
                ServerLiveStatus.Resolve(s.LastRunState, s.Id, activeByServer),
                FleetFacts.For(s, metrics.GetLatest(s.Id, s.AgentId), connections.IsConnected(s.AgentId)),
                now)));
        });

        // The live header status (#249): the observed run-state resolved against the Server's in-flight mutating
        // Operation, polled by live-status.js on the server-detail page. Fail-closed on Server.View (the inventory's
        // per-Server gate): an unknown or unviewable Server is 404, never a hint that it exists. Never cached.
        lifecycle.MapGet("/{id}/status", async (string id, ClaimsPrincipal principal, UserManager<ApplicationUser> users,
            IServerInventory inventory, IOperationStore operations, IOperatorTimeZoneProvider operatorTz, HttpContext http,
            CancellationToken ct) =>
        {
            if (!ServerId.TryParse(id, out ServerId serverId))
            {
                return Results.NotFound();
            }

            ServerSummary? server = await inventory.GetVisibleAsync(Actor(principal, users), serverId, ct).ConfigureAwait(false);
            if (server is null)
            {
                return Results.NotFound();
            }

            Operation? active = await operations.FindActiveForServerAsync(serverId, ct).ConfigureAwait(false);
            ServerStatusView view = ServerLiveStatus.Resolve(server.LastRunState, active?.Kind, active?.StatusLine);
            // #266: the last action's failure, unless a new action is already in flight (it supersedes the old one).
            ServerFailureView? failure = active is null
                ? ServerFailureView.From(await operations.FindUnresolvedFailureForServerAsync(serverId, ct).ConfigureAwait(false), operatorTz.Zone)
                : null;
            http.Response.Headers.CacheControl = "no-store";
            return Results.Ok(StatusBody(serverId, view, failure));
        });

        lifecycle.MapPost("/{id}/start", (string id, ClaimsPrincipal principal, UserManager<ApplicationUser> users,
            IServerLifecycle svc, CancellationToken ct) =>
            RunLifecycleAsync(id, principal, users, (u, s) => svc.StartAsync(u, s, ct)));

        lifecycle.MapPost("/{id}/stop", (string id, ClaimsPrincipal principal, UserManager<ApplicationUser> users,
            IServerLifecycle svc, CancellationToken ct) =>
            RunLifecycleAsync(id, principal, users, (u, s) => svc.StopAsync(u, s, ct)));

        lifecycle.MapPost("/{id}/restart", (string id, ClaimsPrincipal principal, UserManager<ApplicationUser> users,
            IServerLifecycle svc, CancellationToken ct) =>
            RunLifecycleAsync(id, principal, users, (u, s) => svc.RestartAsync(u, s, cancellationToken: ct)));

        // Update (F17): install/validate the PZ install via anonymous SteamCMD — a long, progress-reporting
        // mutating Operation. Same fail-closed server-scoped gate (Server.Update); poll /api/operations/{id}.
        lifecycle.MapPost("/{id}/update", (string id, ClaimsPrincipal principal, UserManager<ApplicationUser> users,
            IServerLifecycle svc, CancellationToken ct) =>
            RunLifecycleAsync(id, principal, users, (u, s) => svc.UpdateAsync(u, s, ct)));

        // Recreate (#229, ADR 0045): rebuild the Server's container from the closed template, preserving its data —
        // optionally on a new host port pair, warning players first if it is running. Same fail-closed server-scoped
        // gate (Server.Recreate) in the service; an empty body keeps the current pair and the default warning.
        lifecycle.MapPost("/{id}/recreate", (string id, RecreateServerRequest? request, ClaimsPrincipal principal,
            UserManager<ApplicationUser> users, IServerLifecycle svc, CancellationToken ct) =>
        {
            GracefulRestartPayload? plan = request?.WarningLeadSeconds is { } leads
                ? new GracefulRestartPayload(leads, request.Reason)
                : null;
            if (plan is not null
                && (GracefulRestartRules.ValidateSchedule(plan.WarningLeadSeconds) is not null
                    || GracefulRestartRules.ValidateReason(plan.Reason) is not null))
            {
                return Task.FromResult(Results.BadRequest(new { error = "invalid_plan" }));
            }

            return RunLifecycleAsync(id, principal, users, (u, s) => svc.RecreateAsync(u, s, request?.GamePort, plan, request?.HeapSizeBytes, ct));
        });

        // Backup (F24): take a backup of the Server's world data — a mutating, server-scoped Operation. Fail-closed
        // server-scoped gate (Backup.Create) in the service; poll /api/operations/{id} for the archive result.
        lifecycle.MapPost("/{id}/backup", async (string id, ClaimsPrincipal principal, UserManager<ApplicationUser> users,
            IServerBackup svc, CancellationToken ct) =>
        {
            if (!ServerId.TryParse(id, out ServerId serverId))
            {
                return Results.BadRequest(new { error = "invalid_request" });
            }

            BackupRequestResult result = await svc
                .CreateAsync(Actor(principal, users), serverId, BackupReason.Manual, ct).ConfigureAwait(false);
            if (result.Succeeded)
            {
                return Results.Accepted(
                    $"/api/operations/{result.Operation!.Value}", new { operationId = result.Operation!.Value.ToString() });
            }

            return result.Failure switch
            {
                BackupRequestFailure.NotAuthorized =>
                    Results.Json(new { error = "not_authorized" }, statusCode: StatusCodes.Status403Forbidden),
                BackupRequestFailure.ServerNotFound =>
                    Results.Json(new { error = "server_not_found" }, statusCode: StatusCodes.Status404NotFound),
                BackupRequestFailure.ServerBusy =>
                    Results.Json(new { error = "server_busy" }, statusCode: StatusCodes.Status409Conflict),
                _ => Results.BadRequest(new { error = "backup_failed" }),
            };
        });

        // Delete a backup (F24): remove the archive from the Agent host — a non-mutating, server-scoped Operation.
        // Fail-closed server-scoped gate (Backup.Delete) in the service; the record is removed on confirmed
        // completion. Backup-scoped route (the backup id resolves its Server and Agent).
        RouteGroupBuilder backups = endpoints.MapGroup("/api/backups").RequireAuthorization();
        backups.MapDelete("/{id}", async (string id, ClaimsPrincipal principal, UserManager<ApplicationUser> users,
            IServerBackup svc, CancellationToken ct) =>
        {
            if (!BackupId.TryParse(id, out BackupId backupId))
            {
                return Results.BadRequest(new { error = "invalid_request" });
            }

            BackupDeletionOutcome result = await svc.DeleteAsync(Actor(principal, users), backupId, ct).ConfigureAwait(false);
            if (result.Succeeded)
            {
                return Results.Accepted(
                    $"/api/operations/{result.Operation!.Value}", new { operationId = result.Operation!.Value.ToString() });
            }

            return result.Failure switch
            {
                BackupDeletionFailure.NotAuthorized =>
                    Results.Json(new { error = "not_authorized" }, statusCode: StatusCodes.Status403Forbidden),
                BackupDeletionFailure.BackupNotFound =>
                    Results.Json(new { error = "backup_not_found" }, statusCode: StatusCodes.Status404NotFound),
                _ => Results.BadRequest(new { error = "backup_delete_failed" }),
            };
        });

        // Restore a Server from a backup (F25): a mutating, server-scoped Operation on the backup's Agent. Fail-closed
        // server-scoped gate (Backup.Restore) in the service; the Agent verifies the archive, takes a protective
        // backup, and swaps the world atomically. Backup-scoped route (the backup id resolves its Server and Agent);
        // poll /api/operations/{id}. A running Server is refused (409) — stop it first.
        backups.MapPost("/{id}/restore", async (string id, ClaimsPrincipal principal, UserManager<ApplicationUser> users,
            IServerRestore svc, CancellationToken ct) =>
        {
            if (!BackupId.TryParse(id, out BackupId backupId))
            {
                return Results.BadRequest(new { error = "invalid_request" });
            }

            RestoreRequestResult result = await svc.RestoreAsync(Actor(principal, users), backupId, ct).ConfigureAwait(false);
            if (result.Succeeded)
            {
                return Results.Accepted(
                    $"/api/operations/{result.Operation!.Value}", new { operationId = result.Operation!.Value.ToString() });
            }

            return result.Failure switch
            {
                RestoreRequestFailure.NotAuthorized =>
                    Results.Json(new { error = "not_authorized" }, statusCode: StatusCodes.Status403Forbidden),
                RestoreRequestFailure.BackupNotFound =>
                    Results.Json(new { error = "backup_not_found" }, statusCode: StatusCodes.Status404NotFound),
                RestoreRequestFailure.ServerBusy =>
                    Results.Json(new { error = "server_busy" }, statusCode: StatusCodes.Status409Conflict),
                RestoreRequestFailure.ServerRunning =>
                    Results.Json(new { error = "server_running" }, statusCode: StatusCodes.Status409Conflict),
                _ => Results.BadRequest(new { error = "restore_failed" }),
            };
        });

        register.MapPost("/servers/import", async (
            ImportServerRequest? request,
            ClaimsPrincipal principal,
            UserManager<ApplicationUser> users,
            IServerInventory inventory,
            CancellationToken cancellationToken) =>
        {
            if (request is null
                || !AgentId.TryParse(request.AgentId, out AgentId agentId)
                || !ServerId.TryParse(request.ServerId, out ServerId serverId)
                || string.IsNullOrWhiteSpace(request.Name))
            {
                return Results.BadRequest(new { error = "invalid_request" });
            }

            ServerImportResult result = await inventory
                .ImportAsync(Actor(principal, users), agentId, serverId, request.Name.Trim(), cancellationToken)
                .ConfigureAwait(false);

            if (result.Succeeded)
            {
                return Results.Ok(new { serverId = result.Server!.Value.ToString() });
            }

            return result.Failure switch
            {
                ServerImportFailure.NotAuthorized =>
                    Results.Json(new { error = "not_authorized" }, statusCode: StatusCodes.Status403Forbidden),
                ServerImportFailure.AgentNotFound =>
                    Results.Json(new { error = "agent_not_found" }, statusCode: StatusCodes.Status404NotFound),
                ServerImportFailure.NotDiscovered =>
                    Results.Json(new { error = "not_discovered" }, statusCode: StatusCodes.Status404NotFound),
                _ => Results.BadRequest(new { error = "import_failed" }),
            };
        });

        return endpoints;
    }

    private static async Task<IResult> RunLifecycleAsync(
        string id,
        ClaimsPrincipal principal,
        UserManager<ApplicationUser> users,
        Func<UserId, ServerId, Task<ServerLifecycleResult>> action)
    {
        if (!ServerId.TryParse(id, out ServerId serverId))
        {
            return Results.BadRequest(new { error = "invalid_request" });
        }

        ServerLifecycleResult result = await action(Actor(principal, users), serverId).ConfigureAwait(false);
        if (result.Succeeded)
        {
            // Accepted: the Operation is enqueued and runs asynchronously; poll its state.
            return Results.Accepted(
                $"/api/operations/{result.Operation!.Value}",
                new { operationId = result.Operation!.Value.ToString() });
        }

        return result.Failure switch
        {
            ServerLifecycleFailure.NotAuthorized =>
                Results.Json(new { error = "not_authorized" }, statusCode: StatusCodes.Status403Forbidden),
            ServerLifecycleFailure.ServerNotFound =>
                Results.Json(new { error = "server_not_found" }, statusCode: StatusCodes.Status404NotFound),
            ServerLifecycleFailure.ServerBusy =>
                Results.Json(new { error = "server_busy" }, statusCode: StatusCodes.Status409Conflict),
            ServerLifecycleFailure.InvalidPort => Results.BadRequest(new { error = "invalid_port" }),
            ServerLifecycleFailure.InvalidHeap => Results.BadRequest(new { error = "invalid_heap" }),
            ServerLifecycleFailure.PortInUse =>
                Results.Json(new { error = "port_in_use" }, statusCode: StatusCodes.Status409Conflict),
            _ => Results.BadRequest(new { error = "lifecycle_failed" }),
        };
    }

    private static object ToDto(ServerSummary s) => new
    {
        id = s.Id.ToString(),
        agentId = s.AgentId.ToString(),
        name = s.Name,
        description = s.Description,
        gamePort = s.GamePort,
        queryPort = s.QueryPort,
        lastRunState = s.LastRunState.ToString(),
        lastStateReportedAt = s.LastStateReportedAt,
        installedBuildId = s.InstalledBuildId,
    };

    // The live-status wire shape live-status.js reads, shared by the header (#249) and fleet (#253) endpoints.
    // #266: the header body also carries the last action's failure (null when there is none); the script writes it
    // with textContent only.
    private static object StatusBody(ServerId id, ServerStatusView view, ServerFailureView? failure = null) => new
    {
        id = id.ToString(),
        label = view.Label,
        tone = view.ToneKey,
        busy = view.Busy,
        canStart = view.CanStart,
        canStop = view.CanStop,
        canRestart = view.CanRestart,
        detail = view.Detail,
        failure = failure is null ? null : new { operationId = failure.OperationId, action = failure.Action, reason = failure.Reason, at = failure.At },
    };

    // One fleet-board entry (#257): the #253 status fields plus the fleet facts. Every value is observed data; the
    // script writes it with textContent only.
    private static object FleetStatusBody(ServerSummary server, ServerStatusView view, FleetServerFacts facts, DateTimeOffset now) => new
    {
        id = server.Id.ToString(),
        label = view.Label,
        tone = view.ToneKey,
        busy = view.Busy,
        canStart = view.CanStart,
        canStop = view.CanStop,
        canRestart = view.CanRestart,
        detail = view.Detail,
        name = server.Name,
        running = facts.IsRunning,
        attention = facts.NeedsAttention,
        players = facts.Players,
        playersAge = facts.PlayersSampledAt is { } counted ? FleetFacts.FormatSampleAge(counted, now) : null,
        uptime = FleetFacts.FormatUptime(facts.StartedAt, now),
        version = facts.Version ?? FleetFacts.Dash,
        versionTitle = FleetFacts.FormatVersionTitle(facts),
        cpuPercent = facts.CpuPercent,
        memoryUsedBytes = facts.MemoryUsedBytes,
        memoryLimitBytes = facts.MemoryLimitBytes,
    };

    private static UserId Actor(ClaimsPrincipal principal, UserManager<ApplicationUser> users)
        => Guid.TryParse(users.GetUserId(principal), out Guid id)
            ? UserId.FromGuid(id)
            : throw new InvalidOperationException("The request is authorized but carries no user id claim.");
}

/// <summary>The body of an import request: which host, which discovered Server id, and an operator name.</summary>
public sealed record ImportServerRequest(string AgentId, string ServerId, string Name);

/// <summary>The body of a register request: which host to provision the new Server on, its name, and optionally the
/// host game port (#229; the pair is it and the port above — omitted ⇒ the next free stride), the JVM heap in bytes, the
/// initial settings, and the acknowledgement needed when the server would exceed the host's free memory (#230).</summary>
public sealed record RegisterServerRequest(
    string AgentId,
    string Name,
    int? GamePort = null,
    long? HeapSizeBytes = null,
    NewServerSettings? Settings = null,
    bool AcknowledgeOvercommit = false);

/// <summary>The optional body of a recreate request (#229): the new host game port (omitted ⇒ keep the current pair),
/// an optional graceful-warning schedule and reason (omitted ⇒ the Agent's default warning), and an optional new JVM heap in
/// bytes (#230; omitted ⇒ keep the current heap).</summary>
public sealed record RecreateServerRequest(
    int? GamePort = null, IReadOnlyList<int>? WarningLeadSeconds = null, string? Reason = null, long? HeapSizeBytes = null);
