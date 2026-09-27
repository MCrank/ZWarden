using ZWarden.Domain.Servers;

namespace ZWarden.Domain.Tests.Servers;

/// <summary>
/// #258: the Build 42 Steam branch a server installs. Null means the public branch. A branch name ends up in
/// <c>-beta ${ZW_PZ_BETA}</c> in the SteamCMD runscript without quotes, so only a tight charset is accepted, and
/// Build 41 is refused because ZWarden supports Build 42 only.
/// </summary>
public class ServerBranchRulesTests
{
    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments("   ")]
    [Arguments("public")]
    [Arguments(" Public ")]
    public async Task No_branch_or_public_normalizes_to_the_public_default(string? branch)
    {
        await Assert.That(ServerBranchRules.Normalize(branch)).IsNull();
        await Assert.That(ServerBranchRules.Validate(branch)).IsNull();
    }

    [Test]
    [Arguments("unstable", "unstable")]
    [Arguments("42.19", "42.19")]
    [Arguments(" Unstable ", "unstable")]
    [Arguments("my_branch-2", "my_branch-2")]
    public async Task A_real_branch_name_is_trimmed_and_lowercased(string branch, string expected)
    {
        await Assert.That(ServerBranchRules.Normalize(branch)).IsEqualTo(expected);
        await Assert.That(ServerBranchRules.Validate(branch)).IsNull();
    }

    [Test]
    [Arguments("42.19 validate")]
    [Arguments("x;quit")]
    [Arguments("x\nquit")]
    [Arguments("$(id)")]
    [Arguments("a/b")]
    [Arguments("-beta")]
    [Arguments(".hidden")]
    [Arguments("naïve")]
    public async Task A_name_that_could_escape_the_runscript_is_rejected(string branch)
    {
        await Assert.That(ServerBranchRules.Validate(branch)).IsNotNull();
    }

    [Test]
    public async Task A_name_longer_than_the_limit_is_rejected()
    {
        await Assert.That(ServerBranchRules.Validate(new string('a', ServerBranchRules.MaxLength))).IsNull();
        await Assert.That(ServerBranchRules.Validate(new string('a', ServerBranchRules.MaxLength + 1))).IsNotNull();
    }

    [Test]
    [Arguments("legacy41")]
    [Arguments("LEGACY41")]
    public async Task Build_41_is_refused(string branch)
    {
        await Assert.That(ServerBranchRules.Validate(branch)).Contains("Build 42");
    }

    [Test]
    public async Task The_curated_list_leads_with_public_and_marks_unstable_as_a_preview()
    {
        var options = ServerBranchCatalog.Options;

        await Assert.That(options[0].Branch).IsNull();
        await Assert.That(options.Single(o => o.Branch == "unstable").IsPreview).IsTrue();
        await Assert.That(options.Any(o => o.Branch == "42.19")).IsTrue();
        foreach (var option in options.Where(o => o.Branch is not null))
        {
            await Assert.That(ServerBranchRules.Validate(option.Branch)).IsNull();
        }
    }

    [Test]
    [Arguments(null, "public")]
    [Arguments("42.19", "42.19")]
    public async Task The_display_name_of_the_default_is_public(string? branch, string expected)
    {
        await Assert.That(ServerBranchRules.DisplayName(branch)).IsEqualTo(expected);
    }
}
