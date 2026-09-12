using Microsoft.Extensions.Logging;
using ZWarden.Application.Authentication;
using ZWarden.Domain.Ids;

namespace ZWarden.Infrastructure.Identity;

/// <summary>
/// The default <see cref="IAuthenticationEventSink"/>: it logs the event (kind, user, tenant, non-secret
/// detail) and stores nothing durable. F6 replaces it with a queryable audit sink. Because
/// <see cref="AuthenticationEvent"/> carries no credential, this cannot log one.
/// </summary>
public sealed partial class LoggingAuthenticationEventSink : IAuthenticationEventSink
{
    private readonly ILogger<LoggingAuthenticationEventSink> _logger;

    public LoggingAuthenticationEventSink(ILogger<LoggingAuthenticationEventSink> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    public Task RecordAsync(AuthenticationEvent authenticationEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(authenticationEvent);
        LogEvent(
            authenticationEvent.Kind,
            authenticationEvent.UserId,
            authenticationEvent.TenantId,
            authenticationEvent.Detail);
        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Authentication event {Kind} for user {UserId} in tenant {TenantId}: {Detail}")]
    private partial void LogEvent(AuthenticationEventKind kind, UserId? userId, TenantId? tenantId, string? detail);
}
