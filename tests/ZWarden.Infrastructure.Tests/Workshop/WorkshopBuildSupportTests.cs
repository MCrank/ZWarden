using ZWarden.Application.Workshop;

namespace ZWarden.Infrastructure.Tests.Workshop;

/// <summary>
/// #292: the Add sheet blocks an item that only supports Build 41, read from its Workshop tags. ZWarden runs Build 42
/// only, so a "Build 41"-tagged item without "Build 42" won't load. Untagged or unknown items are allowed — the tag is
/// the author's claim, and a missing one proves nothing.
/// </summary>
public class WorkshopBuildSupportTests
{
    [Test]
    [Arguments(new[] { "Build 41" }, true)]
    [Arguments(new[] { "build 41", "Multiplayer" }, true)]
    [Arguments(new[] { "Build 41", "Build 42" }, false)]
    [Arguments(new[] { "Build 42" }, false)]
    [Arguments(new[] { "Multiplayer" }, false)]
    [Arguments(new string[0], false)]
    public async Task Only_a_build_41_tag_without_build_42_is_build_41_only(string[] tags, bool expected)
    {
        await Assert.That(WorkshopBuildSupport.IsBuild41Only(tags)).IsEqualTo(expected);
    }
}
