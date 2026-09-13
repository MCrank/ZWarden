using Bunit;
using ZWarden.Domain.Servers;
using ZWarden.Web.Components.Ui;

namespace ZWarden.Web.Tests.Servers;

/// <summary>
/// F14 S6: the owned <see cref="StatusBadge"/> maps the Domain's last-reported <see cref="ServerRunState"/>
/// onto the Signal <c>--status-*</c> ramp (style-guide.md). The class names are full literals so the Tailwind
/// content scan emits them (ADR 0003 condition 2); this asserts the mapping the dashboard relies on.
/// </summary>
public class StatusBadgeTests
{
    [Test]
    [Arguments(ServerRunState.Running, "bg-status-running", "border-status-running")]
    [Arguments(ServerRunState.Stopped, "bg-status-stopped", "border-status-stopped")]
    [Arguments(ServerRunState.Starting, "bg-status-busy", "border-status-busy")]
    [Arguments(ServerRunState.Stopping, "bg-status-busy", "border-status-busy")]
    [Arguments(ServerRunState.Failed, "bg-status-unhealthy", "border-status-unhealthy")]
    [Arguments(ServerRunState.Unknown, "bg-status-unknown", "border-status-unknown")]
    public async Task Badge_maps_run_state_to_the_status_ramp(ServerRunState state, string dotClass, string borderClass)
    {
        using BunitContext ctx = new();

        var cut = ctx.Render<StatusBadge>(p => p.Add(c => c.State, state));

        string markup = cut.Markup;
        await Assert.That(markup).Contains(dotClass);
        await Assert.That(markup).Contains(borderClass);
        await Assert.That(cut.Markup).Contains(state.ToString().ToUpperInvariant());
    }
}
