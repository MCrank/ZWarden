using System.Globalization;
using System.Text;
using ZWarden.Application.Audit;
using ZWarden.Domain.Audit;

namespace ZWarden.Web.Components.Audit;

/// <summary>
/// The audit-trail export endpoint (#161). It streams the current filtered view as CSV so an operator can take
/// the tenant's security/administration record off-box. It is gated by the same <c>Audit.View</c> policy as the
/// viewer page (acceptance criterion: export respects the same authorization) and reads through the tenant-scoped
/// <see cref="IAuditQuery"/>, so every exported row is the ambient tenant's own (ADR 0016). It carries only the
/// non-secret projection the entity already exposes — ids, action, outcome, timing, correlation, detail — never a
/// credential. The CSV is machine-readable: ids are emitted in their canonical typed form (<c>usr-</c>/<c>srv-</c>),
/// not resolved to display names.
/// </summary>
public static class AuditEndpoints
{
    // A generous ceiling so an export of a filtered view is complete for a self-hosted tenant, while a runaway
    // unfiltered export cannot exhaust memory. Beyond it the operator narrows the filter (date range, action).
    private const int ExportLimit = 100_000;

    public static IEndpointRouteBuilder MapAuditEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet("/audit/export", async (
            IAuditQuery audit,
            string? action,
            string? outcome,
            string? correlation,
            string? from,
            string? to,
            CancellationToken cancellationToken) =>
        {
            AuditQuery query = AuditFilter.Build(action, outcome, correlation, from, to, skip: 0, take: ExportLimit);
            IReadOnlyList<AuditEventView> events = await audit.QueryAsync(query, cancellationToken).ConfigureAwait(false);

            byte[] csv = Encoding.UTF8.GetBytes(ToCsv(events));
            string fileName = $"zwarden-audit-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.csv";
            return Results.File(csv, "text/csv", fileName);
        }).RequireAuthorization("Audit.View");

        return endpoints;
    }

    // A minimal RFC 4180 CSV: newest-first (as the query returns), a header row, and CRLF line endings. Every
    // cell is escaped, so a Detail containing a comma, quote, or newline cannot break the shape.
    private static string ToCsv(IReadOnlyList<AuditEventView> events)
    {
        StringBuilder sb = new();
        sb.Append("Time (UTC),Actor,Action,Target,Result,Correlation,Detail\r\n");
        foreach (AuditEventView e in events)
        {
            sb.Append(Field(e.OccurredAt.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)));
            sb.Append(',').Append(Field(e.ActorUserId?.ToString()));
            sb.Append(',').Append(Field(e.Action));
            sb.Append(',').Append(Field(e.ServerId?.ToString()));
            sb.Append(',').Append(Field(e.Outcome.ToString()));
            sb.Append(',').Append(Field(e.CorrelationId));
            sb.Append(',').Append(Field(e.Detail));
            sb.Append("\r\n");
        }

        return sb.ToString();
    }

    private static string Field(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        if (value.IndexOfAny([',', '"', '\r', '\n']) < 0)
        {
            return value;
        }

        return $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }
}
