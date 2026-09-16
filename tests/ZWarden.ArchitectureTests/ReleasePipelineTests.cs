namespace ZWarden.ArchitectureTests;

/// <summary>
/// F40 PR-C (ADR 0039, PRD §55): an offline guard that the signed-release pipeline stays present and keeps its
/// teeth. The publish + sign only happens on a version tag (it needs GHCR and OIDC, so it cannot run here), but
/// the release workflow's shape — what it publishes, that it signs and attests, that the NuGet advisory gate is
/// a hard blocker on the release path, and that it emits verifiable checksums and a digest-pinned Compose — is
/// asserted textually, in the same style as <see cref="SupplyChainCiTests"/> / <see cref="ComposeDistributionTests"/>.
/// </summary>
public class ReleasePipelineTests
{
    private static async Task<string> ReleaseWorkflowAsync() =>
        await File.ReadAllTextAsync(Path.Combine(RepoRoot(), ".github", "workflows", "release.yml"));

    [Test]
    public async Task Release_runs_only_on_a_version_tag()
    {
        string release = await ReleaseWorkflowAsync();

        await Assert.That(release).Contains("tags:");
        await Assert.That(release).Contains("\"v*\"");
    }

    [Test]
    public async Task Release_holds_the_least_privilege_it_needs_to_publish_and_sign()
    {
        string release = await ReleaseWorkflowAsync();

        await Assert.That(release).Contains("packages: write");   // push to GHCR
        await Assert.That(release).Contains("id-token: write");   // cosign keyless (OIDC)
        await Assert.That(release).Contains("attestations: write"); // SBOM/provenance attestations
        await Assert.That(release).Contains("contents: write");   // create the GitHub Release
    }

    [Test]
    public async Task Release_builds_and_publishes_the_three_images_to_ghcr()
    {
        string release = await ReleaseWorkflowAsync();

        await Assert.That(release).Contains("ghcr.io");
        await Assert.That(release).Contains("zwarden-web");
        await Assert.That(release).Contains("zwarden-agent");
        await Assert.That(release).Contains("zwarden-pzserver");
    }

    [Test]
    public async Task Release_signs_each_image_and_attests_its_sbom_keylessly()
    {
        string release = await ReleaseWorkflowAsync();

        await Assert.That(release).Contains("cosign");
        await Assert.That(release).Contains("cosign sign");
        // An SBOM is generated and attached to the image as an attestation (syft/CycloneDX).
        await Assert.That(release).Contains("cosign attest");
        await Assert.That(release).Contains("cyclonedx");
    }

    [Test]
    public async Task The_nuget_advisory_gate_is_a_hard_blocker_on_the_release_path()
    {
        string release = await ReleaseWorkflowAsync();

        // Warning-only on PRs (ADR 0013); promoted to an error that fails the release build (ADR 0039).
        await Assert.That(release).Contains("PromoteNuGetAudit=true");
    }

    [Test]
    public async Task Release_emits_verifiable_checksums_and_a_digest_pinned_compose()
    {
        string release = await ReleaseWorkflowAsync();

        await Assert.That(release).Contains("SHA256SUMS");
        await Assert.That(release).Contains("compose.release.yaml");
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
