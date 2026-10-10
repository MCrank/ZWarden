using ZWarden.Agent.Backups;
using ZWarden.Agent.ControlPlane;
using ZWarden.Agent.ServerConfig;
using ZWarden.Agent.Tests.Docker;
using ZWarden.Contracts.Protocol;
using ZWarden.Contracts.Protocol.Messages;
using ZWarden.Domain.Configuration;
using ZWarden.Domain.Ids;

namespace ZWarden.Agent.Tests.ControlPlane;

/// <summary>
/// #379: a world-changing command sent with <c>BackupFirst</c> takes an automatic backup before it changes anything.
/// A failed backup fails the command and nothing runs; a taken backup rides the completion whatever the command's
/// outcome; a server with no world yet runs the command without one; a command sent without the flag never backs up.
/// </summary>
public partial class AgentCommandProcessorTests
{
    private static readonly ConfigApply FlaggedApply =
        new(PzConfigFile.Ini, "base-1", [new ConfigValueEdit("MaxPlayers", ConfigValueKind.Number, "20")], BackupFirst: true);

    [Test]
    public async Task A_flagged_config_apply_backs_up_first_and_carries_the_backup()
    {
        List<string> journal = [];
        var backups = new FakeServerBackupRunner { Journal = journal };
        var writer = new FakeServerConfigWriter { Journal = journal };
        ServerId serverId = ServerId.New();

        Envelope<OperationCompleted>? reply = await Processor(backups: backups, configWriter: writer)
            .ProcessAsync(Json(FlaggedApply, OperationId.New(), serverId), CancellationToken.None);

        await Assert.That(reply!.Payload.Outcome).IsEqualTo(OperationOutcome.Succeeded);
        string[] expected = ["backup", "write"];
        await Assert.That(journal).IsEquivalentTo(expected, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(backups.LastServerId).IsEqualTo(serverId);
        await Assert.That(reply.Payload.PreOperationBackup!.ArchiveName).IsEqualTo("world-20261010-100000-op-x-pre-op.tar.gz");
        await Assert.That(reply.Payload.PreOperationBackup!.Sha256).IsEqualTo("feed01");
        await Assert.That(reply.Payload.Config).IsNotNull();
    }

    [Test]
    public async Task An_unflagged_config_apply_takes_no_backup()
    {
        var backups = new FakeServerBackupRunner();

        Envelope<OperationCompleted>? reply = await Processor(backups: backups)
            .ProcessAsync(Json(FlaggedApply with { BackupFirst = false }, OperationId.New(), ServerId.New()), CancellationToken.None);

        await Assert.That(reply!.Payload.Outcome).IsEqualTo(OperationOutcome.Succeeded);
        await Assert.That(backups.PreOperationCount).IsEqualTo(0);
        await Assert.That(reply.Payload.PreOperationBackup).IsNull();
    }

    [Test]
    public async Task A_failed_backup_fails_the_config_apply_and_writes_nothing()
    {
        var backups = new FakeServerBackupRunner
        {
            PreOperationOutcome = new(false, null, 0, null, null, "The backup could not be written: No space left on device"),
        };
        var writer = new FakeServerConfigWriter();

        Envelope<OperationCompleted>? reply = await Processor(backups: backups, configWriter: writer)
            .ProcessAsync(Json(FlaggedApply, OperationId.New(), ServerId.New()), CancellationToken.None);

        await Assert.That(reply!.Payload.Outcome).IsEqualTo(OperationOutcome.Failed);
        await Assert.That(reply.Payload.FailureReason).IsEqualTo(
            "No backup could be taken first: The backup could not be written: No space left on device. Nothing was changed.");
        await Assert.That(writer.ApplyCount).IsEqualTo(0);
        await Assert.That(reply.Payload.PreOperationBackup).IsNull();
    }

    [Test]
    public async Task A_server_with_no_world_yet_applies_without_a_backup()
    {
        var backups = new FakeServerBackupRunner { PreOperationOutcome = null };
        var writer = new FakeServerConfigWriter();

        Envelope<OperationCompleted>? reply = await Processor(backups: backups, configWriter: writer)
            .ProcessAsync(Json(FlaggedApply, OperationId.New(), ServerId.New()), CancellationToken.None);

        await Assert.That(reply!.Payload.Outcome).IsEqualTo(OperationOutcome.Succeeded);
        await Assert.That(backups.PreOperationCount).IsEqualTo(1);
        await Assert.That(writer.ApplyCount).IsEqualTo(1);
        await Assert.That(reply.Payload.PreOperationBackup).IsNull();
    }

    [Test]
    public async Task A_config_apply_that_fails_after_its_backup_still_carries_the_backup()
    {
        var writer = new FakeServerConfigWriter { Outcome = ConfigApplyOutcome.DriftRefused("The configuration on disk changed outside ZWarden.") };

        Envelope<OperationCompleted>? reply = await Processor(configWriter: writer)
            .ProcessAsync(Json(FlaggedApply, OperationId.New(), ServerId.New()), CancellationToken.None);

        await Assert.That(reply!.Payload.Outcome).IsEqualTo(OperationOutcome.Failed);
        await Assert.That(reply.Payload.FailureReason).Contains("changed outside ZWarden");
        await Assert.That(reply.Payload.PreOperationBackup).IsNotNull();
    }

    [Test]
    public async Task A_flagged_raw_apply_backs_up_after_taking_the_staged_text_and_before_writing()
    {
        var staging = new ServerConfigRawEditStaging(TimeProvider.System);
        foreach (ServerConfigRawEditChunk chunk in ServerConfigRawEditCodec.Encode("corr-1", "MaxPlayers=20\n"))
        {
            staging.Accept(chunk);
        }

        List<string> journal = [];
        var backups = new FakeServerBackupRunner { Journal = journal };
        var writer = new FakeServerConfigWriter { Journal = journal };

        Envelope<OperationCompleted>? reply = await Processor(backups: backups, configWriter: writer, rawStaging: staging)
            .ProcessAsync(Json(new ConfigApplyRaw(PzConfigFile.Ini, "b", "corr-1", BackupFirst: true), OperationId.New(), ServerId.New()), CancellationToken.None);

        await Assert.That(reply!.Payload.Outcome).IsEqualTo(OperationOutcome.Succeeded);
        string[] expected = ["backup", "write"];
        await Assert.That(journal).IsEquivalentTo(expected, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(reply.Payload.PreOperationBackup).IsNotNull();
    }

    [Test]
    public async Task A_raw_apply_whose_text_never_arrived_takes_no_backup()
    {
        var backups = new FakeServerBackupRunner();

        Envelope<OperationCompleted>? reply = await Processor(backups: backups)
            .ProcessAsync(Json(new ConfigApplyRaw(PzConfigFile.Ini, "b", "missing", BackupFirst: true), OperationId.New(), ServerId.New()), CancellationToken.None);

        await Assert.That(reply!.Payload.Outcome).IsEqualTo(OperationOutcome.Failed);
        await Assert.That(backups.PreOperationCount).IsEqualTo(0);
    }

    [Test]
    public async Task A_failed_backup_fails_the_raw_apply_and_writes_nothing()
    {
        var staging = new ServerConfigRawEditStaging(TimeProvider.System);
        foreach (ServerConfigRawEditChunk chunk in ServerConfigRawEditCodec.Encode("corr-2", "MaxPlayers=20\n"))
        {
            staging.Accept(chunk);
        }

        var backups = new FakeServerBackupRunner { PreOperationOutcome = new(false, null, 0, null, null, "disk full") };
        var writer = new FakeServerConfigWriter();

        Envelope<OperationCompleted>? reply = await Processor(backups: backups, configWriter: writer, rawStaging: staging)
            .ProcessAsync(Json(new ConfigApplyRaw(PzConfigFile.Ini, "b", "corr-2", BackupFirst: true), OperationId.New(), ServerId.New()), CancellationToken.None);

        await Assert.That(reply!.Payload.FailureReason).IsEqualTo("No backup could be taken first: disk full. Nothing was changed.");
        await Assert.That(writer.ApplyRawCount).IsEqualTo(0);
    }

    [Test]
    public async Task A_flagged_game_update_backs_up_first_and_carries_the_backup()
    {
        List<string> journal = [];
        var backups = new FakeServerBackupRunner { Journal = journal };
        var updates = new FakeServerUpdateRunner { Journal = journal };

        Envelope<OperationCompleted>? reply = await Processor(backups: backups, updates: updates)
            .ProcessAsync(Json(new UpdateServer(BackupFirst: true), OperationId.New(), ServerId.New()), CancellationToken.None);

        await Assert.That(reply!.Payload.Outcome).IsEqualTo(OperationOutcome.Succeeded);
        string[] expected = ["backup", "update"];
        await Assert.That(journal).IsEquivalentTo(expected, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(reply.Payload.PreOperationBackup).IsNotNull();
        await Assert.That(reply.Payload.Update).IsNotNull();
    }

    [Test]
    public async Task A_failed_backup_fails_the_game_update_before_it_starts()
    {
        var backups = new FakeServerBackupRunner { PreOperationOutcome = new(false, null, 0, null, null, "disk full") };
        var updates = new FakeServerUpdateRunner();

        Envelope<OperationCompleted>? reply = await Processor(backups: backups, updates: updates)
            .ProcessAsync(Json(new UpdateServer(BackupFirst: true), OperationId.New(), ServerId.New()), CancellationToken.None);

        await Assert.That(reply!.Payload.Outcome).IsEqualTo(OperationOutcome.Failed);
        await Assert.That(reply.Payload.FailureReason!).StartsWith("No backup could be taken first: disk full.");
        await Assert.That(updates.RunCount).IsEqualTo(0);
    }

    [Test]
    public async Task A_failed_game_update_after_its_backup_still_carries_the_backup()
    {
        var updates = new FakeServerUpdateRunner { Outcome = new(false, null, "Error! App '380870' state is 0x202.") };

        Envelope<OperationCompleted>? reply = await Processor(updates: updates)
            .ProcessAsync(Json(new UpdateServer(BackupFirst: true), OperationId.New(), ServerId.New()), CancellationToken.None);

        await Assert.That(reply!.Payload.Outcome).IsEqualTo(OperationOutcome.Failed);
        await Assert.That(reply.Payload.PreOperationBackup).IsNotNull();
    }

    [Test]
    public async Task A_mod_update_restart_backs_up_first_but_a_plain_restart_does_not()
    {
        var backups = new FakeServerBackupRunner();
        var runtime = new FakeContainerRuntime();
        ServerId serverId = ServerId.New();

        Envelope<OperationCompleted>? plain = await Processor(runtime, backups: backups)
            .ProcessAsync(Json(new RestartServer(), OperationId.New(), serverId), CancellationToken.None);
        await Assert.That(backups.PreOperationCount).IsEqualTo(0);
        await Assert.That(plain!.Payload.PreOperationBackup).IsNull();

        Envelope<OperationCompleted>? modUpdate = await Processor(runtime, backups: backups)
            .ProcessAsync(Json(new RestartServer(BackupFirst: true), OperationId.New(), serverId), CancellationToken.None);
        await Assert.That(modUpdate!.Payload.Outcome).IsEqualTo(OperationOutcome.Succeeded);
        await Assert.That(backups.PreOperationCount).IsEqualTo(1);
        await Assert.That(modUpdate.Payload.PreOperationBackup).IsNotNull();
        await Assert.That(runtime.RestartedServerId).IsEqualTo(serverId);
    }

    [Test]
    public async Task A_failed_backup_leaves_the_server_running_instead_of_restarting_it()
    {
        var backups = new FakeServerBackupRunner { PreOperationOutcome = new(false, null, 0, null, null, "disk full") };
        var coordinator = new FakeServerRestartCoordinator();

        Envelope<OperationCompleted>? reply = await Processor(backups: backups, restartCoordinator: coordinator)
            .ProcessAsync(Json(new RestartServer(BackupFirst: true), OperationId.New(), ServerId.New()), CancellationToken.None);

        await Assert.That(reply!.Payload.Outcome).IsEqualTo(OperationOutcome.Failed);
        await Assert.That(reply.Payload.FailureReason!).StartsWith("No backup could be taken first");
        await Assert.That(coordinator.RestartCount).IsEqualTo(0);
    }

    [Test]
    public async Task The_backup_reports_progress_and_keeps_the_lease_alive_while_it_runs()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var backups = new FakeServerBackupRunner { Gate = gate };
        var progress = new RecordingProgressReporter();

        Task<Envelope<OperationCompleted>?> running = Processor(backups: backups, backupHeartbeat: TimeSpan.FromMilliseconds(20))
            .ProcessAsync(Json(FlaggedApply, OperationId.New(), ServerId.New()), CancellationToken.None, progress);

        // The first line arrives at once, then a heartbeat repeats it while the archive is being written.
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (progress.Count < 3)
        {
            await Task.Delay(10, timeout.Token);
        }

        gate.SetResult();
        Envelope<OperationCompleted>? reply = await running;

        await Assert.That(reply!.Payload.Outcome).IsEqualTo(OperationOutcome.Succeeded);
        await Assert.That(progress.Lines[0]).IsEqualTo("Taking an automatic backup first…");
    }

    private sealed class RecordingProgressReporter : IOperationProgressReporter
    {
        private readonly Lock _gate = new();
        private readonly List<string> _lines = [];

        public int Count
        {
            get
            {
                lock (_gate)
                {
                    return _lines.Count;
                }
            }
        }

        public IReadOnlyList<string> Lines
        {
            get
            {
                lock (_gate)
                {
                    return [.. _lines];
                }
            }
        }

        public Task ReportAsync(OperationId operationId, int percentComplete, string? statusLine, CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                _lines.Add(statusLine ?? string.Empty);
            }

            return Task.CompletedTask;
        }
    }
}
