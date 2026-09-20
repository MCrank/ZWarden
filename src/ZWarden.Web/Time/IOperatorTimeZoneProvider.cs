namespace ZWarden.Web.Time;

/// <summary>
/// Resolves the current request's operator display time zone from the <see cref="OperatorTimeZone.CookieName"/>
/// cookie (#211). Scoped, so static-SSR pages can read it per request; interactive islands, which have no
/// <c>HttpContext</c>, are instead handed the resolved zone id as a parameter by their static parent.
/// </summary>
public interface IOperatorTimeZoneProvider
{
    /// <summary>The operator's chosen display zone, or <see cref="TimeZoneInfo.Utc"/> when none is set.</summary>
    TimeZoneInfo Zone { get; }

    /// <summary>Whether the operator has opted into a local (non-UTC) display zone.</summary>
    bool IsLocal { get; }
}
