namespace ZWarden.Infrastructure.Persistence;

/// <summary>Which tenant-scope rule a rejected write broke (ADR 0016). Carried on the exception so a
/// handler can branch without parsing a message; the message itself never names a tenant.</summary>
public enum TenantScopeViolation
{
    /// <summary>An insert assigned the row to a tenant other than the ambient one.</summary>
    ForeignTenantInsert,

    /// <summary>An update targeted a row not owned by the ambient tenant.</summary>
    ForeignTenantWrite,

    /// <summary>An update tried to change a row's immutable tenant scope.</summary>
    ScopeMutation,
}

/// <summary>
/// Thrown when a save would cross or mutate a tenant scope (trust-boundaries §6; ADR 0016). The
/// message states <i>that</i> the scope was wrong and names no tenant id or row content
/// (trust-boundaries §2), so it is safe to log.
/// </summary>
public sealed class TenantScopeViolationException : InvalidOperationException
{
    public TenantScopeViolationException(TenantScopeViolation violation)
        : base(MessageFor(violation))
        => Violation = violation;

    /// <summary>The rule that was broken.</summary>
    public TenantScopeViolation Violation { get; }

    private static string MessageFor(TenantScopeViolation violation) => violation switch
    {
        TenantScopeViolation.ForeignTenantInsert =>
            "Refusing to insert a tenant-owned entity assigned to a tenant other than the ambient one.",
        TenantScopeViolation.ForeignTenantWrite =>
            "Refusing to update a tenant-owned entity that is not owned by the ambient tenant.",
        TenantScopeViolation.ScopeMutation =>
            "A tenant-owned entity's tenant scope is immutable and cannot be changed.",
        _ => "Tenant scope violation.",
    };
}
