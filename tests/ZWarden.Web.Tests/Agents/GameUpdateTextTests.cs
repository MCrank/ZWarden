using ZWarden.Contracts.Protocol.Messages;
using ZWarden.Web.Agents;

namespace ZWarden.Web.Tests.Agents;

/// <summary>#273: the result line a completed game update shows — which Steam build it moved from and to, or that the
/// server was already current. Both build ids are Agent-observed (read from the manifest before and after SteamCMD).</summary>
public class GameUpdateTextTests
{
    [Test]
    [Arguments("24909836", "25485538", "Updated from Steam build 24909836 to 25485538.")]
    [Arguments("25485538", "25485538", "Already on the latest build (Steam build 25485538).")]
    [Arguments(null, "25485538", "Installed Steam build 25485538.")]
    public async Task An_update_describes_the_build_it_moved_between(string? previous, string installed, string expected)
    {
        await Assert.That(GameUpdateText.Describe(new UpdateResult(installed, previous))).IsEqualTo(expected);
    }

    [Test]
    public async Task An_unreadable_manifest_has_no_line()
    {
        await Assert.That(GameUpdateText.Describe(new UpdateResult(null, "24909836"))).IsNull();
    }
}
