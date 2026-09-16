namespace ZWarden.ArchitectureTests;

/// <summary>
/// F40 PR-B (ADR 0039, PRD §55): an offline guard that the supply-chain controls of the release gate stay
/// present in CI and cannot silently disappear. The scans themselves run in GitHub Actions; this proves the
/// jobs and the Dependabot configuration are declared, in the same textual style as
/// <see cref="ComposeDistributionTests"/> / <see cref="HttpsReferenceDeploymentTests"/> — cheap, dependency-free,
/// and enough to catch a deletion or a rename that would quietly drop a control.
/// </summary>
public class SupplyChainCiTests
{
    private static async Task<string> CiWorkflowAsync() =>
        await File.ReadAllTextAsync(Path.Combine(RepoRoot(), ".github", "workflows", "ci.yml"));

    private static async Task<string> DependabotAsync() =>
        await File.ReadAllTextAsync(Path.Combine(RepoRoot(), ".github", "dependabot.yml"));

    [Test]
    public async Task Dependabot_watches_the_nuget_actions_and_docker_ecosystems()
    {
        string dependabot = await DependabotAsync();

        await Assert.That(dependabot).Contains("package-ecosystem: \"nuget\"");
        await Assert.That(dependabot).Contains("package-ecosystem: \"github-actions\"");
        await Assert.That(dependabot).Contains("package-ecosystem: \"docker\"");
    }

    [Test]
    public async Task Ci_generates_a_cyclonedx_sbom_and_uploads_it()
    {
        string ci = await CiWorkflowAsync();

        // The SBOM job runs the CycloneDX .NET tool over the solution and publishes the result as an artifact.
        await Assert.That(ci).Contains("sbom:");
        await Assert.That(ci).Contains("CycloneDX");
        await Assert.That(ci).Contains("actions/upload-artifact");
    }

    [Test]
    public async Task Ci_scans_for_committed_secrets_with_gitleaks()
    {
        string ci = await CiWorkflowAsync();

        await Assert.That(ci).Contains("secret-scan:");
        await Assert.That(ci).Contains("gitleaks");
    }

    [Test]
    public async Task Ci_scans_dependencies_and_the_pzserver_image_for_vulnerabilities_with_trivy()
    {
        string ci = await CiWorkflowAsync();

        // A filesystem scan (the .NET dependency graph + config) and a container-image scan (the canonical
        // PZServer image, PRD §55 "container vulnerability scanning").
        await Assert.That(ci).Contains("trivy");
        await Assert.That(ci).Contains("dependency-scan:");
        await Assert.That(ci).Contains("container-scan:");
        // Findings gate on severity, not on presence — HIGH and CRITICAL fail the job.
        await Assert.That(ci).Contains("CRITICAL");
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
