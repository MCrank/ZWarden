using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;

namespace ZWarden.Agent.Backups;

/// <summary>The facts a produced backup archive carries (F24): its size and integrity checksum. The archive path is
/// the caller's; this reports only what F25 will re-verify.</summary>
/// <param name="SizeBytes">The produced <c>.tar.gz</c>'s size in bytes.</param>
/// <param name="Sha256">The lowercase-hex SHA-256 over the produced archive bytes.</param>
public sealed record BackupArchiveResult(long SizeBytes, string Sha256);

/// <summary>
/// Writes a Server's world tree into a compressed, checksummed archive (F24, ADR 0028). Pure host-side I/O — the
/// Agent owns the directories, so a backup never uses <c>exec</c> or <c>docker cp</c> (ADR 0008). Abstracted so the
/// runner is unit-tested and so the tar/gzip choice stays in one place.
/// </summary>
public interface IBackupArchiver
{
    /// <summary>Archives <paramref name="sourceDirectory"/> (which must exist) into
    /// <c>&lt;destinationDirectory&gt;/&lt;archiveName&gt;</c> as a <c>.tar.gz</c>, and returns the produced size and
    /// checksum. The write is atomic (a temp file in the destination dir, then a rename), so a crash never leaves a
    /// half-archive at the final name. <b>Symlinks are never followed</b> — the PZ <c>data/workshop</c> symlink
    /// points into ephemeral runtime storage, not world data. The SteamCMD install is excluded by construction: it
    /// is a host sibling of the source, not under it.</summary>
    BackupArchiveResult Create(
        string sourceDirectory, string destinationDirectory, string archiveName, CancellationToken cancellationToken);
}

/// <inheritdoc cref="IBackupArchiver" />
public sealed class TarGzBackupArchiver : IBackupArchiver
{
    /// <inheritdoc />
    public BackupArchiveResult Create(
        string sourceDirectory, string destinationDirectory, string archiveName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(archiveName);

        Directory.CreateDirectory(destinationDirectory);
        string finalPath = Path.Combine(destinationDirectory, archiveName);
        string tempPath = Path.Combine(destinationDirectory, $"{archiveName}.tmp-{Guid.NewGuid():N}");

        try
        {
            DirectoryInfo source = new(sourceDirectory);
            using (FileStream fileStream = File.Create(tempPath))
            using (GZipStream gzip = new(fileStream, CompressionLevel.Optimal))
            using (TarWriter tar = new(gzip, TarEntryFormat.Pax, leaveOpen: false))
            {
                WriteTree(tar, source, source.FullName, cancellationToken);
            }

            long size = new FileInfo(tempPath).Length;
            string checksum;
            using (FileStream produced = File.OpenRead(tempPath))
            {
                checksum = Convert.ToHexStringLower(SHA256.HashData(produced));
            }

            // Atomic publish: only a fully written, hashed archive ever appears at the final name.
            File.Move(tempPath, finalPath, overwrite: true);
            return new BackupArchiveResult(size, checksum);
        }
        catch
        {
            TryDeleteTemp(tempPath);
            throw;
        }
    }

    // Depth-first walk that writes one tar entry per regular file, path-relative to the source root with forward
    // slashes. A symlink (file or directory) is never entered or recorded — following data/workshop would pull in
    // ephemeral Workshop content that is not world data (ADR 0028). Directory structure is reconstructed on restore
    // from the file paths, so empty directories are not preserved (world saves never rely on them).
    private static void WriteTree(TarWriter tar, DirectoryInfo directory, string sourceRoot, CancellationToken cancellationToken)
    {
        foreach (FileSystemInfo entry in directory.EnumerateFileSystemInfos())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (entry.LinkTarget is not null)
            {
                continue; // Never follow a symlink (e.g. data/workshop → ephemeral runtime storage).
            }

            switch (entry)
            {
                case DirectoryInfo subdirectory:
                    WriteTree(tar, subdirectory, sourceRoot, cancellationToken);
                    break;

                case FileInfo file:
                    string entryName = Path.GetRelativePath(sourceRoot, file.FullName).Replace('\\', '/');
                    tar.WriteEntry(file.FullName, entryName);
                    break;
            }
        }
    }

    private static void TryDeleteTemp(string tempPath)
    {
        try
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
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
