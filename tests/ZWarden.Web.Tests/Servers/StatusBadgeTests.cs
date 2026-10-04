using Bunit;
using ZWarden.Domain.Servers;
using ZWarden.Web.Components.Ui;

namespace ZWarden.Web.Tests.Servers;

/// <summary>
/// The owned <see cref="StatusBadge"/> maps the Domain's last-reported <see cref="ServerRunState"/> — or, for the
/// Hosts page, a <see cref="HostStatus"/> — onto one Signal tone class (#335). The tinted chip (fill, border, label
/// ink, dot glow) is styled from that class in status.css, and live-status.js swaps the same class when a fleet
/// row's state flips, so this asserts the mapping both rely on.
/// </summary>
public class StatusBadgeTests
{
    [Test]
    [Arguments(ServerRunState.Running, "zw-status-running")]
    [Arguments(ServerRunState.Stopped, "zw-status-stopped")]
    [Arguments(ServerRunState.Starting, "zw-status-busy")]
    [Arguments(ServerRunState.Stopping, "zw-status-busy")]
    [Arguments(ServerRunState.Failed, "zw-status-unhealthy")]
    [Arguments(ServerRunState.Unknown, "zw-status-unknown")]
    public async Task Badge_maps_run_state_to_a_tinted_tone(ServerRunState state, string toneClass)
    {
        using BunitContext ctx = new();

        var cut = ctx.Render<StatusBadge>(p => p.Add(c => c.State, state));

        var badge = cut.Find("[data-status-badge]");
        await Assert.That(badge.ClassList).Contains("zw-status");
        await Assert.That(badge.ClassList).Contains(toneClass);
        await Assert.That(badge.ClassList).Contains("w-[112px]");
        await Assert.That(cut.Find("[data-status-dot]").ClassList).Contains("zw-status-dot");
        await Assert.That(cut.Find("[data-status-label]").TextContent).IsEqualTo(state.ToString().ToUpperInvariant());
    }

    [Test]
    [Arguments(HostStatus.Online, "zw-status-running", "Online")]
    [Arguments(HostStatus.Unreachable, "zw-status-unknown", "Unreachable")]
    [Arguments(HostStatus.Revoked, "zw-status-unhealthy", "Revoked")]
    [Arguments(HostStatus.Disabled, "zw-status-stopped", "Disabled")]
    public async Task Badge_maps_host_status_to_a_tinted_tone(HostStatus host, string toneClass, string label)
    {
        using BunitContext ctx = new();

        var cut = ctx.Render<StatusBadge>(p => p.Add(c => c.Host, host));

        var badge = cut.Find("[data-status-badge]");
        await Assert.That(badge.ClassList).Contains("zw-status");
        await Assert.That(badge.ClassList).Contains(toneClass);
        await Assert.That(badge.ClassList).DoesNotContain("w-[112px]"); // a host chip sizes to its label
        await Assert.That(cut.Find("[data-status-label]").TextContent).IsEqualTo(label);
    }

    [Test]
    public async Task Text_overrides_the_label_and_keeps_the_tone()
    {
        using BunitContext ctx = new();

        var cut = ctx.Render<StatusBadge>(p => p
            .Add(c => c.State, ServerRunState.Starting)
            .Add(c => c.Text, "RESTARTING"));

        await Assert.That(cut.Find("[data-status-badge]").ClassList).Contains("zw-status-busy");
        await Assert.That(cut.Find("[data-status-label]").TextContent).IsEqualTo("RESTARTING");
    }

    [Test]
    public async Task Unmatched_attributes_pass_through_to_the_chip()
    {
        using BunitContext ctx = new();

        var cut = ctx.Render<StatusBadge>(p => p
            .Add(c => c.Host, HostStatus.Revoked)
            .AddUnmatched("data-host-trust", true));

        await Assert.That(cut.Find("[data-status-badge]").HasAttribute("data-host-trust")).IsTrue();
    }
}
