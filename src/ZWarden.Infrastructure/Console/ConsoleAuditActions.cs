namespace ZWarden.Infrastructure.Console;

/// <summary>
/// The stable, machine-readable audit action names for the remote administrative console (F28; F6, ADR 0019).
/// Server-scoped, actor-attributed, carrying the command line as non-secret detail (the credential-reading commands
/// are denied by policy — ADR 0032). The audit trail is the console's durable history (F28 D-4).
/// </summary>
public static class ConsoleAuditActions
{
    /// <summary>An operator ran a console command (a non-mutating console Operation was enqueued). The Operation's
    /// own <c>Operation.*</c> trail then records dispatch and the terminal outcome.</summary>
    public const string CommandExecuted = "Console.CommandExecuted";

    /// <summary>An operator's console command was refused by the F28 command policy (ADR 0032) — a
    /// credential-minting/exposing command or <c>quit</c> — and never sent.</summary>
    public const string CommandDenied = "Console.CommandDenied";
}
