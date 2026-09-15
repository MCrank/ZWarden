using ZWarden.Application.Servers;
using ZWarden.Domain.Ids;
using ZWarden.Web.Components.Pages.Servers;

namespace ZWarden.Web.Tests.Servers;

/// <summary>
/// F27 PR-B: the live-logs view filter. The stdout/stderr toggles and the case-insensitive text contains that the
/// panel applies over its buffered tail, tested as a pure function.
/// </summary>
public class LogLineFilterTests
{
    private static readonly DateTimeOffset At = new(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);

    private static readonly IReadOnlyList<ServerLogLineView> Lines =
    [
        new(1, At, IsStderr: false, "connection accepted", Truncated: false),
        new(2, At, IsStderr: true, "java exception", Truncated: false),
        new(3, At, IsStderr: false, "world saved", Truncated: false),
    ];

    [Test]
    public async Task With_both_streams_and_no_filter_every_line_passes()
    {
        IReadOnlyList<ServerLogLineView> result = LogLineFilter.Apply(Lines, showStdout: true, showStderr: true, filter: "");

        await Assert.That(result).Count().IsEqualTo(3);
    }

    [Test]
    public async Task Turning_off_stderr_hides_the_stderr_line()
    {
        IReadOnlyList<ServerLogLineView> result = LogLineFilter.Apply(Lines, showStdout: true, showStderr: false, filter: "");

        await Assert.That(result.Select(l => l.Text)).DoesNotContain("java exception");
        await Assert.That(result).Count().IsEqualTo(2);
    }

    [Test]
    public async Task Turning_off_stdout_leaves_only_stderr()
    {
        IReadOnlyList<ServerLogLineView> result = LogLineFilter.Apply(Lines, showStdout: false, showStderr: true, filter: "");

        await Assert.That(result).HasSingleItem();
        await Assert.That(result[0].Text).IsEqualTo("java exception");
    }

    [Test]
    public async Task The_text_filter_is_a_case_insensitive_contains()
    {
        IReadOnlyList<ServerLogLineView> result = LogLineFilter.Apply(Lines, showStdout: true, showStderr: true, filter: "SAVED");

        await Assert.That(result).HasSingleItem();
        await Assert.That(result[0].Text).IsEqualTo("world saved");
    }

    [Test]
    public async Task The_stream_toggle_and_text_filter_compose()
    {
        // "exception" matches only the stderr line, which the stderr toggle then hides — nothing passes.
        IReadOnlyList<ServerLogLineView> result = LogLineFilter.Apply(Lines, showStdout: true, showStderr: false, filter: "exception");

        await Assert.That(result).IsEmpty();
    }
}
