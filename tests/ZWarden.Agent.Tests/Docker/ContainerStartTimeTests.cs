using ZWarden.Agent.Docker;

namespace ZWarden.Agent.Tests.Docker;

/// <summary>
/// #257: Docker's inspect <c>State.StartedAt</c> becomes the fleet uptime source. It is untrusted daemon output, so
/// the parse is total — anything that is not a real UTC start time (blank, garbage, the never-started zero time)
/// reads as <c>null</c> rather than an absurd uptime.
/// </summary>
public class ContainerStartTimeTests
{
    [Test]
    public async Task Docker_nanosecond_RFC3339_parses_to_UTC()
    {
        DateTimeOffset? parsed = ContainerStartTime.Parse("2026-09-25T14:03:07.123456789Z");

        await Assert.That(parsed).IsEqualTo(new DateTimeOffset(2026, 9, 25, 14, 3, 7, 123, TimeSpan.Zero).AddTicks(4567));
    }

    [Test]
    public async Task An_offset_timestamp_is_normalised_to_UTC()
    {
        DateTimeOffset? parsed = ContainerStartTime.Parse("2026-09-25T16:03:07+02:00");

        await Assert.That(parsed).IsEqualTo(new DateTimeOffset(2026, 9, 25, 14, 3, 7, TimeSpan.Zero));
        await Assert.That(parsed!.Value.Offset).IsEqualTo(TimeSpan.Zero);
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments("not-a-time")]
    [Arguments("0001-01-01T00:00:00Z")]
    public async Task Missing_garbage_or_zero_time_is_null(string? raw)
    {
        await Assert.That(ContainerStartTime.Parse(raw)).IsNull();
    }
}
