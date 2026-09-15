namespace ZWarden.ArchitectureTests;

/// <summary>
/// F32 (ADR 0035): an offline guard that the reference Caddy ingress config and its deployment documentation
/// stay present and coherent. The end-to-end proxy behaviour is proven by the networked
/// <c>CaddyReferenceDeploymentTests</c> (schedule/dispatch only); this runs on every PR so the shipped Caddyfile,
/// the private-mode variant, and the three-mode deployment guide cannot silently disappear or diverge.
/// </summary>
public class HttpsReferenceDeploymentTests
{
    [Test]
    public async Task Public_mode_caddyfile_reverse_proxies_and_sets_hsts()
    {
        string caddyfile = await File.ReadAllTextAsync(Path.Combine(RepoRoot(), "deploy", "caddy", "Caddyfile"));

        await Assert.That(caddyfile).Contains("reverse_proxy");
        await Assert.That(caddyfile).Contains("Strict-Transport-Security"); // HSTS is not automatic (D-5)
        await Assert.That(caddyfile).Contains("{$ZWARDEN_DOMAIN}");
    }

    [Test]
    public async Task Private_mode_caddyfile_uses_the_internal_ca()
    {
        string internalMode = await File.ReadAllTextAsync(Path.Combine(RepoRoot(), "deploy", "caddy", "Caddyfile.internal"));

        await Assert.That(internalMode).Contains("tls internal");
    }

    [Test]
    public async Task Deployment_guide_documents_all_three_tls_modes()
    {
        string guide = await File.ReadAllTextAsync(Path.Combine(RepoRoot(), "docs", "deployment", "https-reference.md"));

        await Assert.That(guide).Contains("Public");
        await Assert.That(guide).Contains("Private");
        await Assert.That(guide).Contains("Existing reverse proxy");
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
