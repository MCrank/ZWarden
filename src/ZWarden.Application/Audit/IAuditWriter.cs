namespace ZWarden.Application.Audit;

/// <summary>
/// The one way to record an audit event (F6; ADR 0019). It is <b>append-only</b> by contract: there is no
/// update or delete — an audit trail is add-only. It appends through the ambient tenant (ADR 0016), the
/// clock, and the current correlation id. The interface lives in Application and references only Domain
/// types — no EF, no ASP.NET Core.
/// </summary>
public interface IAuditWriter
{
    /// <summary>Appends an audit event built from <paramref name="entry"/>.</summary>
    Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default);
}
