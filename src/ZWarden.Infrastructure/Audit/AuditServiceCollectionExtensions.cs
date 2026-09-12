using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ZWarden.Application.Audit;
using ZWarden.Application.Authentication;

namespace ZWarden.Infrastructure.Audit;

/// <summary>
/// Composition seam for audit (F6; ADR 0019). Registers the append-only <see cref="IAuditWriter"/>, the
/// tenant-scoped <see cref="IAuditQuery"/>, the ambient <see cref="ICorrelationContext"/>, and — closing the
/// loop F4 deferred — <b>supersedes the logging-only authentication sink</b> with the durable
/// <see cref="AuditAuthenticationEventSink"/>. The host calls this after <c>AddZWardenAuthorization</c>, so
/// the durable sink replaces the auth extension's <c>TryAdd</c> default. The writer and query resolve the
/// request-scoped <c>ZWardenDbContext</c> and its session-derived tenant (ADR 0016).
/// </summary>
public static class AuditServiceCollectionExtensions
{
    public static IServiceCollection AddZWardenAudit(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<ICorrelationContext, ActivityCorrelationContext>();

        services.AddScoped<AuditEventRepository>();
        services.AddScoped<IAuditWriter, AuditWriter>();
        services.AddScoped<IAuditQuery, AuditQueryService>();

        // F4 emitted authentication events through a logging-only default and left the durable binding to F6.
        services.Replace(ServiceDescriptor.Scoped<IAuthenticationEventSink, AuditAuthenticationEventSink>());

        return services;
    }
}
