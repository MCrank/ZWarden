using ZWarden.Domain.Ids;

namespace ZWarden.Application.Operations;

/// <summary>
/// Thrown when an Operation named by id is not visible in the current tenant — it does not exist, or it
/// belongs to another tenant and is filtered out (ADR 0016). The two are indistinguishable by design: the
/// engine never reveals whether an id exists in a tenant the caller cannot see.
/// </summary>
public sealed class OperationNotFoundException : Exception
{
    /// <summary>The Operation id that was not found in the current tenant.</summary>
    public OperationId OperationId { get; }

    /// <summary>Creates the exception for <paramref name="operationId"/>.</summary>
    public OperationNotFoundException(OperationId operationId)
        : base($"Operation {operationId} was not found in the current tenant.")
    {
        OperationId = operationId;
    }
}
