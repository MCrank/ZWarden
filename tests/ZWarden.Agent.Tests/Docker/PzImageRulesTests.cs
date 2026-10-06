using ZWarden.Agent.Docker;

namespace ZWarden.Agent.Tests.Docker;

/// <summary>#364: the one rule for whether the Agent's configured PZ image can provision a server, with the
/// actionable reason when it can't.</summary>
public class PzImageRulesTests
{
    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments("   ")]
    public async Task A_missing_image_names_the_setting_to_fix(string? reference)
    {
        string? problem = PzImageRules.Problem(reference);

        await Assert.That(problem).IsNotNull();
        await Assert.That(problem!).Contains("no Project Zomboid image configured");
        await Assert.That(problem!).Contains("ZWARDEN_PZ_IMAGE");
        await Assert.That(problem!).Contains("restart the Agent");
    }

    [Test]
    [Arguments("latest")]
    [Arguments("zwarden-pzserver:latest")]
    [Arguments("registry.example.com:5000/zwarden-pzserver:latest")]
    public async Task A_floating_latest_tag_is_refused_with_the_reference_named(string reference)
    {
        string? problem = PzImageRules.Problem(reference);

        await Assert.That(problem).IsNotNull();
        await Assert.That(problem!).Contains($"'{reference}'");
        await Assert.That(problem!).Contains("floating");
        await Assert.That(problem!).Contains("ZWARDEN_PZ_IMAGE");
        await Assert.That(PzImageRules.IsFloating(reference)).IsTrue();
    }

    [Test]
    [Arguments("zwarden-pzserver:42.20.4")]
    [Arguments("ghcr.io/mcrank/zwarden-pzserver@sha256:abc")]
    [Arguments("registry.example.com:5000/zwarden-pzserver:ci")]
    public async Task A_pinned_reference_is_usable(string reference)
    {
        await Assert.That(PzImageRules.Problem(reference)).IsNull();
        await Assert.That(PzImageRules.IsFloating(reference)).IsFalse();
    }
}
