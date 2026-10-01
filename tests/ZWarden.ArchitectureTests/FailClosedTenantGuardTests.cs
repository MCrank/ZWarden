namespace ZWarden.ArchitectureTests;

/// <summary>
/// #297 (ADR 0046 Q6): the tenant fails closed, and the default tenant is reached only through named paths. Two
/// source-text scans over <c>src</c>, in the style of <see cref="TenantFilterGuardTests"/>:
/// <list type="bullet">
/// <item><c>Tenant.DefaultId</c> is used only by the types that define or deliberately grant it;</item>
/// <item>DI scopes are opened only by <c>TenantScopes</c>, so every scope carries an explicit tenant.</item>
/// </list>
/// </summary>
public class FailClosedTenantGuardTests
{
    // Each entry is a named, reviewed use of the default tenant.
    private static readonly string[] DefaultTenantSeams =
    [
        Path.Combine("ZWarden.Domain", "Tenancy", "Tenant.cs"),                                 // defines it
        Path.Combine("ZWarden.Application", "Tenancy", "SingleTenantContext.cs"),               // session-less hosts
        Path.Combine("ZWarden.Infrastructure", "Tenancy", "TenantBootstrapper.cs"),             // seeds the row
        Path.Combine("ZWarden.Infrastructure", "Tenancy", "ClaimsPrincipalTenantContext.cs"),   // anonymous-request rule
        Path.Combine("ZWarden.Infrastructure", "Tenancy", "TenantScopes.cs"),                   // the system scope
    ];

    private static readonly string[] ScopeSeams =
    [
        Path.Combine("ZWarden.Infrastructure", "Tenancy", "TenantScopes.cs"),
    ];

    [Test]
    public async Task Only_named_seams_use_the_default_tenant()
    {
        List<string> violations = await ScanAsync(
            DefaultTenantSeams,
            text => StripComments(text).Contains("Tenant.DefaultId", StringComparison.Ordinal),
            relative => $"'Tenant.DefaultId' in {relative} - never default the tenant implicitly (#297); open a system scope with TenantScopes.CreateSystemScope() or add a reviewed seam here.");

        await Assert.That(violations).IsEmpty();
    }

    [Test]
    public async Task Only_tenant_scopes_open_a_di_scope()
    {
        List<string> violations = await ScanAsync(
            ScopeSeams,
            text =>
            {
                string code = StripComments(text);
                return code.Contains(".CreateScope(", StringComparison.Ordinal)
                    || code.Contains(".CreateAsyncScope(", StringComparison.Ordinal);
            },
            relative => $"a raw DI scope in {relative} - it would carry no tenant (#297); use TenantScopes.CreateTenantScope/CreateSystemScope.");

        await Assert.That(violations).IsEmpty();
    }

    private static async Task<List<string>> ScanAsync(string[] seams, Func<string, bool> violates, Func<string, string> describe)
    {
        List<string> violations = [];
        foreach (string file in SourceFiles())
        {
            string relative = Path.GetRelativePath(Path.Combine(RepoRoot(), "src"), file);
            if (seams.Contains(relative, StringComparer.Ordinal) || IsMigrationsProject(relative))
            {
                continue;
            }

            if (violates(await File.ReadAllTextAsync(file)))
            {
                violations.Add(describe(relative));
            }
        }

        return violations;
    }

    // EF migrations are generated code and never resolve a tenant at runtime.
    private static bool IsMigrationsProject(string relative) =>
        relative.StartsWith("ZWarden.Migrations.", StringComparison.Ordinal);

    // Drop // and /// comment lines, so doc comments that name the rule don't trip it.
    private static string StripComments(string text) =>
        string.Join('\n', text.Split('\n').Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));

    private static IEnumerable<string> SourceFiles()
    {
        string src = Path.Combine(RepoRoot(), "src");
        foreach (string pattern in new[] { "*.cs", "*.razor" })
        {
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
