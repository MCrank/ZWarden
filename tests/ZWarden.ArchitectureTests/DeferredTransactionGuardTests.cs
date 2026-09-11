namespace ZWarden.ArchitectureTests;

/// <summary>
/// ADR 0005 condition 3: a deferred transaction (or dropping past EF Core to a raw-ADO
/// transaction) is the one code path that reaches SQLite's unrescuable SQLITE_BUSY_SNAPSHOT under
/// write contention. This turns re-introducing it into a red build.
/// </summary>
public class DeferredTransactionGuardTests
{
    private static readonly string[] Forbidden =
    [
        "BeginTransaction(true",
        "BeginTransaction(deferred",
        "deferred: true",
        "deferred:true",
    ];

    [Test]
    public async Task Persistence_code_never_opens_a_deferred_transaction()
    {
        string infrastructure = Path.Combine(RepoRoot(), "src", "ZWarden.Infrastructure");
        List<string> violations = [];

        foreach (string file in Directory.EnumerateFiles(infrastructure, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                continue;
            }

            string text = await File.ReadAllTextAsync(file);
            foreach (string needle in Forbidden)
            {
                if (text.Contains(needle, StringComparison.Ordinal))
                {
                    violations.Add($"'{needle}' in {Path.GetFileName(file)}");
                }
            }
        }

        await Assert.That(violations).IsEmpty();
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
