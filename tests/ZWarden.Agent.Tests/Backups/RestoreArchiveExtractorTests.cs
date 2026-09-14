using System.Formats.Tar;
using System.IO.Compression;
using System.Text;
using ZWarden.Agent.Backups;

namespace ZWarden.Agent.Tests.Backups;

/// <summary>
/// F25 (ADR 0029): the <see cref="TarGzRestoreArchiveExtractor"/> unpacks a backup <c>.tar.gz</c> into a staging
/// tree using pure host I/O, reconstructing directories from the entry paths — and <b>refuses</b> any entry that
/// would escape the destination (a rooted or <c>..</c> path), any symlink/hard link, or any non-file entry type,
/// so a tampered archive can never traverse out of the staging tree. It round-trips an archive the F24 archiver
/// produced and reports the file count restore uses as its health check.
/// </summary>
public class RestoreArchiveExtractorTests
{
    [Test]
    public async Task Extract_round_trips_an_archive_the_backup_archiver_produced()
    {
        using var temp = new TempTree();
        string source = temp.Dir("world");
        File.WriteAllText(Path.Combine(source, "servertest.ini"), "PublicName=Survivors");
        string saves = temp.Dir(Path.Combine("world", "Saves", "Multiplayer"));
        File.WriteAllText(Path.Combine(saves, "map_meta.bin"), "chunkdata");
        string backups = temp.Dir("backups");
        new TarGzBackupArchiver().Create(source, backups, "world.tar.gz", CancellationToken.None);

        string destination = Path.Combine(temp.Root, "staging");
        RestoreExtractionResult result = new TarGzRestoreArchiveExtractor()
            .Extract(Path.Combine(backups, "world.tar.gz"), destination, CancellationToken.None);

        await Assert.That(result.FileCount).IsEqualTo(2);
        await Assert.That(File.ReadAllText(Path.Combine(destination, "servertest.ini"))).IsEqualTo("PublicName=Survivors");
        await Assert.That(File.ReadAllText(Path.Combine(destination, "Saves", "Multiplayer", "map_meta.bin"))).IsEqualTo("chunkdata");
    }

    [Test]
    public async Task Extract_reports_zero_files_for_an_empty_archive()
    {
        using var temp = new TempTree();
        string source = temp.Dir("world");
        string backups = temp.Dir("backups");
        new TarGzBackupArchiver().Create(source, backups, "empty.tar.gz", CancellationToken.None);

        RestoreExtractionResult result = new TarGzRestoreArchiveExtractor()
            .Extract(Path.Combine(backups, "empty.tar.gz"), Path.Combine(temp.Root, "staging"), CancellationToken.None);

        await Assert.That(result.FileCount).IsEqualTo(0);
    }

    [Test]
    [Arguments("../escape.bin")]
    [Arguments("../../etc/evil")]
    [Arguments("sub/../../escape.bin")]
    public async Task Extract_refuses_an_entry_that_escapes_the_destination(string entryName)
    {
        using var temp = new TempTree();
        string archive = WriteArchive(temp, tar =>
        {
            var entry = new PaxTarEntry(TarEntryType.RegularFile, entryName)
            {
                DataStream = new MemoryStream(Encoding.UTF8.GetBytes("pwned")),
            };
            tar.WriteEntry(entry);
        });

        await Assert.That(() => new TarGzRestoreArchiveExtractor()
                .Extract(archive, Path.Combine(temp.Root, "staging"), CancellationToken.None))
            .Throws<RestoreArchiveException>();

        // Nothing must have been written outside (or inside) the destination.
        await Assert.That(File.Exists(Path.Combine(temp.Root, "escape.bin"))).IsFalse();
    }

    [Test]
    public async Task Extract_refuses_a_symlink_entry()
    {
        using var temp = new TempTree();
        string archive = WriteArchive(temp, tar =>
        {
            var link = new PaxTarEntry(TarEntryType.SymbolicLink, "workshop") { LinkName = "/var/lib/steam" };
            tar.WriteEntry(link);
        });

        await Assert.That(() => new TarGzRestoreArchiveExtractor()
                .Extract(archive, Path.Combine(temp.Root, "staging"), CancellationToken.None))
            .Throws<RestoreArchiveException>();
    }

    [Test]
    public async Task Extract_refuses_a_hard_link_entry()
    {
        using var temp = new TempTree();
        string archive = WriteArchive(temp, tar =>
        {
            var link = new PaxTarEntry(TarEntryType.HardLink, "alias.bin") { LinkName = "real.bin" };
            tar.WriteEntry(link);
        });

        await Assert.That(() => new TarGzRestoreArchiveExtractor()
                .Extract(archive, Path.Combine(temp.Root, "staging"), CancellationToken.None))
            .Throws<RestoreArchiveException>();
    }

    private static string WriteArchive(TempTree temp, Action<TarWriter> write)
    {
        string backups = temp.Dir("backups");
        string archivePath = Path.Combine(backups, "crafted.tar.gz");
        using (FileStream file = File.Create(archivePath))
        using (GZipStream gzip = new(file, CompressionLevel.Optimal))
        using (TarWriter tar = new(gzip, TarEntryFormat.Pax, leaveOpen: false))
        {
            write(tar);
        }

        return archivePath;
    }

    private sealed class TempTree : IDisposable
    {
        public TempTree() => Root = Path.Combine(Path.GetTempPath(), $"zw-restore-ex-{Guid.NewGuid():N}");

        public string Root { get; }

        public string Dir(string relative)
        {
            string path = Path.Combine(Root, relative);
            Directory.CreateDirectory(path);
            return path;
        }

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
