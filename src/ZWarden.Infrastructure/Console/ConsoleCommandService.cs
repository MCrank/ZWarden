using ZWarden.Application.Audit;
using ZWarden.Application.Authorization;
using ZWarden.Application.Console;
using ZWarden.Application.Operations;
using ZWarden.Domain.Audit;
using ZWarden.Domain.Authorization;
using ZWarden.Domain.Console;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Operations;
using ZWarden.Domain.Servers;
using ZWarden.Infrastructure.Servers;

namespace ZWarden.Infrastructure.Console;

/// <summary>
/// The tenant-scoped remote-console service (F28): run one operator-authored RCON command line as a durable,
/// authorized, audited, <b>non-mutating</b> Operation — a sibling of <see cref="ServerDiagnostics"/> and
/// <c>PlayerManagement</c>. Fail-closed (ADR 0018): it resolves the Server through the tenant filter (a
/// foreign/unknown Server is <see cref="ConsoleCommandFailure.ServerNotFound"/>), authorizes the <b>elevated</b>
/// server-scoped <c>Console.Execute</c> permission against that Server, and runs the F28 input-safety and command
/// policy (<see cref="ConsoleCommandRules"/>, ADR 0032) before enqueuing. A policy denial is audited
/// (<see cref="ConsoleAuditActions.CommandDenied"/>) and never sent; a successful enqueue is audited
/// (<see cref="ConsoleAuditActions.CommandExecuted"/>). Being non-mutating, it never claims the per-server lock
/// (ADR 0022), so it never contends with an in-flight lifecycle Operation and never surfaces a busy state. The
/// command line rides the Operation's command payload for the Agent to send (ADR 0026).
/// </summary>
public sealed class ConsoleCommandService : IConsoleCommandService
{
    private readonly ServerRepository _servers;
    private readonly IPermissionChecker _permissions;
    private readonly IOperationCoordinator _operations;
    private readonly IAuditWriter _audit;

    public ConsoleCommandService(
        ServerRepository servers,
        IPermissionChecker permissions,
        IOperationCoordinator operations,
        IAuditWriter audit)
    {
        ArgumentNullException.ThrowIfNull(servers);
        ArgumentNullException.ThrowIfNull(permissions);
        ArgumentNullException.ThrowIfNull(operations);
        ArgumentNullException.ThrowIfNull(audit);
        _servers = servers;
        _permissions = permissions;
        _operations = operations;
        _audit = audit;
    }

    /// <inheritdoc />
    public async Task<ConsoleExecutionResult> ExecuteAsync(
        UserId user, ServerId server, string input, CancellationToken cancellationToken = default)
    {
        // Resolve first, through the tenant filter: an unknown or foreign-tenant Server is ServerNotFound, and
        // gives the server-scoped authorization a concrete resource to check.
        Server? resolved = await _servers.FindByIdAsync(server, cancellationToken).ConfigureAwait(false);
        if (resolved is null)
        {
            return ConsoleExecutionResult.Denied(ConsoleCommandFailure.ServerNotFound);
        }

        AuthorizationDecision decision = await _permissions
            .EvaluateAsync(user, Permissions.ConsoleExecute, server: server, cancellationToken).ConfigureAwait(false);
        if (!decision.IsAllowed)
        {
            return ConsoleExecutionResult.Denied(ConsoleCommandFailure.NotAuthorized);
        }

        // Input safety, then the command policy — the same rules the Agent re-checks before it sends the line
        // (F28 D-1 / ADR 0032). A malformed line is a plain rejection; a policy denial is a security-relevant
        // signal, so it is audited and reported distinctly.
        if (ConsoleCommandRules.ValidateInput(input) is { } invalid)
        {
            return ConsoleExecutionResult.Denied(ConsoleCommandFailure.InvalidInput, invalid);
        }

        if (ConsoleCommandRules.EvaluatePolicy(input) is { } denied)
        {
            await _audit.WriteAsync(
                new AuditEntry(ConsoleAuditActions.CommandDenied, AuditOutcome.Denied, user, server, DetailFor(input, denied)),
                cancellationToken).ConfigureAwait(false);
            return ConsoleExecutionResult.Denied(ConsoleCommandFailure.Denied, denied);
        }

        // A non-mutating, server-scoped Operation (ADR 0022): it names the Server (RCON is per-server) but does
        // not take the per-server lock, so it never contends and never throws ServerBusyException. The command
        // line rides the command payload for the Agent to send.
        Operation operation = await _operations.EnqueueAsync(
            new EnqueueOperationRequest(
                resolved.AgentId, OperationKind.ExecuteConsoleCommand, IsMutating: false, Guid.NewGuid().ToString("N"),
                ServerId: server, CommandPayload: new ConsoleCommandPayload(input).ToJson()),
            user,
            cancellationToken).ConfigureAwait(false);

        await _audit.WriteAsync(
            new AuditEntry(ConsoleAuditActions.CommandExecuted, AuditOutcome.Succeeded, user, server, DetailFor(input, $"operation {operation.Id}")),
            cancellationToken).ConfigureAwait(false);

        return ConsoleExecutionResult.Success(operation.Id);
    }

    // The audited detail records the command the operator ran (non-secret — credential commands are denied by
    // policy) plus a short context, both length-bounded so a hostile line cannot bloat the audit row.
    private static string DetailFor(string input, string context)
    {
        const int max = 256;
        string command = input.Length > max ? input[..max] : input;
        return $"{command} — {context}";
    }
}
