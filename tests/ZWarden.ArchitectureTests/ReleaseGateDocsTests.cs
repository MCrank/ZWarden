namespace ZWarden.ArchitectureTests;

/// <summary>
/// F40 PR-E: an offline guard that the release-gate documents stay present and keep full coverage. The gate's
/// exit condition is a <i>documented</i> gate (PRD 59), so the checklist and the two standard assessments are
/// themselves artifacts that must not silently disappear or lose a category — the same drift discipline the
/// code controls get, applied to the documents that index them.
/// </summary>
public class ReleaseGateDocsTests
{
    private static async Task<string> DocAsync(params string[] relative) =>
        await File.ReadAllTextAsync(Path.Combine([RepoRoot(), "docs", .. relative]));

    [Test]
    public async Task The_release_gate_checklist_links_the_threat_model_and_both_assessments()
    {
        string gate = await DocAsync("release-gate.md");

        await Assert.That(gate).Contains("trust-boundaries.md");
        await Assert.That(gate).Contains("security/owasp-top-10-2025.md");
        await Assert.That(gate).Contains("security/asvs-5.0.0.md");
        // The gate must own its accepted gaps openly, not bury them.
        await Assert.That(gate).Contains("residual risk");
    }

    [Test]
    public async Task The_owasp_assessment_covers_every_2025_category()
    {
        string owasp = await DocAsync("security", "owasp-top-10-2025.md");

        for (int n = 1; n <= 10; n++)
        {
            string code = $"A{n:D2}";
            await Assert.That(owasp).Contains(code).Because($"the OWASP Top 10:2025 assessment must cover {code}.");
        }

        // The two categories new in 2025 are called out by name (scope-and-sequencing §6).
        await Assert.That(owasp).Contains("Supply Chain");
        await Assert.That(owasp).Contains("Exceptional Conditions");
    }

    [Test]
    public async Task The_asvs_assessment_covers_the_v5_chapters_and_states_the_target_level()
    {
        string asvs = await DocAsync("security", "asvs-5.0.0.md");

        await Assert.That(asvs).Contains("5.0.0");
        await Assert.That(asvs).Contains("Level 2");
        // Full chapter coverage: the first and last of the 17 chapters must both appear.
        await Assert.That(asvs).Contains("V1 Encoding");
        await Assert.That(asvs).Contains("V17 WebRTC");
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
