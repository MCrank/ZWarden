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

    /// <summary>
    /// F11/ADR 0022: the operations engine claims the per-server lock row, commits, then works — it never
    /// holds a transaction across Agent work and never opens its own database connection (which is the only
    /// way to reach a raw-ADO or deferred transaction, ADR 0005 conditions 3 and 5). This pins that guarantee
    /// to the engine folder specifically, so a future move of the engine out of Infrastructure cannot silently
    /// drop the whole-Infrastructure guard above.
    /// </summary>
    [Test]
    public async Task The_operations_engine_opens_no_deferred_or_raw_connection()
    {
        string engine = Path.Combine(RepoRoot(), "src", "ZWarden.Infrastructure", "Operations");
        string[] forbidden = [.. Forbidden, "new SqliteConnection", "new NpgsqlConnection"];
        List<string> violations = [];

        foreach (string file in Directory.EnumerateFiles(engine, "*.cs", SearchOption.AllDirectories))
        {
            string text = await File.ReadAllTextAsync(file);
            foreach (string needle in forbidden)
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
