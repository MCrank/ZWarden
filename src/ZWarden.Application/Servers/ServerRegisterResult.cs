using ZWarden.Domain.Ids;

namespace ZWarden.Application.Servers;

/// <summary>Why a registration was refused (F14 PR-B). Fail-closed: the service re-checks authorization and
/// the Agent's existence before creating anything.</summary>
public enum ServerRegisterFailure
{
    /// <summary>The caller lacks <c>Server.Register</c> in the current tenant.</summary>
    NotAuthorized,

    /// <summary>No such Agent in the current tenant.</summary>
    AgentNotFound,
}

/// <summary>The outcome of registering a new Server (F14 PR-B): on success, the new Server and the
/// provisioning Operation enqueued to create its container.</summary>
public sealed record ServerRegisterResult(
    bool Succeeded,
    ServerId? Server,
    OperationId? Operation,
    ServerRegisterFailure? Failure)
{
    public static ServerRegisterResult Success(ServerId server, OperationId operation) =>
        new(true, server, operation, null);

    public static ServerRegisterResult Denied(ServerRegisterFailure failure) => new(false, null, null, failure);
}
