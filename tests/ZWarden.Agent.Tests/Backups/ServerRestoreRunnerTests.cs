using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ZWarden.Agent.Backups;
using ZWarden.Agent.Configuration;
using ZWarden.Agent.ControlPlane;
using ZWarden.Agent.Docker;
using ZWarden.Domain.Ids;

namespace ZWarden.Agent.Tests.Backups;

/// <summary>
/// F25 (ADR 0029): the <see cref="ServerRestoreRunner"/> restores a Server's world from a backup, safely. It
/// re-verifies the archive against the recorded SHA-256 and <b>refuses a mismatch before touching the world</b>;
/// refuses while the container is running; takes an inline <b>protective</b> backup of the current world; unpacks
/// into a staging tree and <b>atomically swaps</b> it in; refuses an empty or unsafe archive without destroying the
/// live world; and restores onto a never-provisioned host for disaster recovery.
/// </summary>
public class ServerRestoreRunnerTests
{
    [Test]
    public async Task RunAsync_restores_the_world_and_takes_a_protective_backup()
    {
        using var temp = new TempRoot();
        ServerId serverId = ServerId.New();
        (string archiveName, string sha) = StageArchive(temp, serverId, "new.bin", "restored-world", "world-src.tar.gz");
        string world = WorldDir(temp, serverId);
        Directory.CreateDirectory(world);
        File.WriteAllText(Path.Combine(world, "old.bin"), "stale-world");

        ServerRestoreOutcome outcome = await Runner(temp)
            .RunAsync(serverId, OperationId.New(), archiveName, sha, NullOperationProgressReporter.Instance, CancellationToken.None);

        await Assert.That(outcome.Succeeded).IsTrue();
        await Assert.That(outcome.RestoredArchiveName).IsEqualTo(archiveName);
        // The live world is now the archive's content — the stale file is gone.
        await Assert.That(File.ReadAllText(Path.Combine(world, "new.bin"))).IsEqualTo("restored-world");
        await Assert.That(File.Exists(Path.Combine(world, "old.bin"))).IsFalse();
        // A protective backup of the pre-restore world was written and its facts reported.
        await Assert.That(outcome.ProtectiveArchiveName!).EndsWith("-pre-restore.tar.gz");
        await Assert.That(outcome.ProtectiveSha256!.Length).IsEqualTo(64);
        string protectivePath = Path.Combine(temp.BackupRoot, serverId.ToString(), outcome.ProtectiveArchiveName!);
        await Assert.That(File.Exists(protectivePath)).IsTrue();
        // The protective archive holds the OLD world, so a mistaken restore can be undone.
        string check = Path.Combine(temp.Root, "check");
        new TarGzRestoreArchiveExtractor().Extract(protectivePath, check, CancellationToken.None);
        await Assert.That(File.ReadAllText(Path.Combine(check, "old.bin"))).IsEqualTo("stale-world");
    }

    [Test]
    public async Task RunAsync_refuses_a_checksum_mismatch_without_touching_the_world()
    {
        using var temp = new TempRoot();
        ServerId serverId = ServerId.New();
        (string archiveName, _) = StageArchive(temp, serverId, "new.bin", "restored-world", "world-src.tar.gz");
        string world = WorldDir(temp, serverId);
        Directory.CreateDirectory(world);
        File.WriteAllText(Path.Combine(world, "old.bin"), "stale-world");

        ServerRestoreOutcome outcome = await Runner(temp)
            .RunAsync(serverId, OperationId.New(), archiveName, "not-the-real-checksum", NullOperationProgressReporter.Instance, CancellationToken.None);

        await Assert.That(outcome.Succeeded).IsFalse();
        await Assert.That(outcome.FailureReason!).Contains("checksum");
        // The world is untouched and no protective backup was taken (the refusal is before any mutation).
        await Assert.That(File.ReadAllText(Path.Combine(world, "old.bin"))).IsEqualTo("stale-world");
        await Assert.That(File.Exists(Path.Combine(world, "new.bin"))).IsFalse();
        await Assert.That(Directory.EnumerateFiles(Path.Combine(temp.BackupRoot, serverId.ToString()), "*-pre-restore.tar.gz").Any()).IsFalse();
    }

    [Test]
    public async Task RunAsync_refuses_while_the_container_is_running()
    {
        using var temp = new TempRoot();
        ServerId serverId = ServerId.New();
        (string archiveName, string sha) = StageArchive(temp, serverId, "new.bin", "restored-world", "world-src.tar.gz");
        string world = WorldDir(temp, serverId);
        Directory.CreateDirectory(world);
        File.WriteAllText(Path.Combine(world, "old.bin"), "live");
        var runtime = new StubContainerRuntime(new ManagedContainer("c1", serverId, "running"));

        ServerRestoreOutcome outcome = await Runner(temp, runtime)
            .RunAsync(serverId, OperationId.New(), archiveName, sha, NullOperationProgressReporter.Instance, CancellationToken.None);

        await Assert.That(outcome.Succeeded).IsFalse();
        await Assert.That(outcome.FailureReason!).Contains("running");
        await Assert.That(File.ReadAllText(Path.Combine(world, "old.bin"))).IsEqualTo("live");
    }

    [Test]
    public async Task RunAsync_restores_over_a_stopped_container()
    {
        using var temp = new TempRoot();
        ServerId serverId = ServerId.New();
        (string archiveName, string sha) = StageArchive(temp, serverId, "new.bin", "restored", "world-src.tar.gz");
        Directory.CreateDirectory(WorldDir(temp, serverId));
        var runtime = new StubContainerRuntime(new ManagedContainer("c1", serverId, "exited"));

        ServerRestoreOutcome outcome = await Runner(temp, runtime)
            .RunAsync(serverId, OperationId.New(), archiveName, sha, NullOperationProgressReporter.Instance, CancellationToken.None);

        await Assert.That(outcome.Succeeded).IsTrue();
        await Assert.That(File.ReadAllText(Path.Combine(WorldDir(temp, serverId), "new.bin"))).IsEqualTo("restored");
    }

    [Test]
    public async Task RunAsync_fails_when_the_archive_is_missing()
    {
        using var temp = new TempRoot();

        ServerRestoreOutcome outcome = await Runner(temp)
            .RunAsync(ServerId.New(), OperationId.New(), "gone.tar.gz", "abc", NullOperationProgressReporter.Instance, CancellationToken.None);

        await Assert.That(outcome.Succeeded).IsFalse();
        await Assert.That(outcome.FailureReason!).Contains("not found");
    }

    [Test]
    [Arguments("../escape.tar.gz")]
    [Arguments("sub/world.tar.gz")]
    [Arguments("")]
    public async Task RunAsync_refuses_a_non_bare_archive_name(string archiveName)
    {
        using var temp = new TempRoot();

        ServerRestoreOutcome outcome = await Runner(temp)
            .RunAsync(ServerId.New(), OperationId.New(), archiveName, "abc", NullOperationProgressReporter.Instance, CancellationToken.None);

        await Assert.That(outcome.Succeeded).IsFalse();
        await Assert.That(outcome.FailureReason!).Contains("bare file name");
    }

    [Test]
    public async Task RunAsync_restores_onto_a_host_with_no_existing_world()
    {
        using var temp = new TempRoot();
        ServerId serverId = ServerId.New();
        (string archiveName, string sha) = StageArchive(temp, serverId, "new.bin", "fresh", "world-src.tar.gz");
        // No world directory exists — disaster recovery onto a never-started host.

        ServerRestoreOutcome outcome = await Runner(temp)
            .RunAsync(serverId, OperationId.New(), archiveName, sha, NullOperationProgressReporter.Instance, CancellationToken.None);

        await Assert.That(outcome.Succeeded).IsTrue();
        await Assert.That(File.ReadAllText(Path.Combine(WorldDir(temp, serverId), "new.bin"))).IsEqualTo("fresh");
        await Assert.That(outcome.ProtectiveArchiveName!).EndsWith("-pre-restore.tar.gz");
    }

    [Test]
    public async Task RunAsync_refuses_an_empty_archive_and_keeps_the_live_world()
    {
        using var temp = new TempRoot();
        ServerId serverId = ServerId.New();
        // An archive of an empty world tree — zero files.
        (string archiveName, string sha) = StageArchiveOfEmptyWorld(temp, serverId, "empty.tar.gz");
        string world = WorldDir(temp, serverId);
        Directory.CreateDirectory(world);
        File.WriteAllText(Path.Combine(world, "old.bin"), "precious");

        ServerRestoreOutcome outcome = await Runner(temp)
            .RunAsync(serverId, OperationId.New(), archiveName, sha, NullOperationProgressReporter.Instance, CancellationToken.None);

        await Assert.That(outcome.Succeeded).IsFalse();
        await Assert.That(outcome.FailureReason!).Contains("no world data");
        // The live world survived the refusal.
        await Assert.That(File.ReadAllText(Path.Combine(world, "old.bin"))).IsEqualTo("precious");
    }

    [Test]
    public async Task RunAsync_refuses_an_unsafe_archive_and_keeps_the_live_world()
    {
        using var temp = new TempRoot();
        ServerId serverId = ServerId.New();
        string archivePath = Path.Combine(temp.BackupRoot, serverId.ToString(), "evil.tar.gz");
        Directory.CreateDirectory(Path.GetDirectoryName(archivePath)!);
        using (FileStream file = File.Create(archivePath))
        using (GZipStream gzip = new(file, CompressionLevel.Optimal))
        using (TarWriter tar = new(gzip, TarEntryFormat.Pax, leaveOpen: false))
        {
            var entry = new PaxTarEntry(TarEntryType.RegularFile, "../escape.bin")
            {
                DataStream = new MemoryStream(Encoding.UTF8.GetBytes("pwned")),
            };
            tar.WriteEntry(entry);
        }

        string sha = Sha256OfFile(archivePath);
        string world = WorldDir(temp, serverId);
        Directory.CreateDirectory(world);
        File.WriteAllText(Path.Combine(world, "old.bin"), "precious");

        ServerRestoreOutcome outcome = await Runner(temp)
            .RunAsync(serverId, OperationId.New(), "evil.tar.gz", sha, NullOperationProgressReporter.Instance, CancellationToken.None);

        await Assert.That(outcome.Succeeded).IsFalse();
        await Assert.That(outcome.FailureReason!).Contains("unsafe");
        await Assert.That(File.ReadAllText(Path.Combine(world, "old.bin"))).IsEqualTo("precious");
        await Assert.That(File.Exists(Path.Combine(temp.DataMountRoot, "escape.bin"))).IsFalse();
    }

    // ---------------------------------------------------------------------------------------------------
    // F40 release gate — the disaster-recovery drill (PRD §59). The individual mechanics are covered above;
    // these compose the real F24 backup runner with the real F25 restore runner end to end: back up a live
    // world, destroy it, restore it, and prove the recovery is exact and integrity-checked.
    // ---------------------------------------------------------------------------------------------------

    [Test]
    public async Task DR_backup_then_wipe_then_restore_recovers_the_world_byte_for_byte()
    {
        using var temp = new TempRoot();
        ServerId serverId = ServerId.New();
        string world = WorldDir(temp, serverId);

        // A realistic world tree: files at the root and nested under subdirectories.
        Directory.CreateDirectory(Path.Combine(world, "Sandbox", "map"));
        File.WriteAllText(Path.Combine(world, "players.db"), "player-state-0xDEADBEEF");
        File.WriteAllText(Path.Combine(world, "Sandbox", "options.ini"), "PVP=false\nZombies=insane");
        File.WriteAllText(Path.Combine(world, "Sandbox", "map", "chunk_0_0.bin"), "world-chunk-payload");
        Dictionary<string, string> original = SnapshotTree(world);

        // Back up the live world through the real runner (F24).
        ServerBackupOutcome backup = await BackupRunner(temp).RunAsync(serverId, OperationId.New(), CancellationToken.None);
        await Assert.That(backup.Succeeded).IsTrue();

        // Disaster: the entire world tree is destroyed.
        Directory.Delete(world, recursive: true);

        // Restore from the backup through the real runner (F25).
        ServerRestoreOutcome outcome = await Runner(temp).RunAsync(
            serverId, OperationId.New(), backup.ArchiveName!, backup.Sha256!, NullOperationProgressReporter.Instance, CancellationToken.None);
        await Assert.That(outcome.Succeeded).IsTrue();

        // The recovered world is byte-for-byte identical to the original — same files, same content, nothing extra.
        Dictionary<string, string> recovered = SnapshotTree(world);
        await Assert.That(recovered.Count).IsEqualTo(original.Count);
        foreach ((string relativePath, string hash) in original)
        {
            await Assert.That(recovered.ContainsKey(relativePath)).IsTrue();
            await Assert.That(recovered[relativePath]).IsEqualTo(hash);
        }
    }

    [Test]
    public async Task DR_restore_from_a_corrupted_backup_is_refused_and_the_live_world_survives()
    {
        using var temp = new TempRoot();
        ServerId serverId = ServerId.New();
        string world = WorldDir(temp, serverId);
        Directory.CreateDirectory(world);
        File.WriteAllText(Path.Combine(world, "players.db"), "live-and-precious");

        ServerBackupOutcome backup = await BackupRunner(temp).RunAsync(serverId, OperationId.New(), CancellationToken.None);
        await Assert.That(backup.Succeeded).IsTrue();

        // Bit-rot / tampering after the checksum was recorded: flip a byte in the stored archive.
        string archivePath = Path.Combine(temp.BackupRoot, serverId.ToString(), backup.ArchiveName!);
        byte[] bytes = await File.ReadAllBytesAsync(archivePath);
        bytes[^1] ^= 0xFF;
        await File.WriteAllBytesAsync(archivePath, bytes);

        // Restoring with the originally-recorded checksum must refuse before touching the world.
        ServerRestoreOutcome outcome = await Runner(temp).RunAsync(
            serverId, OperationId.New(), backup.ArchiveName!, backup.Sha256!, NullOperationProgressReporter.Instance, CancellationToken.None);

        await Assert.That(outcome.Succeeded).IsFalse();
        await Assert.That(outcome.FailureReason!).Contains("checksum");
        await Assert.That(File.ReadAllText(Path.Combine(world, "players.db"))).IsEqualTo("live-and-precious");
    }

    private static ServerBackupRunner BackupRunner(TempRoot temp) =>
        new(
            new TarGzBackupArchiver(),
            Options.Create(new AgentOptions { DataMountRoot = temp.DataMountRoot, BackupRoot = temp.BackupRoot }),
            TimeProvider.System,
            NullLogger<ServerBackupRunner>.Instance);

    // A content fingerprint of a world tree: each file's path relative to the tree root, mapped to the
    // lowercase-hex SHA-256 of its bytes. Two trees with the same map are byte-for-byte identical.
    private static Dictionary<string, string> SnapshotTree(string root)
    {
        Dictionary<string, string> map = new(StringComparer.Ordinal);
        foreach (string path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(root, path).Replace('\\', '/');
            map[relative] = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
        }

        return map;
    }

    private static ServerRestoreRunner Runner(TempRoot temp, IContainerRuntime? runtime = null) =>
        new(
            runtime ?? new StubContainerRuntime(),
            new TarGzBackupArchiver(),
            new TarGzRestoreArchiveExtractor(),
            Options.Create(new AgentOptions { DataMountRoot = temp.DataMountRoot, BackupRoot = temp.BackupRoot }),
            TimeProvider.System,
            NullLogger<ServerRestoreRunner>.Instance);

    private static string WorldDir(TempRoot temp, ServerId serverId) =>
        Path.Combine(temp.DataMountRoot, serverId.ToString());

    // Archives a one-file world into <BackupRoot>/<serverId>/<archiveName>, returning the name and the checksum the
    // control plane would have recorded — exactly what the runner re-verifies.
    private static (string Name, string Sha) StageArchive(
        TempRoot temp, ServerId serverId, string fileName, string content, string archiveName)
    {
        string source = Path.Combine(temp.Root, $"src-{Guid.NewGuid():N}");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, fileName), content);
        string destination = Path.Combine(temp.BackupRoot, serverId.ToString());
        BackupArchiveResult result = new TarGzBackupArchiver().Create(source, destination, archiveName, CancellationToken.None);
        return (archiveName, result.Sha256);
    }

    private static (string Name, string Sha) StageArchiveOfEmptyWorld(TempRoot temp, ServerId serverId, string archiveName)
    {
        string source = Path.Combine(temp.Root, $"empty-{Guid.NewGuid():N}");
        Directory.CreateDirectory(source);
        string destination = Path.Combine(temp.BackupRoot, serverId.ToString());
        BackupArchiveResult result = new TarGzBackupArchiver().Create(source, destination, archiveName, CancellationToken.None);
        return (archiveName, result.Sha256);
    }

    private static string Sha256OfFile(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    private sealed class StubContainerRuntime(params ManagedContainer[] managed) : IContainerRuntime
    {
        public Task<IReadOnlyList<ManagedContainer>> ListManagedAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ManagedContainer>>(managed);

        public Task<DockerHealth> ProbeHealthAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<HostMemory> ReadHostMemoryAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<ObservedContainer>> InspectManagedAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<PortAllocation> ClaimRequestedPortsAsync(int gamePort, ServerId forServer, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ServerContainer?> InspectServerAsync(ServerId serverId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task RemoveAsync(ServerId serverId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<PortAllocation> AllocateNextPortsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<string> CreateAsync(PzContainerSpec spec, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task StartAsync(string containerId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task StopAsync(string containerId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task RestartAsync(string containerId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task StartAsync(ServerId serverId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task StopAsync(ServerId serverId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task RestartAsync(ServerId serverId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<string> ReadServerLogsAsync(ServerId serverId, DateTimeOffset? since, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<string?> ResolveNetworkAddressAsync(ServerId serverId, string networkName, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task FollowServerLogsAsync(ServerId serverId, int tailLines, Func<ContainerLogFrame, CancellationToken, ValueTask> onFrame, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class TempRoot : IDisposable
    {
        public TempRoot()
        {
            Root = Path.Combine(Path.GetTempPath(), $"zw-restore-run-{Guid.NewGuid():N}");
            DataMountRoot = Path.Combine(Root, "servers");
            BackupRoot = Path.Combine(Root, "backups");
            Directory.CreateDirectory(DataMountRoot);
            Directory.CreateDirectory(BackupRoot);
        }

        public string Root { get; }

        public string DataMountRoot { get; }

        public string BackupRoot { get; }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Root))
                {
                    Directory.Delete(Root, recursive: true);
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
