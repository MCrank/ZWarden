using System.Collections.Concurrent;
using Docker.DotNet;
using Microsoft.Extensions.Options;
using ZWarden.Agent.Backups;
using ZWarden.Agent.Configuration;
using ZWarden.Agent.Console;
using ZWarden.Agent.Diagnostics;
using ZWarden.Agent.Docker;
using ZWarden.Agent.Mods;
using ZWarden.Agent.Players;
using ZWarden.Agent.Servers;
using ZWarden.Agent.Rcon;
using ZWarden.Agent.ServerConfig;
using ZWarden.Agent.SteamCmd;
using ZWarden.Contracts.Protocol;
using ZWarden.Contracts.Protocol.Messages;
using ZWarden.Domain.Configuration;
using ZWarden.Domain.Ids;

namespace ZWarden.Agent.ControlPlane;

/// <summary>
/// Handles an <see cref="AgentCommand"/> the control plane dispatches (F11), deciding the reply to send back.
/// It deserializes the canonical wire envelope, dispatches on the command type, and <b>dedupes by
/// <c>OperationId</c></b> so a redelivered command (a reconnect replay) runs the work once (PRD 20). F11 added
/// <see cref="PingAgent"/> (an immediate round-trip); F13 adds <see cref="ProbeDockerHealth"/> (a non-mutating
/// Docker connectivity probe). The real mutating commands land with their owning features. Dispatch is
/// transport-free, so it is unit tested without a live connection; the connection wires it to the
/// <see cref="AgentHubProtocol.ReceiveCommand"/> channel.
/// </summary>
public sealed partial class AgentCommandProcessor
{
    private readonly TimeProvider _timeProvider;
    private readonly IContainerRuntime _containerRuntime;
    private readonly IServerHostDirectories _hostDirectories;
    private readonly IServerUpdateRunner _updates;
    private readonly IServerBackupRunner _backups;
    private readonly IServerRestoreRunner _restores;
    private readonly IRconHealthProbe _rconProbe;
    private readonly IRconServerConfig _rconConfig;
    private readonly IPlayerAdministration _players;
    private readonly IServerRestartCoordinator _restartCoordinator;
    private readonly IConsoleAdministration _console;
    private readonly IServerConfigWriter _configWriter;
    private readonly IServerConfigRawEditStaging _rawStaging;
    private readonly IServerConfigReloader _configReloader;
    private readonly IModDiscovery _modDiscovery;
    private readonly IHostDiagnosticsGatherer _hostDiagnostics;
    private readonly IServerDiagnosticsGatherer _serverDiagnostics;
    private readonly AgentOptions _options;
    private readonly ILogger<AgentCommandProcessor> _logger;
    private readonly ConcurrentDictionary<OperationId, byte> _handled = new();

    public AgentCommandProcessor(
        TimeProvider timeProvider,
        IContainerRuntime containerRuntime,
        IServerHostDirectories hostDirectories,
        IServerUpdateRunner updates,
        IServerBackupRunner backups,
        IServerRestoreRunner restores,
        IRconHealthProbe rconProbe,
        IRconServerConfig rconConfig,
        IPlayerAdministration players,
        IServerRestartCoordinator restartCoordinator,
        IConsoleAdministration console,
        IServerConfigWriter configWriter,
        IServerConfigRawEditStaging rawStaging,
        IServerConfigReloader configReloader,
        IModDiscovery modDiscovery,
        IHostDiagnosticsGatherer hostDiagnostics,
        IServerDiagnosticsGatherer serverDiagnostics,
        IOptions<AgentOptions> options,
        ILogger<AgentCommandProcessor> logger)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(containerRuntime);
        ArgumentNullException.ThrowIfNull(hostDirectories);
        ArgumentNullException.ThrowIfNull(updates);
        ArgumentNullException.ThrowIfNull(backups);
        ArgumentNullException.ThrowIfNull(restores);
        ArgumentNullException.ThrowIfNull(rconProbe);
        ArgumentNullException.ThrowIfNull(rconConfig);
        ArgumentNullException.ThrowIfNull(players);
        ArgumentNullException.ThrowIfNull(restartCoordinator);
        ArgumentNullException.ThrowIfNull(console);
        ArgumentNullException.ThrowIfNull(configWriter);
        ArgumentNullException.ThrowIfNull(rawStaging);
        ArgumentNullException.ThrowIfNull(configReloader);
        ArgumentNullException.ThrowIfNull(modDiscovery);
        ArgumentNullException.ThrowIfNull(hostDiagnostics);
        ArgumentNullException.ThrowIfNull(serverDiagnostics);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _timeProvider = timeProvider;
        _containerRuntime = containerRuntime;
        _hostDirectories = hostDirectories;
        _updates = updates;
        _backups = backups;
        _restores = restores;
        _rconProbe = rconProbe;
        _rconConfig = rconConfig;
        _players = players;
        _restartCoordinator = restartCoordinator;
        _console = console;
        _configWriter = configWriter;
        _rawStaging = rawStaging;
        _configReloader = configReloader;
        _modDiscovery = modDiscovery;
        _hostDiagnostics = hostDiagnostics;
        _serverDiagnostics = serverDiagnostics;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Processes a dispatched command's canonical wire JSON and returns the terminal
    /// <see cref="OperationCompleted"/> envelope to send back, or <c>null</c> when there is nothing to send —
    /// a replay of an already-handled operation, a command carrying no <c>OperationId</c>, or a command this
    /// Agent version does not handle. A long-running command (F17's SteamCMD update) emits interim
    /// <c>OperationProgress</c> through <paramref name="progress"/>; omit it (or pass null) for the no-op reporter.
    /// </summary>
    public async Task<Envelope<OperationCompleted>?> ProcessAsync(
        string commandJson, CancellationToken cancellationToken, IOperationProgressReporter? progress = null)
    {
        ArgumentNullException.ThrowIfNull(commandJson);

        Envelope<IProtocolMessage> envelope = ProtocolJson.Deserialize(commandJson);
        if (envelope.OperationId is not { } operationId)
        {
            return null;
        }

        switch (envelope.Payload)
        {
            case PingAgent:
                if (!_handled.TryAdd(operationId, 0))
                {
                    return null; // Already handled this operation — a redelivered command (PRD 20).
                }

                return Completed(OperationOutcome.Succeeded, failureReason: null, operationId);

            case ProbeDockerHealth:
                if (!_handled.TryAdd(operationId, 0))
                {
                    return null; // Already handled this operation — a redelivered command (PRD 20).
                }

                DockerHealth health = await _containerRuntime.ProbeHealthAsync(cancellationToken).ConfigureAwait(false);
                return health.DaemonReachable
                    ? Completed(OperationOutcome.Succeeded, failureReason: null, operationId)
                    : Completed(OperationOutcome.Failed, health.Detail, operationId);

            case GatherHostDiagnostics:
                if (!_handled.TryAdd(operationId, 0))
                {
                    return null; // Already handled this operation — a redelivered command (PRD 20).
                }

                // Read-only host gather (F29): every domain is fail-soft inside the gatherer, so the Operation
                // always succeeds and carries a full bundle — a failing domain is a Fail check, not a failed gather.
                HostDiagnosticsResult hostBundle = await _hostDiagnostics.GatherAsync(cancellationToken).ConfigureAwait(false);
                return Completed(OperationOutcome.Succeeded, failureReason: null, operationId, hostDiagnostics: hostBundle);

            case GatherServerDiagnostics:
                if (envelope.ServerId is not { } gatherServerId)
                {
                    // A per-server gather with no target Server is malformed — fail it explicitly.
                    return Completed(OperationOutcome.Failed, "No target Server on the server diagnostics gather.", operationId);
                }

                if (!_handled.TryAdd(operationId, 0))
                {
                    return null; // Already handled this operation — a redelivered command (PRD 20).
                }

                ServerDiagnosticsResult serverBundle = await _serverDiagnostics
                    .GatherAsync(gatherServerId, cancellationToken).ConfigureAwait(false);
                return Completed(
                    OperationOutcome.Succeeded, failureReason: null, operationId, serverId: gatherServerId, serverDiagnostics: serverBundle);

            case ProbeRconHealth:
                if (envelope.ServerId is not { } rconServerId)
                {
                    // A per-server RCON probe with no target Server is malformed — fail it explicitly.
                    return Completed(OperationOutcome.Failed, "No target Server on the RCON health probe.", operationId);
                }

                if (!_handled.TryAdd(operationId, 0))
                {
                    return null; // Already handled this operation — a redelivered command (PRD 20).
                }

                RconHealthResult rcon = await _rconProbe.ProbeAsync(rconServerId, cancellationToken).ConfigureAwait(false);
                return Completed(
                    rcon.Authenticated ? OperationOutcome.Succeeded : OperationOutcome.Failed,
                    rcon.Authenticated ? null : rcon.Detail,
                    operationId,
                    rconServerId,
                    rcon: rcon);

            case CreateServer:
                if (envelope.ServerId is not { } serverId)
                {
                    // A provisioning command with no target Server is malformed — fail it explicitly.
                    return Completed(OperationOutcome.Failed, "No target Server on the provisioning command.", operationId);
                }

                if (!_handled.TryAdd(operationId, 0))
                {
                    return null; // Already handled this operation — a redelivered command (PRD 20).
                }

                return await ProvisionAsync(serverId, operationId, cancellationToken).ConfigureAwait(false);

            case StartServer:
                return await LifecycleAsync(
                    envelope, operationId, "start", _containerRuntime.StartAsync, cancellationToken).ConfigureAwait(false);

            case StopServer:
                return await LifecycleAsync(
                    envelope, operationId, "stop", _containerRuntime.StopAsync, cancellationToken).ConfigureAwait(false);

            case RestartServer restart:
                // Graceful restart (#114): the coordinator broadcasts a servermsg countdown to players (best-effort,
                // never blocking) and then runs the F15 safe restart, so the same LifecycleAsync fault handling wraps
                // the container half.
                return await LifecycleAsync(
                    envelope,
                    operationId,
                    "restart",
                    (server, ct) => _restartCoordinator.RestartAsync(
                        server, restart.Plan, operationId, progress ?? NullOperationProgressReporter.Instance, ct),
                    cancellationToken).ConfigureAwait(false);

            case UpdateServer:
                if (envelope.ServerId is not { } updateServerId)
                {
                    // An update command with no target Server is malformed — fail it explicitly.
                    return Completed(OperationOutcome.Failed, "No target Server on the update command.", operationId);
                }

                if (!_handled.TryAdd(operationId, 0))
                {
                    return null; // Already handling this update — a redelivered command (PRD 20).
                }

                ServerUpdateOutcome update = await _updates
                    .RunAsync(updateServerId, operationId, progress ?? NullOperationProgressReporter.Instance, cancellationToken)
                    .ConfigureAwait(false);
                return update.Succeeded
                    ? Completed(OperationOutcome.Succeeded, failureReason: null, operationId, updateServerId, update: new UpdateResult(update.InstalledBuildId))
                    : Completed(OperationOutcome.Failed, update.FailureReason, operationId, updateServerId);

            case BackupServer:
                if (envelope.ServerId is not { } backupServerId)
                {
                    // A backup command with no target Server is malformed — fail it explicitly.
                    return Completed(OperationOutcome.Failed, "No target Server on the backup command.", operationId);
                }

                if (!_handled.TryAdd(operationId, 0))
                {
                    return null; // Already handling this backup — a redelivered command (PRD 20).
                }

                ServerBackupOutcome backup = await _backups
                    .RunAsync(backupServerId, operationId, cancellationToken)
                    .ConfigureAwait(false);
                return backup.Succeeded
                    ? Completed(
                        OperationOutcome.Succeeded, failureReason: null, operationId, backupServerId,
                        backup: new BackupResult(backup.ArchiveName!, backup.SizeBytes, backup.Sha256!, backup.CreatedAt!.Value))
                    : Completed(OperationOutcome.Failed, backup.FailureReason, operationId, backupServerId);

            case DeleteBackup deleteBackup:
                if (envelope.ServerId is not { } deleteServerId)
                {
                    // A delete-backup command with no target Server is malformed — fail it explicitly.
                    return Completed(OperationOutcome.Failed, "No target Server on the delete-backup command.", operationId);
                }

                if (!_handled.TryAdd(operationId, 0))
                {
                    return null; // Already handling this deletion — a redelivered command (PRD 20).
                }

                ServerBackupDeletionOutcome deletion = await _backups
                    .DeleteAsync(deleteServerId, deleteBackup.ArchiveName, cancellationToken)
                    .ConfigureAwait(false);
                return deletion.Succeeded
                    ? Completed(
                        OperationOutcome.Succeeded, failureReason: null, operationId, deleteServerId,
                        backupDeletion: new BackupDeletionResult(deleteBackup.ArchiveName))
                    : Completed(OperationOutcome.Failed, deletion.FailureReason, operationId, deleteServerId);

            case RestoreServer restore:
                if (envelope.ServerId is not { } restoreServerId)
                {
                    // A restore command with no target Server is malformed — fail it explicitly.
                    return Completed(OperationOutcome.Failed, "No target Server on the restore command.", operationId);
                }

                if (!_handled.TryAdd(operationId, 0))
                {
                    return null; // Already handling this restore — a redelivered command (PRD 20).
                }

                ServerRestoreOutcome restored = await _restores
                    .RunAsync(
                        restoreServerId, operationId, restore.ArchiveName, restore.Sha256,
                        progress ?? NullOperationProgressReporter.Instance, cancellationToken)
                    .ConfigureAwait(false);
                return restored.Succeeded
                    ? Completed(
                        OperationOutcome.Succeeded, failureReason: null, operationId, restoreServerId,
                        restore: new RestoreResult(
                            restored.RestoredArchiveName!,
                            new BackupResult(
                                restored.ProtectiveArchiveName!,
                                restored.ProtectiveSizeBytes,
                                restored.ProtectiveSha256!,
                                restored.ProtectiveCreatedAt!.Value)))
                    : Completed(OperationOutcome.Failed, restored.FailureReason, operationId, restoreServerId);

            case ListPlayers:
                if (envelope.ServerId is not { } listServerId)
                {
                    return Completed(OperationOutcome.Failed, "No target Server on the player enumeration.", operationId);
                }

                if (!_handled.TryAdd(operationId, 0))
                {
                    return null; // Already handled this operation — a redelivered command (PRD 20).
                }

                try
                {
                    PlayerRosterResult roster = await _players.ListPlayersAsync(listServerId, cancellationToken).ConfigureAwait(false);
                    return Completed(OperationOutcome.Succeeded, failureReason: null, operationId, listServerId, roster: roster);
                }
                catch (PlayerCommandException ex)
                {
                    return Completed(OperationOutcome.Failed, ex.Message, operationId, listServerId);
                }

            case DiscoverMods:
                if (envelope.ServerId is not { } modsServerId)
                {
                    return Completed(OperationOutcome.Failed, "No target Server on the mod discovery.", operationId);
                }

                if (!_handled.TryAdd(operationId, 0))
                {
                    return null; // Already handled this operation — a redelivered command (PRD 20).
                }

                // Discovery is read-only and resilient: it returns empty/partial data for a missing tree or an
                // unparseable config rather than throwing, so a successful completion always carries the result.
                ModDiscoveryResult modResult = await _modDiscovery.DiscoverAsync(modsServerId, cancellationToken).ConfigureAwait(false);
                return Completed(OperationOutcome.Succeeded, failureReason: null, operationId, modsServerId, mods: modResult);

            case KickPlayer kick:
                return await PlayerActionAsync(
                    envelope, operationId, "kick",
                    (server, ct) => _players.KickAsync(server, kick.Username, kick.Reason, ct), cancellationToken).ConfigureAwait(false);

            case BanPlayer ban:
                return await PlayerActionAsync(
                    envelope, operationId, "ban",
                    (server, ct) => _players.BanAsync(server, ban.Username, ban.Reason, ct), cancellationToken).ConfigureAwait(false);

            case UnbanPlayer unban:
                return await PlayerActionAsync(
                    envelope, operationId, "unban",
                    (server, ct) => _players.UnbanAsync(server, unban.Username, ct), cancellationToken).ConfigureAwait(false);

            case RemoveFromWhitelist remove:
                return await PlayerActionAsync(
                    envelope, operationId, "remove-from-whitelist",
                    (server, ct) => _players.RemoveFromWhitelistAsync(server, remove.Username, ct), cancellationToken).ConfigureAwait(false);

            case SetWhitelistMode mode:
                return await PlayerActionAsync(
                    envelope, operationId, "set-whitelist-mode",
                    (server, ct) => _players.SetWhitelistModeAsync(server, mode.Open, ct), cancellationToken).ConfigureAwait(false);

            case ExecuteConsoleCommand console:
                if (envelope.ServerId is not { } consoleServerId)
                {
                    return Completed(OperationOutcome.Failed, "No target Server on the console command.", operationId);
                }

                if (!_handled.TryAdd(operationId, 0))
                {
                    return null; // Already handled this operation — a redelivered command (PRD 20).
                }

                try
                {
                    // The command ran: a succeeded Operation carries the (untrusted, bounded) reply. Whether PZ's
                    // reply reports the desired effect is for the operator to read — the console is opaque by design.
                    ConsoleCommandResult result = await _console
                        .ExecuteAsync(consoleServerId, console.Input, cancellationToken).ConfigureAwait(false);
                    return Completed(
                        OperationOutcome.Succeeded, failureReason: null, operationId, consoleServerId, console: result);
                }
                catch (ConsoleCommandException ex)
                {
                    return Completed(OperationOutcome.Failed, ex.Message, operationId, consoleServerId);
                }

            case ConfigApply apply:
                if (envelope.ServerId is not { } configServerId)
                {
                    // A config apply with no target Server is malformed — fail it explicitly.
                    return Completed(OperationOutcome.Failed, "No target Server on the configuration apply command.", operationId);
                }

                if (!_handled.TryAdd(operationId, 0))
                {
                    return null; // Already handled this operation — a redelivered command (PRD 20).
                }

                ConfigApplyOutcome config = await _configWriter
                    .ApplyAsync(configServerId, apply.File, apply.BaselineHash, apply.Edits, cancellationToken)
                    .ConfigureAwait(false);
                // A drift refusal (ADR 0011) and any other failure are both a failed Operation whose non-secret
                // reason the operator reads; only a write that applied carries the recorded revision.
                LogConfigWriteOutcome(configServerId, operationId, apply.File, "surgical", config.Succeeded, config.ChangedCount, config.FailureReason);
                return config.Succeeded
                    ? await ConfigAppliedAsync(configServerId, operationId, apply.File, config, cancellationToken).ConfigureAwait(false)
                    : Completed(OperationOutcome.Failed, config.FailureReason, operationId, configServerId);

            case ConfigApplyRaw raw:
                if (envelope.ServerId is not { } rawServerId)
                {
                    // A raw config apply with no target Server is malformed — fail it explicitly.
                    return Completed(OperationOutcome.Failed, "No target Server on the raw configuration apply command.", operationId);
                }

                if (!_handled.TryAdd(operationId, 0))
                {
                    return null; // Already handled this operation — a redelivered command (PRD 20).
                }

                // The operator's whole-file text was staged over the transport channel before this Operation was
                // dispatched (ADR 0042). A missing or expired stage — a lost chunk, or an Agent restart between the
                // stage and the Operation — is a clean failure, never a hang.
                if (!_rawStaging.TryTake(raw.CorrelationId, out string? rawContent))
                {
                    return Completed(
                        OperationOutcome.Failed,
                        "The edited configuration text was not received (or expired) before the apply ran; re-submit the edit.",
                        operationId,
                        rawServerId);
                }

                ConfigApplyOutcome rawOutcome = await _configWriter
                    .ApplyRawAsync(rawServerId, raw.File, raw.BaselineHash, rawContent, cancellationToken)
                    .ConfigureAwait(false);
                LogConfigWriteOutcome(rawServerId, operationId, raw.File, "raw", rawOutcome.Succeeded, rawOutcome.ChangedCount, rawOutcome.FailureReason);
                return rawOutcome.Succeeded
                    ? await ConfigAppliedAsync(rawServerId, operationId, raw.File, rawOutcome, cancellationToken).ConfigureAwait(false)
                    : Completed(OperationOutcome.Failed, rawOutcome.FailureReason, operationId, rawServerId);

            default:
                // A command this Agent version does not understand: leave it unhandled (not marked handled) so
                // a future version can process a redelivery. The operation's lease reaps it if never handled.
                return null;
        }
    }

    /// <summary>
    /// Provisions the canonical container for <paramref name="serverId"/> (F14): allocate the port stride from
    /// the host's live bindings (F13), build the closed create-template from Agent configuration, create and
    /// start the container, and report the allocated ports and container id. A create failure (e.g. the image
    /// is not pre-provisioned) becomes a failed completion carrying the actionable, Agent-authored reason.
    /// </summary>
    private async Task<Envelope<OperationCompleted>> ProvisionAsync(
        ServerId serverId,
        OperationId operationId,
        CancellationToken cancellationToken)
    {
        try
        {
            PortAllocation ports = await _containerRuntime.AllocateNextPortsAsync(cancellationToken).ConfigureAwait(false);
            PzContainerSpec spec = new(
                serverId,
                ContainerName: serverId.ToString(),
                ImageReference: _options.PzImageReference ?? string.Empty,
                NetworkName: _options.NetworkName,
                DataMountSource: Path.Combine(_options.DataMountRoot, serverId.ToString()),
                // The SteamCMD install lives in a host sibling of the data dir (F17): persistent, but outside the
                // world-data path the disk meter reads (F16), so the ~6.72 GiB install is not counted as world use.
                ServerMountSource: Path.Combine(_options.DataMountRoot, $"{serverId}.server"),
                Ports: ports,
                MemoryLimitBytes: _options.DefaultMemoryLimitBytes,
                HeapSizeBytes: _options.DefaultHeapSizeBytes);

            // Materialise BOTH host-side bind-mount sources before the create (#184): the Docker Mounts API
            // never auto-creates them, and the Agent's own container filesystem is not where the daemon resolves
            // them, so without this the daemon refuses with "bind source path does not exist".
            _hostDirectories.EnsureCreated(spec);

            // Seed RCON into the Server's config on the (Agent-owned) data mount before the container first
            // launches, so PZ enables RCON with an Agent-generated password on first boot (F18 D-2). Idempotent:
            // a re-provision keeps the existing password. Host-side surgical write, no container env var, no exec.
            _rconConfig.EnsureEnabled(serverId);

            string containerId = await _containerRuntime.CreateAsync(spec, cancellationToken).ConfigureAwait(false);
            await _containerRuntime.StartAsync(containerId, cancellationToken).ConfigureAwait(false);

            return Completed(
                OperationOutcome.Succeeded,
                failureReason: null,
                operationId,
                serverId,
                new ProvisionResult(ports.GamePort, ports.DirectPort, containerId));
        }
        catch (ContainerCreateException ex)
        {
            // Actionable, Agent-authored reason (e.g. the pinned image is not pre-provisioned, ADR 0008 D5).
            return Completed(OperationOutcome.Failed, ex.Message, operationId, serverId);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The RCON config seed (or a host-path write) failed — report it rather than crash the operation.
            return Completed(
                OperationOutcome.Failed,
                $"Could not prepare the server's host data before launch: {ex.Message}",
                operationId,
                serverId);
        }
    }

    /// <summary>
    /// Runs a lifecycle verb (start/stop/restart, F15) against the target Server on the envelope. The Agent
    /// resolves the container it owns for the Server and issues the guarded Docker verb; a Server with no owned
    /// container, a foreign container, or a Docker refusal becomes a failed completion with an actionable,
    /// Agent-authored reason (escaped downstream) — never a crash. Deduped by <c>OperationId</c> (PRD 20).
    /// </summary>
    private async Task<Envelope<OperationCompleted>?> LifecycleAsync(
        Envelope<IProtocolMessage> envelope,
        OperationId operationId,
        string verb,
        Func<ServerId, CancellationToken, Task> action,
        CancellationToken cancellationToken)
    {
        if (envelope.ServerId is not { } serverId)
        {
            // A lifecycle command with no target Server is malformed — fail it explicitly.
            return Completed(OperationOutcome.Failed, $"No target Server on the {verb} command.", operationId);
        }

        if (!_handled.TryAdd(operationId, 0))
        {
            return null; // Already handled this operation — a redelivered command (PRD 20).
        }

        try
        {
            await action(serverId, cancellationToken).ConfigureAwait(false);
            return Completed(OperationOutcome.Succeeded, failureReason: null, operationId, serverId);
        }
        catch (ContainerNotFoundException)
        {
            return Completed(
                OperationOutcome.Failed,
                $"This server has no container on its host to {verb}. Provision (register) the server first.",
                operationId,
                serverId);
        }
        catch (ForeignContainerException ex)
        {
            return Completed(OperationOutcome.Failed, ex.Reason, operationId, serverId);
        }
        catch (DockerApiException ex)
        {
            // A denied verb (socket proxy, ADR 0008) or any daemon-side error — report, do not crash.
            return Completed(
                OperationOutcome.Failed,
                $"The Docker daemon refused to {verb} the container (HTTP {(int)ex.StatusCode}).",
                operationId,
                serverId);
        }
    }

    /// <summary>
    /// Runs a player-management action (F19: kick/ban/unban/remove-from-whitelist/set-whitelist-mode) against the
    /// target Server on the envelope, mirroring <see cref="LifecycleAsync"/>. Deduped by <c>OperationId</c> (PRD
    /// 20). A <see cref="PlayerCommandException"/> (invalid arguments, RCON disabled/unreachable, auth or timeout)
    /// becomes a failed completion with the Agent-authored reason. Otherwise the parsed
    /// <see cref="PlayerActionResult"/> is carried on the completion: an <see cref="PlayerActionOutcome.Applied"/>
    /// outcome is a succeeded Operation; a <see cref="PlayerActionOutcome.NotFound"/>,
    /// <see cref="PlayerActionOutcome.Rejected"/> or <see cref="PlayerActionOutcome.Unknown"/> outcome is a failed
    /// Operation whose (untrusted) reason is PZ's own reply — the command executed, but the target was not
    /// actioned. The result rides the completion either way so the control plane can reconcile (e.g. the ban
    /// registry, ADR 0027, records a ban only on <see cref="PlayerActionOutcome.Applied"/>).
    /// </summary>
    private async Task<Envelope<OperationCompleted>?> PlayerActionAsync(
        Envelope<IProtocolMessage> envelope,
        OperationId operationId,
        string verb,
        Func<ServerId, CancellationToken, Task<PlayerActionResult>> action,
        CancellationToken cancellationToken)
    {
        if (envelope.ServerId is not { } serverId)
        {
            return Completed(OperationOutcome.Failed, $"No target Server on the {verb} command.", operationId);
        }

        if (!_handled.TryAdd(operationId, 0))
        {
            return null; // Already handled this operation — a redelivered command (PRD 20).
        }

        try
        {
            PlayerActionResult result = await action(serverId, cancellationToken).ConfigureAwait(false);
            bool applied = result.Outcome == PlayerActionOutcome.Applied;
            return Completed(
                applied ? OperationOutcome.Succeeded : OperationOutcome.Failed,
                applied ? null : result.Detail ?? $"The server did not apply the {verb}.",
                operationId,
                serverId,
                playerAction: result);
        }
        catch (PlayerCommandException ex)
        {
            return Completed(OperationOutcome.Failed, ex.Message, operationId, serverId);
        }
    }

    /// <summary>
    /// Completes a successful configuration write (surgical or raw). An INI write that changed something is then made
    /// live with RCON <c>reloadoptions</c> (#225) — best-effort: the write stands whatever the reload does, and the
    /// outcome rides the result so the operator knows whether the change is live or waits for a restart. The revision
    /// is the writer's post-write snapshot, not a re-read after PZ rewrites the INI on reload; the two hold the same
    /// values, and revisions are value-hashed (ADR 0011).
    /// </summary>
    private async Task<Envelope<OperationCompleted>> ConfigAppliedAsync(
        ServerId serverId,
        OperationId operationId,
        PzConfigFile file,
        ConfigApplyOutcome outcome,
        CancellationToken cancellationToken)
    {
        ConfigReloadAttempt reload = new(ConfigReloadOutcome.NotAttempted);
        if (file == PzConfigFile.Ini && outcome.ChangedCount > 0)
        {
            try
            {
                reload = await _configReloader.ReloadAsync(serverId, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // The file is already written: still report the write (so its revision is recorded), not a hang.
                reload = new ConfigReloadAttempt(ConfigReloadOutcome.Failed, "The live reload was interrupted.");
            }
        }

        if (reload.Outcome != ConfigReloadOutcome.NotAttempted)
        {
            LogConfigReload(serverId, operationId, reload.Outcome, reload.Detail ?? string.Empty);
        }

        return Completed(
            OperationOutcome.Succeeded, failureReason: null, operationId, serverId,
            config: new ConfigApplyResult(
                file, outcome.SnapshotHash!, outcome.CanonicalSnapshot!, outcome.ChangedCount, reload.Outcome, reload.Detail));
    }

    // One structured line per configuration write outcome (#226), so a failed apply is diagnosable from the Agent's
    // own log, not only from the Operation's failure reason. The reason is the writer's non-secret text.
    private void LogConfigWriteOutcome(
        ServerId serverId, OperationId operationId, PzConfigFile file, string mode, bool succeeded, int changed, string? reason)
    {
        if (succeeded)
        {
            LogConfigWritten(serverId, operationId, file, mode, changed);
        }
        else
        {
            LogConfigWriteFailed(serverId, operationId, file, mode, reason ?? "unknown");
        }
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Configuration write ({Mode}) applied to {File} for server {ServerId} (operation {OperationId}): {ChangedCount} value(s) changed")]
    private partial void LogConfigWritten(ServerId serverId, OperationId operationId, PzConfigFile file, string mode, int changedCount);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Configuration write ({Mode}) to {File} for server {ServerId} (operation {OperationId}) was not applied: {Reason}")]
    private partial void LogConfigWriteFailed(ServerId serverId, OperationId operationId, PzConfigFile file, string mode, string reason);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Live config reload for server {ServerId} (operation {OperationId}): {Outcome} {Detail}")]
    private partial void LogConfigReload(ServerId serverId, OperationId operationId, ConfigReloadOutcome outcome, string detail);

    private Envelope<OperationCompleted> Completed(
        OperationOutcome outcome,
        string? failureReason,
        OperationId operationId,
        ServerId? serverId = null,
        ProvisionResult? provision = null,
        UpdateResult? update = null,
        RconHealthResult? rcon = null,
        PlayerRosterResult? roster = null,
        PlayerActionResult? playerAction = null,
        ConfigApplyResult? config = null,
        ModDiscoveryResult? mods = null,
        BackupResult? backup = null,
        BackupDeletionResult? backupDeletion = null,
        RestoreResult? restore = null,
        ConsoleCommandResult? console = null,
        HostDiagnosticsResult? hostDiagnostics = null,
        ServerDiagnosticsResult? serverDiagnostics = null) =>
        Envelope.Create(
            new OperationCompleted(
                outcome, failureReason, provision, update, rcon, roster, playerAction, config, mods, backup, backupDeletion, restore, console,
                hostDiagnostics, serverDiagnostics),
            _timeProvider.GetUtcNow(),
            serverId: serverId,
            operationId: operationId);
}
