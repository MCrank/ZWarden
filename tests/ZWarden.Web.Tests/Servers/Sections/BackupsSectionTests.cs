using Bunit;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Operations;
using ZWarden.Web.Components.Pages.Servers;

namespace ZWarden.Web.Tests.Servers.Sections;

/// <summary>The Backups section's take / delete / restore as circuit handlers (#299; formerly static form posts,
/// F24/F25). Each runs through the real backup or restore service and enqueues its Operation.</summary>
public sealed class BackupsSectionTests
{
    [Test]
    public async Task Take_backup_enqueues_a_mutating_manual_backup()
    {
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync();
        ServerId serverId = await harness.SeedServerAsync("backup-takeable");
        IRenderedComponent<ServerDetail> cut = harness.Render(serverId, "backups");

        await cut.Find("[data-action=backup-create]").ClickAsync(new());

        cut.WaitForState(() => harness.FirstOperation(serverId, OperationKind.Backup) is not null);
        Operation op = harness.FirstOperation(serverId, OperationKind.Backup)!;
        await Assert.That(op.IsMutating).IsTrue();
        await Assert.That(op.CommandPayload).Contains("Manual");
        await Assert.That(cut.Find("[data-backup-message]").TextContent).Contains("Backup enqueued");
    }

    [Test]
    public async Task Delete_enqueues_a_non_mutating_delete_of_that_archive()
    {
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync();
        ServerId serverId = await harness.SeedServerAsync("backup-deletable");
        await harness.SeedBackupAsync(serverId, "world-1.tar.gz");
        IRenderedComponent<ServerDetail> cut = harness.Render(serverId, "backups");

        await cut.Find("[data-backup-row] [data-action=backup-delete]").ClickAsync(new());

        cut.WaitForState(() => harness.FirstOperation(serverId, OperationKind.DeleteBackup) is not null);
        Operation op = harness.FirstOperation(serverId, OperationKind.DeleteBackup)!;
        await Assert.That(op.IsMutating).IsFalse();
        await Assert.That(op.CommandPayload).Contains("world-1.tar.gz");
    }

    [Test]
    public async Task Restore_enqueues_a_mutating_restore_of_that_archive()
    {
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync();
        ServerId serverId = await harness.SeedServerAsync("backup-restorable");
        await harness.SeedBackupAsync(serverId, "world-1.tar.gz");
        IRenderedComponent<ServerDetail> cut = harness.Render(serverId, "backups");

        await cut.Find("[data-backup-row] [data-action=backup-restore]").ClickAsync(new());

        cut.WaitForState(() => harness.FirstOperation(serverId, OperationKind.Restore) is not null);
        Operation op = harness.FirstOperation(serverId, OperationKind.Restore)!;
        await Assert.That(op.IsMutating).IsTrue();
        await Assert.That(op.CommandPayload).Contains("world-1.tar.gz");
        await Assert.That(op.CommandPayload).Contains("abc123");
    }

    [Test]
    public async Task A_backup_that_could_not_save_first_shows_its_warning()
    {
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync();
        ServerId serverId = await harness.SeedServerAsync("backup-unsaved");
        await harness.SeedBackupAsync(serverId, "world-1.tar.gz", warning: "The world could not be saved before this backup.");
        IRenderedComponent<ServerDetail> cut = harness.Render(serverId, "backups");

        AngleSharp.Dom.IElement badge = cut.Find("[data-backup-row] [data-backup-warning]");
        await Assert.That(badge.TextContent).Contains("Not saved first");
        await Assert.That(badge.GetAttribute("title")).IsEqualTo("The world could not be saved before this backup.");
    }

    [Test]
    public async Task A_backup_without_a_warning_shows_no_badge()
    {
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync();
        ServerId serverId = await harness.SeedServerAsync("backup-saved");
        await harness.SeedBackupAsync(serverId, "world-1.tar.gz");
        IRenderedComponent<ServerDetail> cut = harness.Render(serverId, "backups");

        await Assert.That(cut.FindAll("[data-backup-warning]").Count).IsEqualTo(0);
    }
}
