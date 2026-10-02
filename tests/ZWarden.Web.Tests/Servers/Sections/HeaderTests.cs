using Bunit;
using Microsoft.Extensions.DependencyInjection;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Operations;
using ZWarden.Infrastructure.Persistence;
using ZWarden.Infrastructure.Tenancy;
using ZWarden.Web.Components.Pages.Servers;

namespace ZWarden.Web.Tests.Servers.Sections;

/// <summary>
/// The Server Detail header as circuit handlers (#299; formerly the static lifecycle and countdown-restart form posts,
/// F15/#114/#213/#273): each button runs the real lifecycle service, which enqueues its Operation, and the header
/// re-reads its status so it already says what's in flight. The last failure (#266) can be dismissed.
/// </summary>
public sealed class HeaderTests
{
    [Test]
    public async Task Start_enqueues_a_mutating_start_server_operation()
    {
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync();
        ServerId serverId = await harness.SeedServerAsync("startable");
        IRenderedComponent<ServerDetail> cut = harness.Render(serverId);

        await cut.Find("[data-action=server-start]").ClickAsync(new());

        cut.WaitForState(() => harness.FirstOperation(serverId, OperationKind.StartServer) is not null);
        await Assert.That(harness.FirstOperation(serverId, OperationKind.StartServer)!.IsMutating).IsTrue();
        cut.WaitForState(() => cut.Find("[data-lifecycle-message]").TextContent.Contains("Start enqueued", StringComparison.Ordinal));
    }

    [Test]
    public async Task Restart_enqueues_a_restart_server_operation()
    {
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync();
        ServerId serverId = await harness.SeedServerAsync("restartable-header");
        await harness.SetRunStateAsync(serverId, ZWarden.Domain.Servers.ServerRunState.Running);
        IRenderedComponent<ServerDetail> cut = harness.Render(serverId);

        await cut.Find("[data-action=server-restart]").ClickAsync(new());

        cut.WaitForState(() => harness.FirstOperation(serverId, OperationKind.RestartServer) is not null);
    }

    [Test]
    public async Task Update_game_enqueues_an_update_and_the_header_says_updating()
    {
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync();
        ServerId serverId = await harness.SeedServerAsync("update-header");
        await harness.SetRunStateAsync(serverId, ZWarden.Domain.Servers.ServerRunState.Stopped);
        IRenderedComponent<ServerDetail> cut = harness.Render(serverId);

        await cut.Find("[data-action=server-update]").ClickAsync(new());

        cut.WaitForState(() => cut.Markup.Contains("Game update enqueued", StringComparison.Ordinal));
        await Assert.That(harness.FirstOperation(serverId, OperationKind.UpdateServer)!.IsMutating).IsTrue();
        await Assert.That(cut.Markup).Contains("UPDATING");
        await Assert.That(cut.Find("[data-action=server-start]").HasAttribute("disabled")).IsTrue();
    }

    [Test]
    public async Task The_countdown_restart_carries_the_message_and_the_default_five_minute_plan()
    {
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync();
        ServerId serverId = await harness.SeedServerAsync("graceful-enqueue");
        await harness.SetRunStateAsync(serverId, ZWarden.Domain.Servers.ServerRunState.Running);
        IRenderedComponent<ServerDetail> cut = harness.Render(serverId);

        await InteractivePageHarness.TypeAsync(cut, "graceful-message", "Scheduled maintenance.");
        await cut.Find("[data-graceful-restart] form").SubmitAsync();

        cut.WaitForState(() => harness.FirstOperation(serverId, OperationKind.RestartServer) is not null);
        string payload = harness.FirstOperation(serverId, OperationKind.RestartServer)!.CommandPayload!;
        await Assert.That(payload).Contains("Scheduled maintenance.");
        await Assert.That(payload).Contains("300");
    }

    [Test]
    public async Task The_immediate_countdown_restarts_with_no_warning()
    {
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync();
        ServerId serverId = await harness.SeedServerAsync("graceful-immediate");
        await harness.SetRunStateAsync(serverId, ZWarden.Domain.Servers.ServerRunState.Running);
        IRenderedComponent<ServerDetail> cut = harness.Render(serverId);

        await cut.Find("#graceful-countdown").ChangeAsync(new() { Value = "immediate" });
        await cut.Find("[data-graceful-restart] form").SubmitAsync();

        cut.WaitForState(() => harness.FirstOperation(serverId, OperationKind.RestartServer) is not null);
        string payload = harness.FirstOperation(serverId, OperationKind.RestartServer)!.CommandPayload!;
        await Assert.That(payload).Contains("\"warningLeadSeconds\":[]");
        await Assert.That(payload).DoesNotContain("300");
    }

    [Test]
    public async Task Dismissing_the_last_failure_hides_it_and_remembers_it_in_the_browser()
    {
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync();
        ServerId serverId = await harness.SeedServerAsync("refused");
        await using (AsyncServiceScope scope = harness.Factory.Services.CreateSystemScope())
        {
            ZWardenDbContext db = scope.ServiceProvider.GetRequiredService<ZWardenDbContext>();
            Operation op = Operation.Enqueue(AgentId.New(), OperationKind.RecreateServer, isMutating: true, "k", DateTimeOffset.UtcNow, serverId);
            op.MarkDispatched(DateTimeOffset.UtcNow.AddMinutes(5), DateTimeOffset.UtcNow);
            op.Fail("Host port 16261/udp is already published.", DateTimeOffset.UtcNow);
            db.Add(op);
            await db.SaveChangesAsync();
        }

        IRenderedComponent<ServerDetail> cut = harness.Render(serverId);
        await Assert.That(cut.Find("[data-last-failure-reason]").TextContent).Contains("16261/udp");

        await cut.Find("[data-last-failure-dismiss]").ClickAsync(new());

        await Assert.That(cut.FindAll("[data-last-failure]")).IsEmpty();
        await Assert.That(harness.Context.JSInterop.Invocations.Any(i => i.Identifier == "dismiss")).IsTrue();
    }
}
