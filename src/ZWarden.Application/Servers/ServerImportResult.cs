using ZWarden.Domain.Ids;

namespace ZWarden.Application.Servers;

/// <summary>Why an import was refused (F14). Fail-closed: the service re-checks authorization and validates the
/// target against what the Agent actually discovered, so a forged or stale request cannot adopt an
/// arbitrary id.</summary>
public enum ServerImportFailure
{
    /// <summary>The caller lacks <c>Server.Register</c> in the current tenant.</summary>
    NotAuthorized,

    /// <summary>No such Agent in the current tenant.</summary>
    AgentNotFound,

    /// <summary>The Server id was not in the Agent's last discovery snapshot — nothing to adopt.</summary>
    NotDiscovered,
}

/// <summary>The outcome of an import (F14). Adopting an already-imported Server is idempotent success.</summary>
public sealed record ServerImportResult(bool Succeeded, ServerId? Server, ServerImportFailure? Failure)
{
    public static ServerImportResult Success(ServerId server) => new(true, server, null);

    public static ServerImportResult Denied(ServerImportFailure failure) => new(false, null, failure);
}
