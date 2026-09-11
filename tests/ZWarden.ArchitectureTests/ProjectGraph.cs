using System.Xml.Linq;

namespace ZWarden.ArchitectureTests;

/// <summary>
/// Reads a <c>src/*.csproj</c> and exposes its declared dependencies. The
/// architecture rules assert against what a project *declares* rather than what the
/// compiler happened to keep, so an accidentally-added forbidden reference is caught
/// whether or not any code uses it yet.
/// </summary>
public sealed record ProjectGraph(
    IReadOnlySet<string> ProjectReferences,
    IReadOnlySet<string> PackageReferences,
    IReadOnlySet<string> FrameworkReferences)
{
    public static ProjectGraph ForSourceProject(string projectName)
    {
        string path = Path.Combine(RepoRoot.Value, "src", projectName, $"{projectName}.csproj");
        XDocument doc = XDocument.Load(path);

        static IReadOnlySet<string> Includes(XDocument d, string element, Func<string, string> project) =>
            d.Descendants(element)
                .Select(e => (string?)e.Attribute("Include"))
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Select(v => project(v!))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return new ProjectGraph(
            Includes(doc, "ProjectReference", v => Path.GetFileNameWithoutExtension(v.Replace('\\', '/'))),
            Includes(doc, "PackageReference", v => v),
            Includes(doc, "FrameworkReference", v => v));
    }

    private static readonly Lazy<string> RepoRoot = new(() =>
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ZWarden.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Could not locate the repository root (ZWarden.slnx).");
    });
}
