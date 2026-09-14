using ZWarden.Domain.Ids;

namespace ZWarden.Application.Mods;

/// <summary>
/// The operator-facing Workshop-and-mod discovery service (F21): enqueue a read-only, authorized,
/// <b>non-mutating</b> discovery Operation on a Server's Agent. Fail-closed (ADR 0018): it resolves the Server
/// through the tenant filter (a foreign/unknown Server is <see cref="ModDiscoveryFailure.ServerNotFound"/>) and
/// authorizes <c>Mod.View</c> against that specific Server before enqueuing. Being non-mutating, discovery never
/// claims the per-server lock (ADR 0022). It is a <b>read</b>, so it is not audited (criterion 11 is administrative
/// activity). The observed inventory returns on the completion into the in-memory <see cref="IModInventoryCache"/>;
/// the caller polls the returned Operation.
/// </summary>
public interface IModDiscoveryService
{
    /// <summary>Enqueues a discovery Operation for <paramref name="server"/> after authorizing <c>Mod.View</c>.</summary>
    Task<ModDiscoveryDispatch> RequestAsync(UserId user, ServerId server, CancellationToken cancellationToken = default);
}

/// <summary>Why a discovery request was refused (F21). Fail-closed: the service resolves the Server through the
/// tenant filter and re-checks the server-scoped permission before enqueuing anything.</summary>
public enum ModDiscoveryFailure
{
    /// <summary>The caller lacks <c>Mod.View</c> on this Server.</summary>
    NotAuthorized,

    /// <summary>No such Server in the current tenant (or not visible to this caller's tenant).</summary>
    ServerNotFound,
}

/// <summary>The outcome of a discovery request (F21): on success, the enqueued (non-mutating) Operation whose
/// state the caller polls; otherwise a typed failure. Non-mutating, so there is no <c>ServerBusy</c> case.</summary>
public sealed record ModDiscoveryDispatch(bool Succeeded, OperationId? Operation, ModDiscoveryFailure? Failure)
{
    public static ModDiscoveryDispatch Success(OperationId operation) => new(true, operation, null);

    public static ModDiscoveryDispatch Denied(ModDiscoveryFailure failure) => new(false, null, failure);
}
