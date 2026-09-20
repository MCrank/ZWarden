using Bunit;
using Microsoft.Extensions.DependencyInjection;
using ZWarden.Application.Servers;
using ZWarden.Domain.Ids;
using ZWarden.Infrastructure.Servers;
using ZWarden.Web.Agents;
using ZWarden.Web.Components.Pages.Servers;

namespace ZWarden.Web.Tests.Servers;

/// <summary>
/// F27 PR-B: the interactive live-logs island. It subscribes through the coordinator on open, reads the
/// ownership-guarded buffer, renders lines as data (stdout/stderr distinguished), filters client-side, surfaces
/// the dropped indicator, and unsubscribes on dispose. The panel is built from Blueprint wrappers, so the context
/// runs loose JSInterop.
/// </summary>
public class LiveServerLogPanelTests
{
    private static readonly DateTimeOffset At = new(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);

    private static ServerLogLineView Line(long seq, bool isStderr, string text, bool truncated = false) =>
        new(seq, At, isStderr, text, truncated);

    private static ServerLogBuffer Register(BunitContext ctx, RecordingCoordinator coordinator)
    {
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ServerLogBuffer buffer = new(new ServerLogBufferOptions());
        ctx.Services.AddSingleton<IServerLogBuffer>(buffer);
        ctx.Services.AddSingleton<IServerLogSubscriptionCoordinator>(coordinator);
        return buffer;
    }

    [Test]
    public async Task It_subscribes_and_renders_buffered_lines_as_data()
    {
        using BunitContext ctx = new();
        RecordingCoordinator coordinator = new();
        ServerLogBuffer buffer = Register(ctx, coordinator);
        AgentId agent = AgentId.New();
        ServerId server = ServerId.New();
        buffer.Append(agent, server, [Line(1, isStderr: false, "world loaded"), Line(2, isStderr: true, "<b>boom</b>")], dropped: false);

        var cut = ctx.Render<LiveServerLogPanel>(p => p
            .Add(c => c.ServerId, server.ToString())
            .Add(c => c.AgentId, agent.ToString()));

        string markup = cut.Markup;
        await Assert.That(coordinator.Subscribed).Contains(server);
        await Assert.That(markup).Contains("world loaded");
        // Untrusted text is rendered as data — the markup carries the HTML-encoded form, never live tags.
        await Assert.That(markup).Contains("&lt;b&gt;boom&lt;/b&gt;");
        await Assert.That(markup).DoesNotContain("<b>boom</b>");
        await Assert.That(markup).Contains("data-stream=\"stderr\"");
        await Assert.That(markup).Contains("data-stream=\"stdout\"");
        // Lines use the two-column grid so a wrapped message aligns under the message, not the timestamp (#212).
        await Assert.That(markup).Contains("grid-cols-[max-content_1fr]");
        // The scroll viewport is tagged for the stick-to-bottom tail helper; on first render the panel attaches the
        // scroll listener and opens at the newest line.
        await Assert.That(markup).Contains("zw-log-viewport");
        cut.WaitForState(() => ctx.JSInterop.Invocations.Any(i => i.Identifier == "zwLiveLogs.attach"));
        cut.WaitForState(() => ctx.JSInterop.Invocations.Any(i => i.Identifier == "zwLiveLogs.toBottom"));
    }

    [Test]
    public async Task It_renders_timestamps_in_the_supplied_time_zone()
    {
        using BunitContext ctx = new();
        ServerLogBuffer buffer = Register(ctx, new RecordingCoordinator());
        AgentId agent = AgentId.New();
        ServerId server = ServerId.New();
        buffer.Append(agent, server, [Line(1, isStderr: false, "world loaded")], dropped: false);

        var cut = ctx.Render<LiveServerLogPanel>(p => p
            .Add(c => c.ServerId, server.ToString())
            .Add(c => c.AgentId, agent.ToString())
            .Add(c => c.TimeZoneId, "America/New_York"));

        // The panel formats the line's timestamp in the supplied zone (#211), not a hardcoded UTC — assert it
        // matches the same helper the panel uses, so the test is robust to the platform's tz database.
        TimeZoneInfo zone = ZWarden.Web.Time.OperatorTimeZone.Resolve("America/New_York");
        string expected = ZWarden.Web.Time.OperatorTimeZone.Format(At, zone, "HH:mm:ss");
        await Assert.That(cut.Markup).Contains(expected);
        if (zone != TimeZoneInfo.Utc)
        {
            await Assert.That(cut.Markup).DoesNotContain("12:00:00 UTC");
        }
    }

    [Test]
    public async Task It_shows_the_waiting_state_when_the_buffer_is_empty()
    {
        using BunitContext ctx = new();
        Register(ctx, new RecordingCoordinator());

        var cut = ctx.Render<LiveServerLogPanel>(p => p
            .Add(c => c.ServerId, ServerId.New().ToString())
            .Add(c => c.AgentId, AgentId.New().ToString()));

        await Assert.That(cut.Markup).Contains("data-log-empty");
    }

    [Test]
    public async Task It_surfaces_the_dropped_indicator()
    {
        using BunitContext ctx = new();
        ServerLogBuffer buffer = Register(ctx, new RecordingCoordinator());
        AgentId agent = AgentId.New();
        ServerId server = ServerId.New();
        buffer.Append(agent, server, [Line(1, isStderr: false, "kept")], dropped: true);

        var cut = ctx.Render<LiveServerLogPanel>(p => p
            .Add(c => c.ServerId, server.ToString())
            .Add(c => c.AgentId, agent.ToString()));

        await Assert.That(cut.Markup).Contains("data-log-dropped");
    }

    [Test]
    public async Task It_unsubscribes_when_disposed()
    {
        using BunitContext ctx = new();
        RecordingCoordinator coordinator = new();
        Register(ctx, coordinator);
        AgentId agent = AgentId.New();
        ServerId server = ServerId.New();

        var cut = ctx.Render<LiveServerLogPanel>(p => p
            .Add(c => c.ServerId, server.ToString())
            .Add(c => c.AgentId, agent.ToString()));

        await cut.Instance.DisposeAsync();

        await Assert.That(coordinator.Unsubscribed).Contains(server);
    }

    private sealed class RecordingCoordinator : IServerLogSubscriptionCoordinator
    {
        public List<ServerId> Subscribed { get; } = [];

        public List<ServerId> Unsubscribed { get; } = [];

        public Task SubscribeAsync(ServerId serverId, AgentId owningAgentId, CancellationToken cancellationToken)
        {
            Subscribed.Add(serverId);
            return Task.CompletedTask;
        }

        public Task UnsubscribeAsync(ServerId serverId, AgentId owningAgentId, CancellationToken cancellationToken)
        {
            Unsubscribed.Add(serverId);
            return Task.CompletedTask;
        }
    }
}
