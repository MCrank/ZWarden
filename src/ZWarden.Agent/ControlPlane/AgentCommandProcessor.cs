using System.Collections.Concurrent;
using Docker.DotNet;
using Microsoft.Extensions.Options;
using ZWarden.Agent.Configuration;
using ZWarden.Agent.Docker;
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
    private readonly AgentOptions _options;
    private readonly ConcurrentDictionary<OperationId, byte> _handled = new();

    public AgentCommandProcessor(
        TimeProvider timeProvider,
        IContainerRuntime containerRuntime,
        IServerUpdateRunner updates,
        IOptions<AgentOptions> options)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(containerRuntime);
        ArgumentNullException.ThrowIfNull(updates);
        ArgumentNullException.ThrowIfNull(options);
        _timeProvider = timeProvider;
        _containerRuntime = containerRuntime;
        _updates = updates;
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

    private Envelope<OperationCompleted> Completed(
        OperationOutcome outcome,
        string? failureReason,
        OperationId operationId,
        ServerId? serverId = null,
        ProvisionResult? provision = null,
        UpdateResult? update = null) =>
        Envelope.Create(
            new OperationCompleted(outcome, failureReason, provision, update),
            _timeProvider.GetUtcNow(),
            serverId: serverId,
            operationId: operationId);
}
