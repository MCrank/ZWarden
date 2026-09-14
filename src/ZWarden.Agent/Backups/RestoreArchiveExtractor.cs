using System.Formats.Tar;
using System.IO.Compression;

namespace ZWarden.Agent.Backups;

/// <summary>Thrown when a backup archive contains an entry a restore refuses to unpack (F25): a path that escapes
/// the restore directory, a symlink or hard link, or an unsupported entry type. Even though ZWarden authored the
/// archive (F24), the extractor validates every entry as defense in depth (ADR 0029) — a tampered archive can never
/// write outside the staging tree.</summary>
public sealed class RestoreArchiveException : Exception
{
    public RestoreArchiveException(string message)
        : base(message)
    {
    }
}

/// <summary>What a restore extraction produced (F25): how many regular files were written. A real world backup
/// always has files; a zero-file extraction is how the runner detects an empty or garbage archive before it swaps
/// anything over the live world.</summary>
/// <param name="FileCount">The number of regular files written into the destination.</param>
public sealed record RestoreExtractionResult(int FileCount);

/// <summary>
/// Unpacks a backup <c>.tar.gz</c> into a destination tree (F25, ADR 0029). Pure host-side I/O — the Agent owns the
/// directories, so a restore never uses <c>exec</c> or <c>docker cp</c> (ADR 0008). Every entry is validated before
/// it is written: an entry whose resolved path escapes the destination, a symlink/hard link, or any non-file entry
/// type is <b>refused</b> (a <see cref="RestoreArchiveException"/>), so a tampered archive cannot traverse out of the
/// staging tree. Abstracted so the runner is unit-tested and the tar/gzip choice stays in one place — the mirror of
/// <see cref="IBackupArchiver"/>.
/// </summary>
public interface IRestoreArchiveExtractor
{
    /// <summary>Extracts <paramref name="archivePath"/> (a <c>.tar.gz</c>) into <paramref name="destinationDirectory"/>,
    /// reconstructing the directory structure from the entry paths, and returns the file count. The destination is
    /// created if missing. Throws <see cref="RestoreArchiveException"/> on any unsafe entry.</summary>
    RestoreExtractionResult Extract(string archivePath, string destinationDirectory, CancellationToken cancellationToken);
}

/// <inheritdoc cref="IRestoreArchiveExtractor" />
public sealed class TarGzRestoreArchiveExtractor : IRestoreArchiveExtractor
{
    /// <inheritdoc />
    public RestoreExtractionResult Extract(
        string archivePath, string destinationDirectory, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);

        Directory.CreateDirectory(destinationDirectory);
        string root = Path.GetFullPath(destinationDirectory);
        int fileCount = 0;

        using FileStream file = File.OpenRead(archivePath);
        using GZipStream gzip = new(file, CompressionMode.Decompress);
        using TarReader reader = new(gzip);

        while (true)
        {
            TarEntry? entry;
            try
            {
                entry = reader.GetNextEntry();
            }
            catch (EndOfStreamException)
            {
                // A backup of an empty world tree is a zero-entry tar (no end-of-archive block), so the very first
                // read hits the end of the stream. Treat it as a clean, empty archive — the runner verifies the
                // checksum before extracting, so a *truncated* (corrupt) archive is caught upstream, not here.
                break;
            }

            if (entry is null)
            {
                break;
            }

            cancellationToken.ThrowIfCancellationRequested();

            // Extended-attribute metadata entries carry no path of their own — skip them.
            if (entry.EntryType is TarEntryType.GlobalExtendedAttributes or TarEntryType.ExtendedAttributes)
            {
                continue;
            }

            // A link entry is never unpacked — following it could write outside the world tree (the same reason
            // the backup never *follows* a symlink, ADR 0028). Refuse the whole restore rather than skip it.
            if (entry.EntryType is TarEntryType.SymbolicLink or TarEntryType.HardLink)
            {
                throw new RestoreArchiveException(
                    $"it contains a link entry ('{entry.Name}'), which is not allowed in a world backup.");
            }

            string target = Path.GetFullPath(Path.Combine(root, entry.Name));
            if (!IsUnderRoot(root, target))
            {
                // A rooted or "../" entry that resolves outside the destination — a path-traversal attempt.
                throw new RestoreArchiveException(
                    $"it contains an entry ('{entry.Name}') that escapes the restore directory.");
            }

            switch (entry.EntryType)
            {
                case TarEntryType.Directory:
                    Directory.CreateDirectory(target);
                    break;

                case TarEntryType.RegularFile:
                case TarEntryType.V7RegularFile:
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    entry.ExtractToFile(target, overwrite: true);
                    fileCount++;
                    break;

                default:
                    // Devices, FIFOs, and anything else have no place in a world tree — refuse rather than skip.
                    throw new RestoreArchiveException(
                        $"it contains an unsupported entry type ('{entry.EntryType}').");
            }
        }

        return new RestoreExtractionResult(fileCount);
    }

    // True when the resolved entry path stays at or under the destination root. Ordinal comparison is correct on the
    // Linux hosts the Agent runs on; the check is defense in depth over an archive ZWarden itself produced.
    private static bool IsUnderRoot(string root, string candidate)
    {
        if (string.Equals(candidate, root, StringComparison.Ordinal))
        {
            return true;
        }

        string prefix = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        return candidate.StartsWith(prefix, StringComparison.Ordinal);
    }
}
