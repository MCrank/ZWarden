namespace ZWarden.ArchitectureTests;

/// <summary>
/// F13 (ADR 0008, research §5.3) architecture guards, turned into a red build. No proxy can constrain the
/// <c>POST /containers/create</c> body — a create carrying <c>Privileged: true</c> or a host namespace is a
/// full host escape that reaches the daemon unsanitized (measured). So the invariants are an <b>Agent-side
/// correctness requirement</b>, and item 1 of §5.3 gets the cheap PRD 15 guard the ADR names explicitly:
/// no source in <c>src/</c> may set <c>Privileged</c> true or pin a container to a host namespace.
/// Source-text scans, in the style of <see cref="SecretHandlingGuardTests"/>.
/// </summary>
public class ContainerCreationGuardTests
{
    // §5.3 item 1: Privileged is never settable, never configurable, no override. The sharpest, cheapest
    // guard the ADR asks for. Whitespace around '=' is normalized away before matching.
    private static readonly string[] ForbiddenPrivilegedAssignments =
    [
        "Privileged=true",
    ];

    // §5.3 item 5: host PID/IPC/userns/UTS/cgroup namespaces are never used; §5.3 item 4: NetworkMode is a
    // named ZWarden network, never "host". These catch the literal host-namespace pin on the mode properties.
    private static readonly string[] ForbiddenHostNamespaces =
    [
        "PidMode=\"host\"",
        "IpcMode=\"host\"",
        "UsernsMode=\"host\"",
        "UTSMode=\"host\"",
        "CgroupnsMode=\"host\"",
        "NetworkMode=\"host\"",
    ];

    [Test]
    public async Task No_source_sets_a_container_privileged()
    {
        await AssertNoSourceContains(ForbiddenPrivilegedAssignments,
            "a container must never be created privileged (ADR 0008, research §5.3 item 1)");
    }

    [Test]
    public async Task No_source_pins_a_container_to_a_host_namespace()
    {
        await AssertNoSourceContains(ForbiddenHostNamespaces,
            "a container must never share a host namespace (ADR 0008, research §5.3 items 4-5)");
    }

    private static async Task AssertNoSourceContains(string[] forbidden, string why)
    {
        List<string> violations = [];
        foreach (string file in SourceFiles())
        {
            foreach (string line in await File.ReadAllLinesAsync(file))
            {
                string normalized = line.Replace(" ", string.Empty, StringComparison.Ordinal);
                foreach (string needle in forbidden)
                {
                    if (normalized.Contains(needle, StringComparison.Ordinal))
                    {
                        violations.Add($"'{needle}' in {Path.GetFileName(file)}: {line.Trim()} — {why}.");
                    }
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
