using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ZWarden.Agent.Backups;
using ZWarden.Agent.Configuration;
using ZWarden.Domain.Ids;

namespace ZWarden.Agent.Tests.Backups;

/// <summary>
/// F24: the <see cref="ServerBackupRunner"/> resolves the host paths from the Agent's configuration and drives the
/// archiver. The source is the Server's world tree (<c>&lt;DataMountRoot&gt;/&lt;serverId&gt;</c>); the destination
/// is <c>&lt;BackupRoot&gt;/&lt;serverId&gt;/</c>. A missing world directory fails without archiving; an archiver
/// I/O error is a failed outcome with a legible reason; success returns the archive's facts and a timestamped,
/// operation-traceable name.
/// </summary>
public class ServerBackupRunnerTests
{
    [Test]
    public async Task RunAsync_archives_the_world_tree_to_the_backup_root()
    {
        using var temp = new TempRoot();
        ServerId serverId = ServerId.New();
        Directory.CreateDirectory(Path.Combine(temp.DataMountRoot, serverId.ToString()));
        var archiver = new RecordingArchiver { Result = new BackupArchiveResult(2048, "beef") };
        OperationId operationId = OperationId.New();

        ServerBackupOutcome outcome = await Runner(temp, archiver).RunAsync(serverId, operationId, CancellationToken.None);

        await Assert.That(outcome.Succeeded).IsTrue();
        await Assert.That(outcome.SizeBytes).IsEqualTo(2048L);
        await Assert.That(outcome.Sha256).IsEqualTo("beef");
        await Assert.That(outcome.CreatedAt).IsNotNull();
        await Assert.That(archiver.SourceDirectory).IsEqualTo(Path.Combine(temp.DataMountRoot, serverId.ToString()));
        await Assert.That(archiver.DestinationDirectory).IsEqualTo(Path.Combine(temp.BackupRoot, serverId.ToString()));
        await Assert.That(outcome.ArchiveName!).StartsWith("world-");
        await Assert.That(outcome.ArchiveName!).EndsWith(".tar.gz");
        await Assert.That(outcome.ArchiveName!).Contains(operationId.ToString());
        await Assert.That(archiver.ArchiveName).IsEqualTo(outcome.ArchiveName);
    }

    [Test]
    public async Task RunPreOperationAsync_saves_then_archives_under_a_pre_op_name()
    {
        using var temp = new TempRoot();
        ServerId serverId = ServerId.New();
        Directory.CreateDirectory(Path.Combine(temp.DataMountRoot, serverId.ToString()));
        List<string> journal = [];
        var archiver = new RecordingArchiver { Result = new BackupArchiveResult(512, "cafe"), Journal = journal };
        var saver = new FakeWorldSaver { Result = WorldSaveResult.Saved, Journal = journal };
        OperationId operationId = OperationId.New();

        ServerBackupOutcome? outcome = await Runner(temp, archiver, saver)
            .RunPreOperationAsync(serverId, operationId, CancellationToken.None);

        await Assert.That(outcome).IsNotNull();
        await Assert.That(outcome!.Succeeded).IsTrue();
        await Assert.That(outcome.Sha256).IsEqualTo("cafe");
        await Assert.That(outcome.ArchiveName!).StartsWith("world-");
        await Assert.That(outcome.ArchiveName!).EndsWith($"-{operationId}-pre-op.tar.gz");
        await Assert.That(archiver.DestinationDirectory).IsEqualTo(Path.Combine(temp.BackupRoot, serverId.ToString()));
        string[] expected = ["save", "archive"];
        await Assert.That(journal).IsEquivalentTo(expected, TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    public async Task RunPreOperationAsync_takes_no_backup_when_the_server_has_no_world_yet()
    {
        using var temp = new TempRoot();
        var archiver = new RecordingArchiver();

        ServerBackupOutcome? outcome = await Runner(temp, archiver)
            .RunPreOperationAsync(ServerId.New(), OperationId.New(), CancellationToken.None);

        await Assert.That(outcome).IsNull();
        await Assert.That(archiver.CallCount).IsEqualTo(0);
    }

    [Test]
    public async Task RunPreOperationAsync_fails_with_the_archiver_reason()
    {
        using var temp = new TempRoot();
        ServerId serverId = ServerId.New();
        Directory.CreateDirectory(Path.Combine(temp.DataMountRoot, serverId.ToString()));
        var archiver = new RecordingArchiver { Throw = new IOException("No space left on device") };

        ServerBackupOutcome? outcome = await Runner(temp, archiver)
            .RunPreOperationAsync(serverId, OperationId.New(), CancellationToken.None);

        await Assert.That(outcome!.Succeeded).IsFalse();
        await Assert.That(outcome.FailureReason!).Contains("No space left on device");
    }

    [Test]
    public async Task RunAsync_fails_without_archiving_when_the_world_directory_is_missing()
    {
        using var temp = new TempRoot();
        var archiver = new RecordingArchiver();

        ServerBackupOutcome outcome = await Runner(temp, archiver)
            .RunAsync(ServerId.New(), OperationId.New(), CancellationToken.None);

        await Assert.That(outcome.Succeeded).IsFalse();
        await Assert.That(outcome.FailureReason!).Contains("data directory");
        await Assert.That(archiver.CallCount).IsEqualTo(0);
    }

    [Test]
    public async Task RunAsync_fails_with_a_legible_reason_when_the_archiver_errors()
    {
        using var temp = new TempRoot();
        ServerId serverId = ServerId.New();
        Directory.CreateDirectory(Path.Combine(temp.DataMountRoot, serverId.ToString()));
        var archiver = new RecordingArchiver { Throw = new IOException("disk full") };

        ServerBackupOutcome outcome = await Runner(temp, archiver).RunAsync(serverId, OperationId.New(), CancellationToken.None);

        await Assert.That(outcome.Succeeded).IsFalse();
        await Assert.That(outcome.FailureReason!).Contains("disk full");
        await Assert.That(outcome.Sha256).IsNull();
    }

    [Test]
    public async Task RunAsync_saves_a_running_world_before_archiving_it_and_carries_no_warning()
    {
        using var temp = new TempRoot();
        ServerId serverId = ServerId.New();
        Directory.CreateDirectory(Path.Combine(temp.DataMountRoot, serverId.ToString()));
        List<string> journal = [];
        var archiver = new RecordingArchiver { Journal = journal };
        var saver = new FakeWorldSaver { Result = WorldSaveResult.Saved, Journal = journal };

        ServerBackupOutcome outcome = await Runner(temp, archiver, saver).RunAsync(serverId, OperationId.New(), CancellationToken.None);

        await Assert.That(outcome.Succeeded).IsTrue();
        await Assert.That(outcome.Warning).IsNull();
        string[] expected = ["save", "archive"];
        await Assert.That(journal).IsEquivalentTo(expected, TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    public async Task RunAsync_archives_anyway_with_a_warning_when_the_world_could_not_be_saved()
    {
        using var temp = new TempRoot();
        ServerId serverId = ServerId.New();
        Directory.CreateDirectory(Path.Combine(temp.DataMountRoot, serverId.ToString()));
        var archiver = new RecordingArchiver();
        var saver = new FakeWorldSaver { Result = WorldSaveResult.Failed("the server's RCON port timed out") };

        ServerBackupOutcome outcome = await Runner(temp, archiver, saver).RunAsync(serverId, OperationId.New(), CancellationToken.None);

        await Assert.That(outcome.Succeeded).IsTrue();
        await Assert.That(archiver.CallCount).IsEqualTo(1);
        await Assert.That(outcome.Warning!).Contains("could not be saved before this backup");
        await Assert.That(outcome.Warning!).Contains("the server's RCON port timed out");
        await Assert.That(outcome.Warning!).Contains("most recent changes may be missing");
    }

    [Test]
    public async Task RunAsync_archives_a_stopped_world_without_a_warning()
    {
        using var temp = new TempRoot();
        ServerId serverId = ServerId.New();
        Directory.CreateDirectory(Path.Combine(temp.DataMountRoot, serverId.ToString()));
        var saver = new FakeWorldSaver { Result = WorldSaveResult.NotRunning };

        ServerBackupOutcome outcome = await Runner(temp, new RecordingArchiver(), saver)
            .RunAsync(serverId, OperationId.New(), CancellationToken.None);

        await Assert.That(outcome.Succeeded).IsTrue();
        await Assert.That(outcome.Warning).IsNull();
    }

    [Test]
    public async Task RunAsync_does_not_try_to_save_when_there_is_no_world_directory()
    {
        using var temp = new TempRoot();
        var saver = new FakeWorldSaver();

        await Runner(temp, new RecordingArchiver(), saver).RunAsync(ServerId.New(), OperationId.New(), CancellationToken.None);

        await Assert.That(saver.CallCount).IsEqualTo(0);
    }

    [Test]
    public async Task DeleteAsync_removes_the_named_archive_from_the_backup_directory()
    {
        using var temp = new TempRoot();
        ServerId serverId = ServerId.New();
        string dir = Path.Combine(temp.BackupRoot, serverId.ToString());
        Directory.CreateDirectory(dir);
        string archive = Path.Combine(dir, "world-1.tar.gz");
        File.WriteAllText(archive, "bytes");

        ServerBackupDeletionOutcome outcome = await Runner(temp, new RecordingArchiver())
            .DeleteAsync(serverId, "world-1.tar.gz", CancellationToken.None);

        await Assert.That(outcome.Succeeded).IsTrue();
        await Assert.That(File.Exists(archive)).IsFalse();
    }

    [Test]
    public async Task DeleteAsync_is_idempotent_when_the_archive_is_already_gone()
    {
        using var temp = new TempRoot();

        ServerBackupDeletionOutcome outcome = await Runner(temp, new RecordingArchiver())
            .DeleteAsync(ServerId.New(), "missing.tar.gz", CancellationToken.None);

        await Assert.That(outcome.Succeeded).IsTrue();
    }

    [Test]
    [Arguments("../escape.tar.gz")]
    [Arguments("sub/world.tar.gz")]
    [Arguments("")]
    public async Task DeleteAsync_refuses_a_non_bare_archive_name(string archiveName)
    {
        using var temp = new TempRoot();

        ServerBackupDeletionOutcome outcome = await Runner(temp, new RecordingArchiver())
            .DeleteAsync(ServerId.New(), archiveName, CancellationToken.None);

        await Assert.That(outcome.Succeeded).IsFalse();
        await Assert.That(outcome.FailureReason!).Contains("bare file name");
    }

    private static ServerBackupRunner Runner(TempRoot temp, IBackupArchiver archiver, IWorldSaver? saver = null) =>
        new(
            archiver,
            saver ?? new FakeWorldSaver(),
            Options.Create(new AgentOptions { DataMountRoot = temp.DataMountRoot, BackupRoot = temp.BackupRoot }),
            TimeProvider.System,
            NullLogger<ServerBackupRunner>.Instance);

    private sealed class RecordingArchiver : IBackupArchiver
    {
        public BackupArchiveResult Result { get; set; } = new(1, "aa");

        public Exception? Throw { get; set; }

        public int CallCount { get; private set; }

        public string? SourceDirectory { get; private set; }

        public string? DestinationDirectory { get; private set; }

        public string? ArchiveName { get; private set; }

        public List<string>? Journal { get; set; }

        public BackupArchiveResult Create(
            string sourceDirectory, string destinationDirectory, string archiveName, CancellationToken cancellationToken)
        {
            CallCount++;
            SourceDirectory = sourceDirectory;
            DestinationDirectory = destinationDirectory;
            ArchiveName = archiveName;
            Journal?.Add("archive");
            if (Throw is not null)
            {
                throw Throw;
            }

            return Result;
        }
    }

    private sealed class TempRoot : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), $"zw-bkp-run-{Guid.NewGuid():N}");

        public TempRoot()
        {
            DataMountRoot = Path.Combine(_root, "servers");
            BackupRoot = Path.Combine(_root, "backups");
            Directory.CreateDirectory(DataMountRoot);
            Directory.CreateDirectory(BackupRoot);
        }

        public string DataMountRoot { get; }

        public string BackupRoot { get; }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_root))
                {
                    Directory.Delete(_root, recursive: true);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
