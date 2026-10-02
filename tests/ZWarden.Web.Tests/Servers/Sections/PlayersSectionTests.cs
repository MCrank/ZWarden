using Bunit;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Operations;

namespace ZWarden.Web.Tests.Servers.Sections;

/// <summary>
/// The Players section's actions as circuit handlers (#299; formerly static form posts, F19). Each click runs through
/// the real player-management service and enqueues its Operation; the service re-checks the per-server permission.
/// </summary>
public sealed class PlayersSectionTests
{
    [Test]
    public async Task Refresh_enqueues_a_list_players_operation()
    {
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync();
        ServerId serverId = await harness.SeedServerAsync("enumerable");
        IRenderedComponent<Web.Components.Pages.Servers.ServerDetail> cut = harness.Render(serverId, "players");

        await cut.Find("[data-action=refresh]").ClickAsync(new());

        cut.WaitForState(() => cut.FindAll("[data-player-message]") is [var message]
            && message.TextContent.Contains("Roster refresh started", StringComparison.Ordinal));
        await Assert.That(harness.FirstOperation(serverId, OperationKind.ListPlayers)).IsNotNull();
    }

    [Test]
    public async Task Kick_enqueues_a_kick_carrying_the_username()
    {
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync();
        ServerId serverId = await harness.SeedServerAsync("kickable");
        IRenderedComponent<Web.Components.Pages.Servers.ServerDetail> cut = harness.Render(serverId, "players");

        await InteractivePageHarness.TypeAsync(cut, "player-username", "Bob");
        await cut.Find("[data-action=kick]").ClickAsync(new());

        Operation? op = null;
        cut.WaitForAssertion(() => op = harness.FirstOperation(serverId, OperationKind.KickPlayer) ?? throw new InvalidOperationException("no kick yet"));
        await Assert.That(op!.IsMutating).IsFalse();
        await Assert.That(op.CommandPayload).Contains("Bob");
    }
}
