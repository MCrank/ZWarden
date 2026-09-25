using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using ZWarden.Application.Agents;
using ZWarden.Application.Audit;
using ZWarden.Application.Backups;
using ZWarden.Application.Configuration;
using ZWarden.Application.Console;
using ZWarden.Application.Operations;
using ZWarden.Application.Players;
using ZWarden.Application.Servers;
using ZWarden.Contracts.Protocol;
using ZWarden.Contracts.Protocol.Messages;
using ZWarden.Domain.Audit;
using ZWarden.Domain.Operations;
using ZWarden.Domain.Security;
using ZWarden.Infrastructure.Operations;
using ZWarden.Infrastructure.Persistence;
using ZWarden.Web.Agents;

namespace ZWarden.Web.Operations;

/// <summary>
/// The real <see cref="IOperationDispatcher"/> (F11): it sends an operation's command down the F10 SignalR
/// connection. It resolves the Agent's live connection from the in-memory <see cref="IAgentConnectionRegistry"/>
/// (the seam F10 built for exactly this), and — to avoid the "completed before Running was persisted" race —
/// transitions the operation to <see cref="OperationState.Running"/> under a lease and <b>persists that
/// before the command leaves</b>, then sends. With no live connection it leaves the operation
/// <see cref="OperationState.Pending"/> and returns <c>false</c>. The command travels as the canonical wire
/// string of an <c>Envelope&lt;AgentCommand&gt;</c> (<see cref="AgentHubProtocol.ReceiveCommand"/>), so one
/// channel carries the whole closed vocabulary.
/// </summary>
public sealed class OperationDispatcher : IOperationDispatcher
{
    private readonly IAgentConnectionRegistry _registry;
    private readonly IHubContext<AgentHub> _hub;
    private readonly ZWardenDbContext _context;
    private readonly IAuditWriter _audit;
    private readonly TimeProvider _clock;
    private readonly OperationEngineOptions _options;
    private readonly ISecretProtector _secrets;

    public OperationDispatcher(
        IAgentConnectionRegistry registry,
        IHubContext<AgentHub> hub,
        ZWardenDbContext context,
        IAuditWriter audit,
        TimeProvider clock,
        IOptions<OperationEngineOptions> options,
        ISecretProtector secrets)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(hub);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(audit);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(secrets);
        _registry = registry;
        _hub = hub;
        _context = context;
        _audit = audit;
        _clock = clock;
        _options = options.Value;
        _secrets = secrets;
    }

    /// <inheritdoc />
    public async Task<bool> TryDispatchAsync(Operation operation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        string? connectionId = _registry.GetConnectionId(operation.AgentId);
        if (connectionId is null)
        {
            // The Agent is offline; leave the operation Pending for a later dispatch (F11 has no reconnect
            // pump yet — the enqueue is the trigger).
            return false;
        }

        DateTimeOffset now = _clock.GetUtcNow();
        operation.MarkDispatched(now + _options.LeaseDuration, now);
        // Persist Running before the command leaves, so an Agent that replies instantly never races a
        // not-yet-persisted transition.
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await _audit.WriteAsync(
            new AuditEntry(OperationAuditActions.Started, AuditOutcome.Succeeded, ServerId: operation.ServerId,
                Detail: $"{operation.Kind} {operation.Id}"),
            cancellationToken).ConfigureAwait(false);

        AgentCommand command = CommandFor(operation.Kind, operation.CommandPayload, _secrets.UnprotectString);
        Envelope<AgentCommand> envelope = Envelope.Create<AgentCommand>(
            command, now, agentId: operation.AgentId, serverId: operation.ServerId, operationId: operation.Id);

        await _hub.Clients.Client(connectionId)
            .SendAsync(AgentHubProtocol.ReceiveCommand, ProtocolJson.Serialize(envelope), cancellationToken)
            .ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// Maps an <see cref="OperationKind"/> (and, for kinds whose command carries parameters, the Operation's
    /// <paramref name="commandPayload"/>) to the <see cref="AgentCommand"/> that carries it down the connection.
    /// The whole vocabulary is here in one place; a new kind without a mapping throws rather than dispatching a
    /// wrong command. The F19 player commands read their target username/reason/flag from the payload the
    /// enqueueing service wrote (<see cref="PlayerCommandPayload"/>). Pure and static so the map is unit-testable
    /// without the hub/registry/persistence dependencies.
    /// </summary>
    public static AgentCommand CommandFor(
        OperationKind kind, string? commandPayload = null, Func<string, string>? unprotect = null) => kind switch
    {
        OperationKind.DiagnosticsPing => new PingAgent(),
        OperationKind.DiagnosticsDockerHealth => new ProbeDockerHealth(),
        OperationKind.ProvisionServer => CreateCommand(commandPayload, unprotect),
        OperationKind.RecreateServer => RecreateCommand(commandPayload),
        OperationKind.StartServer => new StartServer(),
        OperationKind.StopServer => new StopServer(),
        OperationKind.RestartServer => RestartCommand(commandPayload),
        OperationKind.UpdateServer => new UpdateServer(),
        OperationKind.RconHealthProbe => new ProbeRconHealth(),
        OperationKind.GatherHostDiagnostics => new GatherHostDiagnostics(),
        OperationKind.GatherServerDiagnostics => new GatherServerDiagnostics(),
        OperationKind.ListPlayers => new ListPlayers(),
        OperationKind.ModDiscovery => new DiscoverMods(),
        OperationKind.KickPlayer => new KickPlayer(Payload(commandPayload).Username!, Payload(commandPayload).Reason),
        OperationKind.BanPlayer => new BanPlayer(Payload(commandPayload).Username!, Payload(commandPayload).Reason),
        OperationKind.UnbanPlayer => new UnbanPlayer(Payload(commandPayload).Username!),
        OperationKind.RemoveFromWhitelist => new RemoveFromWhitelist(Payload(commandPayload).Username!),
        OperationKind.SetWhitelistMode => new SetWhitelistMode(Payload(commandPayload).Open ?? false),
        OperationKind.ConfigApply => ConfigApplyCommand(commandPayload),
        OperationKind.ConfigApplyRaw => ConfigApplyRawCommand(commandPayload),
        OperationKind.ExecuteConsoleCommand => new ExecuteConsoleCommand(ConsolePayload(commandPayload).Input),
        OperationKind.Backup => new BackupServer(),
        OperationKind.DeleteBackup => new DeleteBackup(BackupPayload(commandPayload).ArchiveName!),
        OperationKind.Restore => RestoreCommand(commandPayload),
        _ => throw new NotSupportedException($"No command mapping for operation kind '{kind}'."),
    };

    // The optional container payload (#229): absent on a provision that lets the Agent allocate the next stride.
    private static ServerContainerPayload? ContainerPayload(string? commandPayload) =>
        commandPayload is null ? null : ServerContainerPayload.FromJson(commandPayload);

    // Builds the RecreateServer wire command (#229): the optional new game port and the optional graceful plan (null ⇒
    // the Agent's default warning schedule; an empty schedule skips the warning).
    private static RecreateServer RecreateCommand(string? commandPayload)
    {
        ServerContainerPayload? payload = ContainerPayload(commandPayload);
        return new RecreateServer(
            payload?.GamePort,
            payload?.Plan is { } plan ? new GracefulRestartPlan(plan.WarningLeadSeconds, plan.Reason) : null,
            payload?.HeapSizeBytes);
    }

    // Builds the CreateServer wire command (#229, #230): port, heap and the wizard's initial settings. The stored join
    // password is a protected envelope, decrypted here only for the moment the command is built — dispatching one
    // without the protector is a wiring bug, so it throws rather than sending the envelope as the password.
    private static CreateServer CreateCommand(string? commandPayload, Func<string, string>? unprotect)
    {
        ServerContainerPayload? payload = ContainerPayload(commandPayload);
        InitialServerSettings? settings = payload?.Settings is { } s
            ? new InitialServerSettings(
                s.Public,
                s.PublicName,
                s.MaxPlayers,
                s.ProtectedPassword is { } envelope
                    ? (unprotect ?? throw new InvalidOperationException(
                        "A provisioning Operation with a protected password was dispatched without the secret protector."))(envelope)
                    : null,
                s.WelcomeMessage)
            : null;
        return new CreateServer(payload?.GamePort, payload?.HeapSizeBytes, settings);
    }

    private static BackupCommandPayload BackupPayload(string? commandPayload) => BackupCommandPayload.FromJson(
        commandPayload ?? throw new InvalidOperationException("A backup-deletion Operation was dispatched with no command payload."));

    // Builds the RestoreServer wire command from the Application-neutral payload the enqueueing service wrote: the
    // Agent needs the archive name (to locate it under BackupRoot) and the checksum (to re-verify before unpacking).
    private static RestoreServer RestoreCommand(string? commandPayload)
    {
        RestoreCommandPayload payload = RestoreCommandPayload.FromJson(
            commandPayload ?? throw new InvalidOperationException("A restore Operation was dispatched with no command payload."));
        return new RestoreServer(payload.ArchiveName, payload.Sha256);
    }

    // Builds the RestartServer wire command from the optional graceful-restart payload the enqueueing service wrote
    // (#114). No payload ⇒ the Agent applies its default warning schedule; a payload carries the operator's chosen
    // countdown (empty = restart immediately, no warning) and message.
    private static RestartServer RestartCommand(string? commandPayload)
    {
        if (commandPayload is null)
        {
            return new RestartServer();
        }

        GracefulRestartPayload payload = GracefulRestartPayload.FromJson(commandPayload);
        return new RestartServer(new GracefulRestartPlan(payload.WarningLeadSeconds, payload.Reason));
    }

    private static PlayerCommandPayload Payload(string? commandPayload) => PlayerCommandPayload.FromJson(
        commandPayload ?? throw new InvalidOperationException("A player Operation was dispatched with no command payload."));

    private static ConsoleCommandPayload ConsolePayload(string? commandPayload) => ConsoleCommandPayload.FromJson(
        commandPayload ?? throw new InvalidOperationException("A console Operation was dispatched with no command payload."));

    // Builds the ConfigApply wire command from the Application-neutral payload the enqueueing service wrote,
    // mapping the neutral edit kinds onto their wire twins (F20b PR3). The file and baseline cross unchanged.
    private static ConfigApply ConfigApplyCommand(string? commandPayload)
    {
        ConfigApplyPayload payload = ConfigApplyPayload.FromJson(
            commandPayload ?? throw new InvalidOperationException("A config Operation was dispatched with no command payload."));
        IReadOnlyList<ConfigValueEdit> edits =
            [.. payload.Edits.Select(e => new ConfigValueEdit(e.Path, ToWireKind(e.Kind), e.Value))];
        return new ConfigApply(payload.File, payload.BaselineHash, edits);
    }

    // Builds the ConfigApplyRaw wire command from the Application-neutral payload the enqueueing service wrote
    // (F20c PR-D, ADR 0042). The operator's whole-file text is not here — it was staged over its own channel — so
    // the command carries only the file, drift baseline, and staging correlation id.
    private static ConfigApplyRaw ConfigApplyRawCommand(string? commandPayload)
    {
        ConfigApplyRawPayload payload = ConfigApplyRawPayload.FromJson(
            commandPayload ?? throw new InvalidOperationException("A raw config Operation was dispatched with no command payload."));
        return new ConfigApplyRaw(payload.File, payload.BaselineHash, payload.CorrelationId);
    }

    private static ConfigValueKind ToWireKind(ConfigEditKind kind) => kind switch
    {
        ConfigEditKind.Bool => ConfigValueKind.Bool,
        ConfigEditKind.Number => ConfigValueKind.Number,
        ConfigEditKind.Text => ConfigValueKind.Text,
        _ => throw new NotSupportedException($"No wire mapping for config edit kind '{kind}'."),
    };
}
