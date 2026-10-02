using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using ZWarden.Application.Servers;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Operations;
using ZWarden.Web.Components.Pages.Servers;

namespace ZWarden.Web.Tests.Servers.Sections;

/// <summary>
/// The Overview section's container settings (Recreate, #229/#230) and Delete (#271) as circuit handlers (#299;
/// formerly static form posts and the dialog.js confirmation). The lifecycle service re-checks every action; the
/// alert dialog only arms Delete once the exact name is typed.
/// </summary>
public sealed class OverviewSectionTests
{
    private const long GiB = 1024L * 1024 * 1024;

    [Test]
    public async Task Recreate_carries_the_new_port_and_the_chosen_countdown()
    {
        await using ServerDetailHarness harness = await ServerDetailHarness.StartAsync();
        ServerId serverId = await harness.SeedServerAsync("move-me", gamePort: 16261, queryPort: 16262);
        IRenderedComponent<ServerDetail> cut = harness.Render(serverId);

        await ServerDetailHarness.TypeAsync(cut, "recreate-port", "27015");
        await cut.Find("#recreate-countdown").ChangeAsync(new() { Value = "1m" });
        await cut.Find("[data-recreate] form").SubmitAsync();

        cut.WaitForState(() => harness.Payload(serverId, OperationKind.RecreateServer) is not null);
        ServerContainerPayload parsed = ServerContainerPayload.FromJson(harness.Payload(serverId, OperationKind.RecreateServer)!);
        await Assert.That(parsed.GamePort).IsEqualTo(27015);
        await Assert.That(parsed.Plan!.WarningLeadSeconds).IsEquivalentTo([60, 30, 10]);
    }

    [Test]
    public async Task Recreate_with_a_new_heap_keeps_the_current_port()
    {
        await using ServerDetailHarness harness = await ServerDetailHarness.StartAsync();
        ServerId serverId = await harness.SeedServerAsync("grow-me", gamePort: 16261, queryPort: 16262);
        IRenderedComponent<ServerDetail> cut = harness.Render(serverId);

        await ServerDetailHarness.TypeAsync(cut, "recreate-heap", "8");
        await cut.Find("[data-recreate] form").SubmitAsync();

        cut.WaitForState(() => harness.Payload(serverId, OperationKind.RecreateServer) is not null);
        ServerContainerPayload parsed = ServerContainerPayload.FromJson(harness.Payload(serverId, OperationKind.RecreateServer)!);
        await Assert.That(parsed.HeapSizeBytes).IsEqualTo(8 * GiB);
        await Assert.That(parsed.GamePort).IsNull();
    }

    [Test]
    public async Task A_heap_past_the_hosts_free_memory_warns_and_recreates_only_once_acknowledged()
    {
        // 16 GiB host, 2 reserved, 20 committed: this server's own 10 GiB (4 + 6) counts as released ⇒ 4 GiB free, so
        // an 8 GiB heap (14 GiB limit) is 10 short.
        await using ServerDetailHarness harness = await ServerDetailHarness.StartAsync();
        ServerId serverId = await harness.SeedServerAsync("grow-big", gamePort: 16261, queryPort: 16262);
        harness.SeedHostCapacity(serverId, 16 * GiB, 20 * GiB, 6 * GiB, 4 * GiB, 2 * GiB);
        IRenderedComponent<ServerDetail> cut = harness.Render(serverId);
        await Assert.That(cut.Markup).Contains("4 GiB free on this host for this server");

        await ServerDetailHarness.TypeAsync(cut, "recreate-heap", "8");
        await cut.Find("#recreate-countdown").ChangeAsync(new() { Value = "1m" });
        await cut.Find("[data-recreate] form").SubmitAsync();

        cut.WaitForState(() => cut.FindAll("[data-recreate-overcommit-warning]").Count == 1);
        await Assert.That(cut.Markup).Contains("10 GiB short");
        await Assert.That(harness.FirstOperation(serverId, OperationKind.RecreateServer)).IsNull();
        // The typed values stay in the circuit, so the acknowledged resubmit keeps the chosen countdown.
        await Assert.That(cut.Find("#recreate-countdown").GetAttribute("value")).IsEqualTo("1m");

        await cut.Find("#recreate-acknowledge").ClickAsync(new());
        await cut.Find("[data-recreate] form").SubmitAsync();

        cut.WaitForState(() => harness.Payload(serverId, OperationKind.RecreateServer) is not null);
        ServerContainerPayload parsed = ServerContainerPayload.FromJson(harness.Payload(serverId, OperationKind.RecreateServer)!);
        await Assert.That(parsed.HeapSizeBytes).IsEqualTo(8 * GiB);
        await Assert.That(parsed.Plan!.WarningLeadSeconds).IsEquivalentTo([60, 30, 10]);
    }

    [Test]
    public async Task An_invalid_port_is_refused_in_the_header_without_enqueueing()
    {
        await using ServerDetailHarness harness = await ServerDetailHarness.StartAsync();
        ServerId serverId = await harness.SeedServerAsync("bad-move", gamePort: 16261, queryPort: 16262);
        IRenderedComponent<ServerDetail> cut = harness.Render(serverId);

        await ServerDetailHarness.TypeAsync(cut, "recreate-port", "80");
        await cut.Find("[data-recreate] form").SubmitAsync();

        cut.WaitForState(() => cut.FindAll("[data-lifecycle-message]").Count == 1);
        await Assert.That(cut.Find("[data-lifecycle-message]").TextContent).Contains("between 1024 and 65534");
        await Assert.That(harness.FirstOperation(serverId, OperationKind.RecreateServer)).IsNull();
    }

    [Test]
    public async Task The_delete_dialog_names_the_server_and_arms_only_on_the_exact_name()
    {
        await using ServerDetailHarness harness = await ServerDetailHarness.StartAsync();
        ServerId serverId = await harness.SeedServerAsync("doomed", gamePort: 16261, queryPort: 16262);
        IRenderedComponent<ServerDetail> cut = harness.Render(serverId);

        await cut.Find("[data-action=delete-open]").ClickAsync(new());

        cut.WaitForState(() => cut.FindAll("[data-delete-dialog]").Count == 1);
        string dialog = cut.Find("[data-delete-dialog]").TextContent;
        await Assert.That(dialog).Contains("Delete doomed?");
        await Assert.That(dialog).Contains("World data and backups are kept");
        await Assert.That(dialog).Contains("can't be undone");
        await Assert.That(cut.Find("[data-action=delete]").HasAttribute("disabled")).IsTrue();

        await ServerDetailHarness.TypeAsync(cut, "delete-confirm", "Doomed");
        await Assert.That(cut.Find("[data-action=delete]").HasAttribute("disabled")).IsTrue();

        await ServerDetailHarness.TypeAsync(cut, "delete-confirm", "doomed");
        await Assert.That(cut.Find("[data-action=delete]").HasAttribute("disabled")).IsFalse();
    }

    [Test]
    public async Task Deleting_with_the_servers_name_enqueues_the_delete_and_returns_to_the_fleet()
    {
        await using ServerDetailHarness harness = await ServerDetailHarness.StartAsync();
        ServerId serverId = await harness.SeedServerAsync("doomed", gamePort: 16261, queryPort: 16262);
        await harness.SetRunStateAsync(serverId, ZWarden.Domain.Servers.ServerRunState.Running);
        IRenderedComponent<ServerDetail> cut = harness.Render(serverId);

        await cut.Find("[data-action=delete-open]").ClickAsync(new());
        cut.WaitForState(() => cut.FindAll("[data-delete-dialog]").Count == 1);
        await cut.Find("#delete-countdown").ChangeAsync(new() { Value = "1m" });
        await ServerDetailHarness.TypeAsync(cut, "delete-confirm", "doomed");
        await cut.Find("[data-action=delete]").ClickAsync(new());

        cut.WaitForState(() => harness.Payload(serverId, OperationKind.DeleteServer) is not null);
        await Assert.That(GracefulRestartPayload.FromJson(harness.Payload(serverId, OperationKind.DeleteServer)!).WarningLeadSeconds)
            .IsEquivalentTo([60, 30, 10]);
        NavigationManager navigation = harness.Context.Services.GetRequiredService<NavigationManager>();
        await Assert.That(new Uri(navigation.Uri).AbsolutePath).IsEqualTo("/servers");
    }
}
