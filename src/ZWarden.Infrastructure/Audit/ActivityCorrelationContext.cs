using System.Diagnostics;
using ZWarden.Application.Audit;

namespace ZWarden.Infrastructure.Audit;

/// <summary>
/// The default <see cref="ICorrelationContext"/> (F6; ADR 0019): the ambient correlation id is the current
/// <see cref="Activity"/>'s trace id. ASP.NET Core opens an <see cref="Activity"/> per request, so the audit
/// events of one request share a trace id without any call site threading one through. Returns <c>null</c>
/// when no activity (or no trace id) is in scope.
/// </summary>
public sealed class ActivityCorrelationContext : ICorrelationContext
{
    private const string NoTraceId = "00000000000000000000000000000000";

    /// <inheritdoc />
    public string? CurrentCorrelationId
    {
        get
        {
            string? traceId = Activity.Current?.TraceId.ToString();
            return string.IsNullOrEmpty(traceId) || traceId == NoTraceId ? null : traceId;
        }
    }
}
