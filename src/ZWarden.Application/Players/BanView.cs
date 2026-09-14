using ZWarden.Domain.Players;

namespace ZWarden.Application.Players;

/// <summary>
/// A read-model view of a <see cref="BanRecord"/> for the ban list (F19). Carries only non-secret display fields;
/// the username is <b>untrusted</b> operator input, escaped at render. The registry lists the bans ZWarden
/// issued, not PZ's authoritative set (ADR 0027).
/// </summary>
/// <param name="Username">The banned account username.</param>
/// <param name="Reason">The recorded reason, or <c>null</c>.</param>
/// <param name="IsActive">Whether the ban is currently active (vs. lifted through ZWarden).</param>
/// <param name="IssuedAt">When the ban was issued through ZWarden (UTC).</param>
public sealed record BanView(string Username, string? Reason, bool IsActive, DateTimeOffset IssuedAt);
