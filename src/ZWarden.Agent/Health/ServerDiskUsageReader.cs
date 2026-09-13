namespace ZWarden.Agent.Health;

/// <summary>
/// The default <see cref="IServerDiskUsageReader"/> (F16). Usage is the summed length of every file under the
/// data directory; capacity is the total size of the volume it sits on. Every filesystem call is guarded — a
/// missing directory, a vanished file mid-walk, or a denied read degrades to <c>null</c> for that figure, never
/// an exception.
/// </summary>
public sealed class ServerDiskUsageReader : IServerDiskUsageReader
{
    /// <inheritdoc />
    public DiskUsage Read(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        return new DiskUsage(UsedBytes(dataDirectory), CapacityBytes(dataDirectory));
    }

    private static long? UsedBytes(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return null;
        }

        try
        {
            long total = 0;
            foreach (string file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            {
                try
                {
                    total += new FileInfo(file).Length;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // A file vanished or is unreadable mid-walk; skip it rather than abandon the whole total.
                }
            }

            return total;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static long? CapacityBytes(string directory)
    {
        try
        {
            string? root = Path.GetPathRoot(Path.GetFullPath(directory));
            if (string.IsNullOrEmpty(root))
            {
                return null;
            }

            DriveInfo drive = new(root);
            return drive.IsReady ? drive.TotalSize : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }
}
