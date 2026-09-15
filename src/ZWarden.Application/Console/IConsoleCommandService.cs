using ZWarden.Domain.Ids;

namespace ZWarden.Application.Console;

/// <summary>Why a console command was refused (F28). Fail-closed: the service re-checks the elevated server-scoped
/// <c>Console.Execute</c> permission, resolves the Server through the tenant filter, and runs the F28 input-safety
/// and command policy before enqueueing anything.</summary>
public enum ConsoleCommandFailure
{
    /// <summary>The caller lacks the elevated server-scoped <c>Console.Execute</c> permission on this Server.</summary>
    NotAuthorized,

    /// <summary>No such Server in the current tenant (or not visible to this caller's tenant).</summary>
    ServerNotFound,

    /// <summary>The command failed input safety — not a single, bounded, printable-ASCII line
    /// (<c>ConsoleCommandRules.ValidateInput</c>).</summary>
    InvalidInput,

    /// <summary>The command is refused by the F28 command policy (ADR 0032) — it mints or exposes a credential, or
    /// is <c>quit</c> (<c>ConsoleCommandRules.EvaluatePolicy</c>). Denials are audited.</summary>
    Denied,
}

/// <summary>The outcome of a console command (F28): on success, the enqueued Operation whose state (and observed
/// output) the caller follows; otherwise a typed failure with an optional operator-facing detail.</summary>
public sealed record ConsoleExecutionResult(bool Succeeded, OperationId? Operation, ConsoleCommandFailure? Failure, string? Detail)
{
    public static ConsoleExecutionResult Success(OperationId operation) => new(true, operation, null, null);

    public static ConsoleExecutionResult Denied(ConsoleCommandFailure failure, string? detail = null) =>
        new(false, null, failure, detail);
}

/// <summary>
/// The tenant-scoped remote-console service (F28): run one operator-authored RCON command line as a durable,
/// authorized, audited, <b>non-mutating</b> Operation — a sibling of the F18 diagnostics and F19 player services.
/// Fail-closed (ADR 0018): it resolves the Server through the tenant filter (a foreign/unknown Server is
/// <see cref="ConsoleCommandFailure.ServerNotFound"/>), authorizes the elevated server-scoped
/// <c>Console.Execute</c> permission against that Server, and runs the F28 input-safety + command policy
/// (<c>ConsoleCommandRules</c>, ADR 0032) before enqueuing. The command line rides the Operation's command payload
/// for the Agent to send (ADR 0026). The observed output returns on the completion into the in-memory
/// <see cref="IConsoleOutputCache"/>.
/// </summary>
public interface IConsoleCommandService
{
    Task<ConsoleExecutionResult> ExecuteAsync(UserId user, ServerId server, string input, CancellationToken cancellationToken = default);
}
