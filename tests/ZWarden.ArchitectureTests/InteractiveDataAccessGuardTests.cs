namespace ZWarden.ArchitectureTests;

/// <summary>
/// #297 PR-B (ADR 0046 Q7): no component under <c>ZWarden.Web/Components</c> takes a <c>ZWardenDbContext</c> or its
/// factory. A component's DI scope is the circuit on an interactive page, so a context injected there would be
/// shared by every event and render (the #154 bug class). Components call Application services, through
/// <c>ActionScopeRunner</c> on an interactive page. A source-text scan, like <see cref="TenantFilterGuardTests"/>.
/// </summary>
public class InteractiveDataAccessGuardTests
{
    private static readonly string[] Forbidden = ["ZWardenDbContext", "IDbContextFactory"];

    [Test]
    public async Task No_component_takes_a_dbcontext_or_its_factory()
    {
        string components = Path.Combine(RepoRoot(), "src", "ZWarden.Web", "Components");
        List<string> violations = [];

        foreach (string file in Directory.EnumerateFiles(components, "*.*", SearchOption.AllDirectories)
                     .Where(f => f.EndsWith(".razor", StringComparison.Ordinal) || f.EndsWith(".cs", StringComparison.Ordinal)))
        {
            string code = string.Join('\n', (await File.ReadAllLinesAsync(file))
                .Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));
            foreach (string name in Forbidden.Where(n => code.Contains(n, StringComparison.Ordinal)))
            {
                violations.Add($"'{name}' in {Path.GetRelativePath(RepoRoot(), file)} - components call services (via ActionScopeRunner on an interactive page), never the context (#297).");
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
