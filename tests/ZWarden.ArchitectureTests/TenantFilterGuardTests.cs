namespace ZWarden.ArchitectureTests;

/// <summary>
/// The trust-boundaries §9 rule-4 guard, turned into a red build (ADR 0016): the tenant filter must
/// never be silently defeated. <c>IgnoreQueryFilters()</c> is the documented escape hatch that would
/// return another tenant's rows past the filter, so it is forbidden in <c>src</c> outside a single
/// sanctioned tenant-administration seam. A source-text scan, in the style of
/// <see cref="SecretHandlingGuardTests"/>. (The companion assertion - that every tenant-owned entity
/// actually carries a filter - is model-level and lives in the Infrastructure tests.)
/// </summary>
public class TenantFilterGuardTests
{
    // Paths (relative to src, using the platform separator) where an unscoped read is deliberately
    // allowed. Empty in F3A: nothing yet reads a tenant-owned table unscoped. F3D's tenant
    // administration will add its own file here when it needs one.
    private static readonly string[] SanctionedUnscopedSeams = [];

    [Test]
    public async Task No_source_bypasses_the_tenant_filter_with_ignore_query_filters()
    {
        List<string> violations = [];

        foreach (string file in SourceFiles("*.cs"))
        {
            string relative = RelativeToSrc(file);
            if (SanctionedUnscopedSeams.Any(seam => relative.Contains(seam, StringComparison.Ordinal)))
            {
                continue;
            }

            // The call form ".IgnoreQueryFilters(" - not a bare mention in a doc comment, which is how
            // the repository documents the rule it upholds.
            string text = await File.ReadAllTextAsync(file);
            if (text.Contains(".IgnoreQueryFilters(", StringComparison.Ordinal))
            {
                violations.Add($"'.IgnoreQueryFilters(' in {relative} - it defeats the tenant filter (ADR 0016); route the read through a tenant-scoped repository.");
            }
        }

        await Assert.That(violations).IsEmpty();
    }

    private static IEnumerable<string> SourceFiles(string pattern)
    {
        string src = Path.Combine(RepoRoot(), "src");
        foreach (string file in Directory.EnumerateFiles(src, pattern, SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                continue;
            }

            yield return file;
        }
    }

    private static string RelativeToSrc(string file) =>
        Path.GetRelativePath(Path.Combine(RepoRoot(), "src"), file);

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
