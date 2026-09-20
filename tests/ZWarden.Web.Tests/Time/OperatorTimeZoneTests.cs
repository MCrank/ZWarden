using ZWarden.Web.Time;

namespace ZWarden.Web.Tests.Time;

/// <summary>
/// #211: the operator display-time-zone helper. It resolves a per-browser cookie id to a system zone (fail-safe
/// to UTC), and formats an instant in that zone with an unambiguous offset label. Pure, so it is tested directly.
/// </summary>
public sealed class OperatorTimeZoneTests
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task Resolve_falls_back_to_utc_for_a_blank_or_unknown_id()
    {
        await Assert.That(OperatorTimeZone.Resolve(null)).IsEqualTo(TimeZoneInfo.Utc);
        await Assert.That(OperatorTimeZone.Resolve("")).IsEqualTo(TimeZoneInfo.Utc);
        await Assert.That(OperatorTimeZone.Resolve("   ")).IsEqualTo(TimeZoneInfo.Utc);
        await Assert.That(OperatorTimeZone.Resolve("Not/ARealZone")).IsEqualTo(TimeZoneInfo.Utc);
    }

    [Test]
    public async Task Resolve_returns_the_matching_system_zone()
    {
        // The host's own zone id is always resolvable, cross-platform (UTC on CI).
        TimeZoneInfo resolved = OperatorTimeZone.Resolve(TimeZoneInfo.Local.Id);
        await Assert.That(resolved.Id).IsEqualTo(TimeZoneInfo.Local.Id);
    }

    [Test]
    public async Task Format_in_utc_appends_a_utc_label()
    {
        string formatted = OperatorTimeZone.Format(Noon, TimeZoneInfo.Utc);
        await Assert.That(formatted).IsEqualTo("2026-09-14 12:00:00 UTC");
    }

    [Test]
    public async Task Format_converts_to_the_zone_and_appends_the_offset_label()
    {
        // A fixed −5h zone is deterministic without depending on the platform tz database.
        TimeZoneInfo minusFive = TimeZoneInfo.CreateCustomTimeZone("t-5", TimeSpan.FromHours(-5), "t-5", "t-5");

        string formatted = OperatorTimeZone.Format(Noon, minusFive, "HH:mm:ss");

        await Assert.That(formatted).IsEqualTo("07:00:00 UTC-05:00");
    }
}
