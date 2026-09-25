using System.Text.Json;
using Docker.DotNet;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ZWarden.Agent.Configuration;
using ZWarden.Agent.ControlPlane;
using ZWarden.Agent.Docker;
using ZWarden.Agent.Rcon;
using ZWarden.Agent.ServerConfig;
using ZWarden.Contracts.Protocol.Messages;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Servers;

namespace ZWarden.Agent.Servers;

/// <summary>The result of provisioning or recreating a Server's container (F14, #229).</summary>
/// <param name="Succeeded">Whether the Server now runs (or, for a stopped Recreate, holds) a container on the
/// requested pair.</param>
/// <param name="Ports">The host pair the Server's container ends on — the new pair on success, the previous pair after a
/// rolled-back Recreate — or <c>null</c> when the Server is left without a container.</param>
/// <param name="ContainerId">The Docker id of the container the Server ends on, or <c>null</c> when it has none.</param>
/// <param name="FailureReason">On failure, an actionable, Agent-authored reason; <c>null</c> on success.</param>
public sealed record ServerProvisionOutcome(bool Succeeded, PortAllocation? Ports, string? ContainerId, string? FailureReason);

/// <summary>
/// Builds a Server's canonical container from F13's closed create-template: the first time (<see cref="ProvisionAsync"/>,
/// F14) and again, preserving its data, when its host ports change (<see cref="RecreateAsync"/>, #229, ADR 0045). The
/// container's <c>/pz/data</c> and <c>/pz/server</c> binds are always derived from the ServerId, so the world, config
/// and installed PZ build live on the host and survive a recreate with no re-download. Host ports are the operator's
/// pair (validated and pre-flighted against every container on the daemon) or the next free stride; Docker's start is
/// the authority for a clash with a process outside Docker. The same recreate is how the heap (#230) and the PZ branch
/// (#258) change later. The heap is per server (#230): the container's limit is the heap plus the Agent's overhead.
/// </summary>
public interface IServerProvisioner
{
    /// <summary>Provisions <paramref name="serverId"/>'s container on the requested pair (or the next free stride), with
    /// the requested heap (or the Agent's default), seeds the initial settings, creates and starts it. Never throws for an
    /// operator-actionable failure.</summary>
    Task<ServerProvisionOutcome> ProvisionAsync(ServerId serverId, CreateServer request, CancellationToken cancellationToken);

    /// <summary>
    /// Recreates <paramref name="serverId"/>'s container on the requested pair (or its current pair) with the requested
    /// heap (or the heap it runs with now): if running, warn players (the request's plan) and stop safely; remove it;
    /// create from the same template; start it only if it was running. Refuses — changing nothing — an invalid heap, a
    /// container whose data binds are not the ServerId-derived ones, or an unavailable pair. On a create/start failure it
    /// rolls back to the previous pair, heap and run state. An absent container is created and started (repair).
    /// </summary>
    Task<ServerProvisionOutcome> RecreateAsync(
        ServerId serverId,
        RecreateServer request,
        OperationId operationId,
        IOperationProgressReporter progress,
        CancellationToken cancellationToken);
}

/// <inheritdoc />
public sealed partial class ServerProvisioner : IServerProvisioner
{
    private const int MaxDaemonDetailLength = 300;

    private readonly IContainerRuntime _runtime;
    private readonly IServerHostDirectories _hostDirectories;
    private readonly IRconServerConfig _rconConfig;
    private readonly IInitialSettingsSeeder _settingsSeeder;
    private readonly IServerRestartCoordinator _restartCoordinator;
    private readonly AgentOptions _options;
    private readonly ILogger<ServerProvisioner> _logger;

    public ServerProvisioner(
        IContainerRuntime runtime,
        IServerHostDirectories hostDirectories,
        IRconServerConfig rconConfig,
        IInitialSettingsSeeder settingsSeeder,
        IServerRestartCoordinator restartCoordinator,
        IOptions<AgentOptions> options,
        ILogger<ServerProvisioner> logger)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(hostDirectories);
        ArgumentNullException.ThrowIfNull(rconConfig);
        ArgumentNullException.ThrowIfNull(settingsSeeder);
        ArgumentNullException.ThrowIfNull(restartCoordinator);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _runtime = runtime;
        _hostDirectories = hostDirectories;
        _rconConfig = rconConfig;
        _settingsSeeder = settingsSeeder;
        _restartCoordinator = restartCoordinator;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ServerProvisionOutcome> ProvisionAsync(ServerId serverId, CreateServer request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Refusals that change nothing: the heap and the settings are operator input, re-validated here (defence in depth).
        if (ValidateHeap(request.HeapSizeBytes) is { } heapRefusal)
        {
            return Failed(heapRefusal);
        }

        if (request.Settings is { } settings && InitialSettingsSeeder.Validate(settings) is { } settingsRefusal)
        {
            return Failed($"{settingsRefusal} Nothing was created.");
        }

        PortAllocation ports;
        try
        {
            ports = await ResolvePortsAsync(serverId, request.GamePort, current: null, cancellationToken).ConfigureAwait(false);
        }
        catch (PortUnavailableException ex)
        {
            return Failed(ex.Message);
        }

        (string? containerId, string? failure) = await CreateAndStartAsync(
                SpecFor(serverId, ports, request.HeapSizeBytes), start: true, cancellationToken, request.Settings)
            .ConfigureAwait(false);
        return failure is null
            ? new ServerProvisionOutcome(true, ports, containerId, null)
            : Failed(failure);
    }

    /// <inheritdoc />
    public async Task<ServerProvisionOutcome> RecreateAsync(
        ServerId serverId,
        RecreateServer request,
        OperationId operationId,
        IOperationProgressReporter progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(progress);
        (int? gamePort, GracefulRestartPlan? plan) = (request.GamePort, request.Plan);

        // 1. Refusals that change nothing: an invalid heap; a container with other data binds (an import from elsewhere)
        // would lose its world to a template recreate; an unavailable pair would only fail after the server was taken down.
        if (ValidateHeap(request.HeapSizeBytes) is { } heapRefusal)
        {
            return Failed(heapRefusal);
        }

        ServerContainer? current = await _runtime.InspectServerAsync(serverId, cancellationToken).ConfigureAwait(false);
        if (current is not null && MountMismatch(serverId, current) is { } mismatch)
        {
            return Failed(mismatch);
        }

        PortAllocation target;
        try
        {
            target = await ResolvePortsAsync(serverId, gamePort, current, cancellationToken).ConfigureAwait(false);
        }
        catch (PortUnavailableException ex)
        {
            return Failed(ex.Message);
        }

        bool wasRunning = current?.IsRunning ?? false;

        // 2. Take the old container down: warn + safe stop if running (the FIFO save→quit, F15), then remove it.
        if (current is not null)
        {
            if (wasRunning)
            {
                await _restartCoordinator.WarnAsync(serverId, plan, operationId, progress, cancellationToken).ConfigureAwait(false);
                await ReportAsync(progress, operationId, 25, "Stopping the server safely.", cancellationToken).ConfigureAwait(false);
                try
                {
                    await _runtime.StopAsync(serverId, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (IsContainerFault(ex))
                {
                    return Failed($"Stopping the server failed, so nothing was changed: {Describe(ex)}");
                }
            }

            await ReportAsync(progress, operationId, 50, "Removing the old container (world data is kept).", cancellationToken).ConfigureAwait(false);
            try
            {
                await _runtime.RemoveAsync(serverId, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (IsContainerFault(ex) || ex is InvalidOperationException)
            {
                if (wasRunning)
                {
                    await TryStartExistingAsync(serverId, cancellationToken).ConfigureAwait(false);
                }

                return Failed($"Removing the old container failed, so nothing was changed: {Describe(ex)}");
            }
        }

        // 3. Build the new container from the same template. An absent container (repair) is started, like provisioning.
        await ReportAsync(progress, operationId, 75, $"Creating the container on ports {target.GamePort}/{target.DirectPort}.", cancellationToken)
            .ConfigureAwait(false);
        // The requested heap, else the heap the old container ran with (a port change must not reset it), else the default.
        long? heap = request.HeapSizeBytes ?? current?.HeapSizeBytes;
        (string? containerId, string? failure) = await CreateAndStartAsync(
            SpecFor(serverId, target, heap), start: current is null || wasRunning, cancellationToken).ConfigureAwait(false);
        if (failure is null)
        {
            return new ServerProvisionOutcome(true, target, containerId, null);
        }

        // 4. Roll back to the previous pair and run state, so a failed port change leaves the server as it was.
        if (current?.Ports is not { } previous)
        {
            return Failed($"{failure} The server has no container — recreate it again (e.g. on a different port) to repair it.");
        }

        LogRollingBack(serverId, previous.GamePort, failure);
        (string? rolledBackId, string? rollbackFailure) = await CreateAndStartAsync(
            SpecFor(serverId, previous, current.HeapSizeBytes), start: wasRunning, cancellationToken).ConfigureAwait(false);
        return rollbackFailure is null
            ? new ServerProvisionOutcome(false, previous, rolledBackId, $"{failure} The server was rolled back to ports {previous.GamePort}/{previous.DirectPort}.")
            : Failed($"{failure} Rolling back to ports {previous.GamePort}/{previous.DirectPort} also failed ({rollbackFailure}); "
                + "the server has no container — recreate it again to repair it.");
    }

    // The operator's pair (validated + pre-flighted), else the container's current pair, else the next free stride.
    private async Task<PortAllocation> ResolvePortsAsync(
        ServerId serverId, int? gamePort, ServerContainer? current, CancellationToken cancellationToken)
    {
        if (gamePort is { } requested)
        {
            return await _runtime.ClaimRequestedPortsAsync(requested, serverId, cancellationToken).ConfigureAwait(false);
        }

        return current?.Ports ?? await _runtime.AllocateNextPortsAsync(cancellationToken).ConfigureAwait(false);
    }

    // Prepare host data, create, and (optionally) start. A failed start removes the new container again, so the name is
    // free for a retry or a rollback. Returns the container id, or the actionable failure reason.
    private async Task<(string? ContainerId, string? Failure)> CreateAndStartAsync(
        PzContainerSpec spec, bool start, CancellationToken cancellationToken, InitialServerSettings? settings = null)
    {
        string containerId;
        try
        {
            // Materialise BOTH host-side bind-mount sources before the create (#184): the Docker Mounts API never
            // auto-creates them, and the Agent's own container filesystem is not where the daemon resolves them.
            _hostDirectories.EnsureCreated(spec);

            // Seed RCON into the Server's config on the (Agent-owned) data mount before the container first launches
            // (F18 D-2). Idempotent: a recreate keeps the existing password. Host-side write, no env var, no exec.
            _rconConfig.EnsureEnabled(spec.ServerId);

            // Seed the wizard's basic settings the same way, before PZ's first boot (#230 D3); provisioning only.
            if (settings is not null)
            {
                _settingsSeeder.Seed(spec.ServerId, settings);
            }

            containerId = await _runtime.CreateAsync(spec, cancellationToken).ConfigureAwait(false);
        }
        catch (ContainerCreateException ex)
        {
            // Actionable, Agent-authored reason (e.g. the pinned image is not pre-provisioned, ADR 0008 D5).
            return (null, ex.Message);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (null, $"Could not prepare the server's host data before launch: {ex.Message}");
        }

        if (!start)
        {
            return (containerId, null);
        }

        try
        {
            await _runtime.StartAsync(containerId, cancellationToken).ConfigureAwait(false);
            return (containerId, null);
        }
        catch (DockerApiException ex)
        {
            await TryRemoveAsync(spec.ServerId, cancellationToken).ConfigureAwait(false);
            string detail = DaemonMessage(ex);
            return (null, DockerFailureInterpreter.IsPortInUse(ex.ResponseBody)
                ? $"A host port of the pair {spec.Ports.GamePort}/{spec.Ports.DirectPort} is already in use on this host "
                    + $"(Docker: {detail}). Choose a different game port."
                : $"Starting the container failed (Docker: {detail}).");
        }
    }

    // A named heap gets heap + overhead as its limit (#230); none keeps the Agent's defaults (incl. an explicit limit).
    private PzContainerSpec SpecFor(ServerId serverId, PortAllocation ports, long? heapSizeBytes = null) => new(
        serverId,
        ContainerName: serverId.ToString(),
        ImageReference: _options.PzImageReference ?? string.Empty,
        NetworkName: _options.NetworkName,
        DataMountSource: Path.Combine(_options.DataMountRoot, serverId.ToString()),
        // The SteamCMD install lives in a host sibling of the data dir (F17): persistent, but outside the world-data
        // path the disk meter reads (F16), so the ~6.72 GiB install is not counted as world use.
        ServerMountSource: Path.Combine(_options.DataMountRoot, $"{serverId}.server"),
        Ports: ports,
        MemoryLimitBytes: heapSizeBytes is { } heap ? heap + _options.MemoryOverheadBytes : _options.DefaultMemoryLimitBytes,
        HeapSizeBytes: heapSizeBytes ?? _options.DefaultHeapSizeBytes);

    private static string? ValidateHeap(long? heapSizeBytes) =>
        heapSizeBytes is { } heap && ServerMemoryRules.ValidateHeap(heap) is { } reason
            ? $"{reason} Nothing was changed."
            : null;

    private string? MountMismatch(ServerId serverId, ServerContainer current)
    {
        PzContainerSpec expected = SpecFor(serverId, default);
        foreach ((string target, string source) in (ReadOnlySpan<(string, string)>)
            [(PzContainerFactory.DataMountTarget, expected.DataMountSource), (PzContainerFactory.ServerMountTarget, expected.ServerMountSource)])
        {
            if (!current.BindMounts.TryGetValue(target, out string? actual) || !string.Equals(actual, source, StringComparison.Ordinal))
            {
                return $"The server's container mounts {target} from '{actual ?? "(nothing)"}', not '{source}', so recreating it "
                    + "would not keep its data. Refusing; nothing was changed.";
            }
        }

        return null;
    }

    private async Task TryRemoveAsync(ServerId serverId, CancellationToken cancellationToken)
    {
        try
        {
            await _runtime.RemoveAsync(serverId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsContainerFault(ex) || ex is InvalidOperationException)
        {
            LogCleanupFailed(serverId, ex.Message);
        }
    }

    private async Task TryStartExistingAsync(ServerId serverId, CancellationToken cancellationToken)
    {
        try
        {
            await _runtime.StartAsync(serverId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsContainerFault(ex))
        {
            LogCleanupFailed(serverId, ex.Message);
        }
    }

    private static async Task ReportAsync(
        IOperationProgressReporter progress, OperationId operationId, int percent, string status, CancellationToken cancellationToken) =>
        await progress.ReportAsync(operationId, percent, status, cancellationToken).ConfigureAwait(false);

    private static bool IsContainerFault(Exception ex) =>
        ex is DockerApiException or ContainerNotFoundException or ForeignContainerException;

    private static string Describe(Exception ex) => ex is DockerApiException docker ? $"Docker: {DaemonMessage(docker)}" : ex.Message;

    // The daemon's own "message" from its JSON error body, bounded — never the whole raw response.
    private static string DaemonMessage(DockerApiException ex)
    {
        string? message = null;
        if (!string.IsNullOrWhiteSpace(ex.ResponseBody))
        {
            try
            {
                using JsonDocument body = JsonDocument.Parse(ex.ResponseBody);
                if (body.RootElement.ValueKind == JsonValueKind.Object
                    && body.RootElement.TryGetProperty("message", out JsonElement element)
                    && element.ValueKind == JsonValueKind.String)
                {
                    message = element.GetString();
                }
            }
            catch (JsonException)
            {
                message = ex.ResponseBody;
            }
        }

        message = string.IsNullOrWhiteSpace(message) ? $"HTTP {(int)ex.StatusCode}" : message.Trim();
        return message.Length <= MaxDaemonDetailLength ? message : message[..MaxDaemonDetailLength] + "…";
    }

    private static ServerProvisionOutcome Failed(string reason) => new(false, null, null, reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Recreating server {ServerId} failed; rolling back to game port {GamePort}: {Failure}")]
    private partial void LogRollingBack(ServerId serverId, ushort gamePort, string failure);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Best-effort container cleanup for server {ServerId} failed: {Detail}")]
    private partial void LogCleanupFailed(ServerId serverId, string detail);
}
