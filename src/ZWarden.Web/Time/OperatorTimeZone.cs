using System.Globalization;

namespace ZWarden.Web.Time;

/// <summary>
/// Renders instants in the operator's chosen display time zone (#211). The operator opts into local time from
/// Settings, which stores an IANA time-zone id (e.g. <c>America/Chicago</c>) in the <see cref="CookieName"/>
/// cookie on their browser; absent or unrecognized means UTC, the default and canonical zone. Formatting always
/// appends an unambiguous offset label so a value is never mistaken for another zone. Pure and static so it is
/// trivially testable and callable from both static-SSR pages and interactive islands.
/// </summary>
public static class OperatorTimeZone
{
    /// <summary>The per-browser cookie holding the operator's chosen IANA time-zone id (empty/absent ⇒ UTC).</summary>
    public const string CookieName = "zw-tz";

    /// <summary>
    /// Resolves the display zone from a cookie value. A null/blank value, or one .NET cannot resolve to a system
    /// zone, falls back to <see cref="TimeZoneInfo.Utc"/> — the operator's preference never breaks rendering.
    /// </summary>
    public static TimeZoneInfo Resolve(string? cookieZoneId)
    {
        if (string.IsNullOrWhiteSpace(cookieZoneId))
        {
            return TimeZoneInfo.Utc;
        }

        try
        {
            // .NET 6+ resolves IANA ids on every platform (ICU), so a browser-supplied zone works cross-host.
            return TimeZoneInfo.FindSystemTimeZoneById(cookieZoneId.Trim());
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.Utc;
        }
        catch (InvalidTimeZoneException)
        {
            return TimeZoneInfo.Utc;
        }
    }

    /// <summary>
    /// Converts <paramref name="value"/> to <paramref name="zone"/> and formats it with an appended offset label
    /// (<c>UTC</c> at zero offset, else <c>UTC+hh:mm</c>/<c>UTC-hh:mm</c> at that instant, honouring DST).
    /// </summary>
    public static string Format(DateTimeOffset value, TimeZoneInfo zone, string format = "yyyy-MM-dd HH:mm:ss")
    {
        ArgumentNullException.ThrowIfNull(zone);
        DateTimeOffset local = TimeZoneInfo.ConvertTime(value, zone);
        return $"{local.ToString(format, CultureInfo.InvariantCulture)} {OffsetLabel(local.Offset)}";
    }

    /// <summary>The short label for a UTC offset: <c>UTC</c> at zero, otherwise <c>UTC±hh:mm</c>.</summary>
    public static string OffsetLabel(TimeSpan offset) => offset == TimeSpan.Zero
        ? "UTC"
        : $"UTC{(offset < TimeSpan.Zero ? "-" : "+")}{offset.Duration():hh\\:mm}";
}
