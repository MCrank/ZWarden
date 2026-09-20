using Microsoft.AspNetCore.Http;

namespace ZWarden.Web.Time;

/// <summary>
/// The <see cref="IOperatorTimeZoneProvider"/> over the request cookie (#211). Reads
/// <see cref="OperatorTimeZone.CookieName"/> from the current <see cref="HttpContext"/> and resolves it
/// fail-safe to UTC. Registered scoped; a background scope with no <c>HttpContext</c> resolves to UTC.
/// </summary>
public sealed class OperatorTimeZoneProvider : IOperatorTimeZoneProvider
{
    private readonly IHttpContextAccessor _accessor;

    public OperatorTimeZoneProvider(IHttpContextAccessor accessor)
    {
        ArgumentNullException.ThrowIfNull(accessor);
        _accessor = accessor;
    }

    private string? CookieValue => _accessor.HttpContext?.Request.Cookies[OperatorTimeZone.CookieName];

    public TimeZoneInfo Zone => OperatorTimeZone.Resolve(CookieValue);

    public bool IsLocal => Zone != TimeZoneInfo.Utc && !string.IsNullOrWhiteSpace(CookieValue);
}
