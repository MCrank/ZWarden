namespace ZWarden.ArchitectureTests;

/// <summary>
/// #368 (ADR 0049): moving a Server or a backup to another Host is how an Agent comes to manage containers stamped
/// with another Agent's id, so it happens on one path only. <c>ReassignTo</c> is called from
/// <c>HostReplacementService</c> and nowhere else in <c>src/</c> (its definitions in the Domain aside). Source-text
/// scan, in the style of <see cref="ContainerCreationGuardTests"/>.
/// </summary>
public class HostReplacementGuardTests
{
    private static readonly string[] Allowed = ["HostReplacementService.cs", "Server.cs", "Backup.cs"];

    [Test]
    public async Task Only_the_host_replacement_service_reassigns_a_server_or_backup()
    {
        List<string> violations = [];
        foreach (string file in SourceFiles())
        {
            if (Allowed.Contains(Path.GetFileName(file), StringComparer.Ordinal))
            {
                continue;
            }

            foreach (string line in await File.ReadAllLinesAsync(file))
            {
                if (line.Contains(".ReassignTo(", StringComparison.Ordinal))
                {
                    violations.Add($"{Path.GetFileName(file)}: {line.Trim()} — only HostReplacementService moves a Server or backup (ADR 0049).");
                }
            }
        }

        await Assert.That(violations).IsEmpty();
    }

    private static IEnumerable<string> SourceFiles()
    {
        string src = Path.Combine(RepoRoot(), "src");
        foreach (string file in Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                continue;
            }

            yield return file;
        }
    }

    private static string RepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ZWarden.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Could not locate the repository root (ZWarden.slnx).");
    }
}
