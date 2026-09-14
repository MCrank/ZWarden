using System.Collections.Concurrent;
using Docker.DotNet;
using Microsoft.Extensions.Options;
using ZWarden.Agent.Backups;
using ZWarden.Agent.Configuration;
using ZWarden.Agent.Docker;
using ZWarden.Agent.Mods;
using ZWarden.Agent.Players;
using ZWarden.Agent.Rcon;
using ZWarden.Agent.ServerConfig;
using ZWarden.Agent.SteamCmd;
using ZWarden.Contracts.Protocol;
using ZWarden.Contracts.Protocol.Messages;
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
public sealed class AgentCommandProcessor
{
    private readonly TimeProvider _timeProvider;
    private readonly IContainerRuntime _containerRuntime;
    private readonly IServerUpdateRunner _updates;
    private readonly IServerBackupRunner _backups;
    private readonly IServerRestoreRunner _restores;
    private readonly IRconHealthProbe _rconProbe;
    private readonly IRconServerConfig _rconConfig;
    private readonly IPlayerAdministration _players;
    private readonly IServerConfigWriter _configWriter;
    private readonly IModDiscovery _modDiscovery;
    private readonly AgentOptions _options;
    private readonly ConcurrentDictionary<OperationId, byte> _handled = new();

    public AgentCommandProcessor(
        TimeProvider timeProvider,
        IContainerRuntime containerRuntime,
        IServerUpdateRunner updates,
        IServerBackupRunner backups,
        IServerRestoreRunner restores,
        IRconHealthProbe rconProbe,
        IRconServerConfig rconConfig,
        IPlayerAdministration players,
        IServerConfigWriter configWriter,
        IModDiscovery modDiscovery,
        IOptions<AgentOptions> options)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(containerRuntime);
        ArgumentNullException.ThrowIfNull(updates);
        ArgumentNullException.ThrowIfNull(backups);
        ArgumentNullException.ThrowIfNull(restores);
        ArgumentNullException.ThrowIfNull(rconProbe);
        ArgumentNullException.ThrowIfNull(rconConfig);
        ArgumentNullException.ThrowIfNull(players);
        ArgumentNullException.ThrowIfNull(configWriter);
        ArgumentNullException.ThrowIfNull(modDiscovery);
        ArgumentNullException.ThrowIfNull(options);
        _timeProvider = timeProvider;
        _containerRuntime = containerRuntime;
        _updates = updates;
        _backups = backups;
        _restores = restores;
        _rconProbe = rconProbe;
        _rconConfig = rconConfig;
        _players = players;
        _configWriter = configWriter;
        _modDiscovery = modDiscovery;
        _options = options.Value;
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

            case RestartServer:
                return await LifecycleAsync(
                    envelope, operationId, "restart", _containerRuntime.RestartAsync, cancellationToken).ConfigureAwait(false);

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
                return config.Succeeded
                    ? Completed(
                        OperationOutcome.Succeeded, failureReason: null, operationId, configServerId,
                        config: new ConfigApplyResult(apply.File, config.SnapshotHash!, config.CanonicalSnapshot!, config.ChangedCount))
                    : Completed(OperationOutcome.Failed, config.FailureReason, operationId, configServerId);

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
                MemoryLimitBytes: _options.DefaultMemoryLimitBytes);

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
        RestoreResult? restore = null) =>
        Envelope.Create(
            new OperationCompleted(
                outcome, failureReason, provision, update, rcon, roster, playerAction, config, mods, backup, backupDeletion, restore),
            _timeProvider.GetUtcNow(),
            serverId: serverId,
            operationId: operationId);
}
