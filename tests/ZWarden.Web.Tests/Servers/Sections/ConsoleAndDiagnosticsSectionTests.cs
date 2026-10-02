using Bunit;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Operations;
using ZWarden.Web.Components.Pages.Servers;

namespace ZWarden.Web.Tests.Servers.Sections;

/// <summary>The Console (F28) and Diagnostics (F29) sections' actions as circuit handlers (#299; formerly static form
/// posts). Each runs through the real service; the console policy refuses blocked commands without enqueuing.</summary>
public sealed class ConsoleAndDiagnosticsSectionTests
{
    [Test]
    public async Task Run_enqueues_a_non_mutating_console_command_carrying_the_line()
    {
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync();
        ServerId serverId = await harness.SeedServerAsync("console-enqueue");
        IRenderedComponent<ServerDetail> cut = harness.Render(serverId, "console");

        await InteractivePageHarness.TypeAsync(cut, "console-input", "servermsg \"hello\"");
        await cut.Find("[data-console-card] form").SubmitAsync();

        cut.WaitForState(() => harness.FirstOperation(serverId, OperationKind.ExecuteConsoleCommand) is not null);
        Operation op = harness.FirstOperation(serverId, OperationKind.ExecuteConsoleCommand)!;
        await Assert.That(op.IsMutating).IsFalse();
        await Assert.That(op.CommandPayload).Contains("servermsg");
        await Assert.That(cut.Find("[data-console-message]").TextContent).Contains("Command enqueued");
    }

    [Test]
    public async Task A_policy_blocked_command_is_refused_without_enqueuing()
    {
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync();
        ServerId serverId = await harness.SeedServerAsync("console-denied");
        IRenderedComponent<ServerDetail> cut = harness.Render(serverId, "console");

        await InteractivePageHarness.TypeAsync(cut, "console-input", "setpassword \"bob\" \"pw\"");
        await cut.Find("[data-console-card] form").SubmitAsync();

        cut.WaitForState(() => cut.FindAll("[data-console-message]").Count == 1);
        await Assert.That(harness.FirstOperation(serverId, OperationKind.ExecuteConsoleCommand)).IsNull();
    }

    [Test]
    public async Task Run_diagnostics_enqueues_a_read_only_gather()
    {
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync();
        ServerId serverId = await harness.SeedServerAsync("diagnosable");
        IRenderedComponent<ServerDetail> cut = harness.Render(serverId, "diagnostics");

        await cut.Find("[data-action=run-diagnostics]").ClickAsync(new());

        cut.WaitForState(() => harness.FirstOperation(serverId, OperationKind.GatherServerDiagnostics) is not null);
        await Assert.That(harness.FirstOperation(serverId, OperationKind.GatherServerDiagnostics)!.IsMutating).IsFalse();
        await Assert.That(cut.Find("[data-diagnostics-message]").TextContent).Contains("Diagnostics gather started");
    }
}
