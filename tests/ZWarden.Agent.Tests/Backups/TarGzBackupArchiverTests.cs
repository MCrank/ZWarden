using System.Formats.Tar;
using System.IO.Compression;
using ZWarden.Agent.Backups;

namespace ZWarden.Agent.Tests.Backups;

/// <summary>
/// F24 (ADR 0028): the <see cref="TarGzBackupArchiver"/> writes a Server's world tree into a compressed,
/// checksummed <c>.tar.gz</c> using pure host I/O. It round-trips the tree, never reaches outside the source (so
/// the sibling SteamCMD install is excluded by construction), never follows a symlink (the <c>data/workshop</c>
/// link points at ephemeral storage), publishes atomically (no temp left behind), and emits a stable lowercase-hex
/// SHA-256 over the produced archive.
/// </summary>
public class TarGzBackupArchiverTests
{
    [Test]
    public async Task Create_round_trips_the_world_tree()
    {
        using var temp = new TempTree();
        string source = temp.Dir("world");
        File.WriteAllText(Path.Combine(source, "servertest.ini"), "PublicName=Survivors");
        string saves = temp.Dir(Path.Combine("world", "Saves", "Multiplayer"));
        File.WriteAllText(Path.Combine(saves, "map_meta.bin"), "chunkdata");
        string dest = temp.Dir("out");

        BackupArchiveResult result = new TarGzBackupArchiver().Create(source, dest, "world.tar.gz", CancellationToken.None);

        await Assert.That(result.SizeBytes).IsGreaterThan(0L);
        await Assert.That(result.Sha256.Length).IsEqualTo(64);
        await Assert.That(result.Sha256).IsEqualTo(result.Sha256.ToLowerInvariant());

        string extracted = temp.Dir("extracted");
        Extract(Path.Combine(dest, "world.tar.gz"), extracted);
        await Assert.That(File.ReadAllText(Path.Combine(extracted, "servertest.ini"))).IsEqualTo("PublicName=Survivors");
        await Assert.That(File.ReadAllText(Path.Combine(extracted, "Saves", "Multiplayer", "map_meta.bin"))).IsEqualTo("chunkdata");
    }

    [Test]
    public async Task Create_produces_a_stable_checksum_for_identical_content()
    {
        using var temp = new TempTree();
        string source = temp.Dir("world");
        File.WriteAllText(Path.Combine(source, "a.txt"), "same");
        string dest = temp.Dir("out");

        BackupArchiveResult first = new TarGzBackupArchiver().Create(source, dest, "one.tar.gz", CancellationToken.None);
        BackupArchiveResult second = new TarGzBackupArchiver().Create(source, dest, "two.tar.gz", CancellationToken.None);

        await Assert.That(second.Sha256).IsEqualTo(first.Sha256);
    }

    [Test]
    public async Task Create_excludes_the_sibling_install_directory()
    {
        using var temp = new TempTree();
        string source = temp.Dir("srv");
        File.WriteAllText(Path.Combine(source, "world.bin"), "world");
        // The SteamCMD install is a host *sibling* of the data dir (F17): <root>/<serverId>.server.
        string install = temp.Dir("srv.server");
        File.WriteAllText(Path.Combine(install, "pzserver"), "6.72GiB");
        string dest = temp.Dir("out");

        new TarGzBackupArchiver().Create(source, dest, "b.tar.gz", CancellationToken.None);

        string extracted = temp.Dir("extracted");
        Extract(Path.Combine(dest, "b.tar.gz"), extracted);
        await Assert.That(File.Exists(Path.Combine(extracted, "world.bin"))).IsTrue();
        await Assert.That(File.Exists(Path.Combine(extracted, "pzserver"))).IsFalse();
    }

    [Test]
    public async Task Create_does_not_follow_a_symlink()
    {
        using var temp = new TempTree();
        string source = temp.Dir("world");
        File.WriteAllText(Path.Combine(source, "real.bin"), "world");

        // data/workshop is a symlink to the Workshop cache on the install volume; it must not be chased into the archive.
        string outside = temp.Dir("steamroot");
        File.WriteAllText(Path.Combine(outside, "108600.bin"), "workshop content");
        try
        {
            Directory.CreateSymbolicLink(Path.Combine(source, "workshop"), outside);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Creating a symlink needs privilege on Windows; the Linux CI tier exercises this path.
            return;
        }

        string dest = temp.Dir("out");
        new TarGzBackupArchiver().Create(source, dest, "b.tar.gz", CancellationToken.None);

        string extracted = temp.Dir("extracted");
        Extract(Path.Combine(dest, "b.tar.gz"), extracted);
        await Assert.That(File.Exists(Path.Combine(extracted, "real.bin"))).IsTrue();
        await Assert.That(File.Exists(Path.Combine(extracted, "workshop", "108600.bin"))).IsFalse();
        await Assert.That(Directory.Exists(Path.Combine(extracted, "workshop"))).IsFalse();
    }

    [Test]
    public async Task Create_publishes_atomically_leaving_no_temp_file()
    {
        using var temp = new TempTree();
        string source = temp.Dir("world");
        File.WriteAllText(Path.Combine(source, "a.txt"), "x");
        string dest = temp.Dir("out");

        new TarGzBackupArchiver().Create(source, dest, "final.tar.gz", CancellationToken.None);

        await Assert.That(File.Exists(Path.Combine(dest, "final.tar.gz"))).IsTrue();
        await Assert.That(Directory.EnumerateFiles(dest, "*.tmp-*").Any()).IsFalse();
    }

    [Test]
    public async Task Create_archives_an_empty_world_without_crashing()
    {
        using var temp = new TempTree();
        string source = temp.Dir("world");
        string dest = temp.Dir("out");

        BackupArchiveResult result = new TarGzBackupArchiver().Create(source, dest, "empty.tar.gz", CancellationToken.None);

        await Assert.That(File.Exists(Path.Combine(dest, "empty.tar.gz"))).IsTrue();
        await Assert.That(result.Sha256.Length).IsEqualTo(64);
    }

    [Test]
    public async Task Create_archives_a_group_readable_password_file_and_restore_keeps_it_group_readable()
    {
        if (OperatingSystem.IsWindows())
        {
            return; // Unix file modes; the Linux CI tier exercises this path.
        }

        using var temp = new TempTree();
        string source = temp.Dir("world");
        string pwfile = Path.Combine(source, ".zwarden-adminpw");
        File.WriteAllText(pwfile, "admin-pw");
        // The PZ image writes it 0640 so the Agent (group 10000) can read it (#377).
        const UnixFileMode GroupReadable = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead;
        File.SetUnixFileMode(pwfile, GroupReadable);
        string dest = temp.Dir("out");

        new TarGzBackupArchiver().Create(source, dest, "pw.tar.gz", CancellationToken.None);

        TarEntry? entry = null;
        using (FileStream file = File.OpenRead(Path.Combine(dest, "pw.tar.gz")))
        using (GZipStream gzip = new(file, CompressionMode.Decompress))
        using (TarReader reader = new(gzip))
        {
            for (TarEntry? next = reader.GetNextEntry(); next is not null; next = reader.GetNextEntry())
            {
                if (next.Name == ".zwarden-adminpw")
                {
                    entry = next;
                }
            }
        }

        await Assert.That(entry).IsNotNull();
        await Assert.That(entry!.Mode).IsEqualTo(GroupReadable);

        // Restored by the Agent, the file stays readable by the PZ container through its group.
        string restored = temp.Dir("restored");
        new TarGzRestoreArchiveExtractor().Extract(Path.Combine(dest, "pw.tar.gz"), restored, CancellationToken.None);
        string restoredFile = Path.Combine(restored, ".zwarden-adminpw");
        await Assert.That(File.ReadAllText(restoredFile)).IsEqualTo("admin-pw");
        await Assert.That(File.GetUnixFileMode(restoredFile) & UnixFileMode.GroupRead).IsEqualTo(UnixFileMode.GroupRead);
        await Assert.That(File.GetUnixFileMode(restoredFile) & UnixFileMode.OtherRead).IsEqualTo(UnixFileMode.None);
    }

    private static void Extract(string archivePath, string destination)
    {
        Directory.CreateDirectory(destination);
        using FileStream file = File.OpenRead(archivePath);
        using GZipStream gzip = new(file, CompressionMode.Decompress);
        TarFile.ExtractToDirectory(gzip, destination, overwriteFiles: true);
    }

    private sealed class TempTree : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), $"zw-bkp-{Guid.NewGuid():N}");

        public string Dir(string relative)
        {
            string path = Path.Combine(_root, relative);
            Directory.CreateDirectory(path);
            return path;
        }

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
