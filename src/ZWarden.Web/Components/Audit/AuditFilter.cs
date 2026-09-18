using System.Globalization;
using ZWarden.Application.Audit;
using ZWarden.Domain.Audit;

namespace ZWarden.Web.Components.Audit;

/// <summary>
/// Builds a tenant-scoped <see cref="AuditQuery"/> from the viewer's raw GET filter values (#161), shared by the
/// <c>/audit</c> page and the <c>/audit/export</c> endpoint so both parse identically — a filtered view and its
/// export always agree. Values arrive as strings from the query string; unparseable or blank ones simply do not
/// constrain (fail-open on the filter, never on tenancy). Dates are day-granular (an <c>&lt;input type="date"&gt;</c>
/// yields <c>yyyy-MM-dd</c>): "from" is the start of the named day and "to" the end of it, both in UTC, so the
/// range is inclusive of both endpoints against the repository's <c>OccurredAt &gt;= From</c> / <c>&lt;= To</c>.
/// </summary>
public static class AuditFilter
{
    public static AuditQuery Build(
        string? action,
        string? outcome,
        string? correlation,
        string? from,
        string? to,
        int skip,
        int take)
        => new(
            From: ParseFrom(from),
            To: ParseTo(to),
            Action: string.IsNullOrWhiteSpace(action) ? null : action.Trim(),
            Outcome: Enum.TryParse(outcome, out AuditOutcome parsed) ? parsed : null,
            CorrelationId: string.IsNullOrWhiteSpace(correlation) ? null : correlation.Trim(),
            Skip: skip < 0 ? 0 : skip,
            Take: take);

    /// <summary>The start of the named UTC day, or <c>null</c> when unset/unparseable.</summary>
    public static DateTimeOffset? ParseFrom(string? value)
        => DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly day)
            ? new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero)
            : null;

    /// <summary>The end of the named UTC day (inclusive), or <c>null</c> when unset/unparseable.</summary>
    public static DateTimeOffset? ParseTo(string? value)
        => DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly day)
            ? new DateTimeOffset(day.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero)
            : null;
}
