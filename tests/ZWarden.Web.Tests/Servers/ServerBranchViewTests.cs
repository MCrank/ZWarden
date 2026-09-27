using ZWarden.Web.Components.Servers;

namespace ZWarden.Web.Tests.Servers;

/// <summary>
/// #258: the branch picker's parsing and wording. The picker offers the curated list plus "Custom…"; the custom name
/// is read only when Custom is picked, since the static-SSR form can't show or hide the field. The label and note
/// are what the fleet board and Server Detail show.
/// </summary>
public class ServerBranchViewTests
{
    [Test]
    [Arguments(null, null)]
    [Arguments("", null)]
    [Arguments("42.19", "42.19")]
    [Arguments("unstable", "unstable")]
    public async Task A_curated_choice_resolves_to_its_branch(string? choice, string? expected)
    {
        bool ok = ServerBranchView.TryResolve(choice, custom: "ignored-unless-custom", out string? branch, out string? error);

        await Assert.That(ok).IsTrue();
        await Assert.That(error).IsNull();
        await Assert.That(branch).IsEqualTo(expected);
    }

    [Test]
    public async Task A_custom_choice_resolves_to_the_typed_branch_normalized()
    {
        bool ok = ServerBranchView.TryResolve(ServerBranchView.CustomChoice, " My-Test ", out string? branch, out _);

        await Assert.That(ok).IsTrue();
        await Assert.That(branch).IsEqualTo("my-test");
    }

    [Test]
    public async Task A_custom_choice_with_no_name_is_refused()
    {
        bool ok = ServerBranchView.TryResolve(ServerBranchView.CustomChoice, "  ", out _, out string? error);

        await Assert.That(ok).IsFalse();
        await Assert.That(error!).Contains("custom branch");
    }

    [Test]
    [Arguments("legacy41", "Build 42")]
    [Arguments("a b", "may only contain")]
    public async Task An_invalid_custom_branch_is_refused_with_the_rule(string custom, string reason)
    {
        bool ok = ServerBranchView.TryResolve(ServerBranchView.CustomChoice, custom, out _, out string? error);

        await Assert.That(ok).IsFalse();
        await Assert.That(error!).Contains(reason);
    }

    [Test]
    public async Task A_choice_that_is_not_offered_is_refused()
    {
        // A tampered post: only the curated values and Custom are accepted from the picker.
        await Assert.That(ServerBranchView.TryResolve("x;quit", null, out _, out _)).IsFalse();
    }

    [Test]
    [Arguments(null, "public")]
    [Arguments("42.19", "42.19")]
    [Arguments("unstable", "unstable (preview)")]
    public async Task The_label_names_the_branch_and_flags_a_preview(string? branch, string expected)
    {
        await Assert.That(ServerBranchView.Label(branch)).IsEqualTo(expected);
    }

    [Test]
    public async Task The_note_says_whether_updates_follow_public_or_stay_on_the_branch()
    {
        await Assert.That(ServerBranchView.Note(null)).Contains("each new public release");
        await Assert.That(ServerBranchView.Note("42.19")).Contains("stay on 42.19");
    }

    [Test]
    [Arguments(null, "Pulls the latest build on public.")]
    [Arguments("unstable", "Pulls the latest build on unstable (preview).")]
    [Arguments("42.19", "Stays on 42.19 (pinned).")]
    [Arguments("42.18.1", "Stays on 42.18.1 (pinned).")]
    [Arguments("iwillbackupmysave", "Pulls the latest build on iwillbackupmysave.")]
    public async Task The_update_note_says_what_update_game_pulls(string? branch, string expected)
    {
        // #273: beside the header's Update game. A version-named branch is a pin; any other branch moves.
        await Assert.That(ServerBranchView.UpdateNote(branch)).IsEqualTo(expected);
    }
}
