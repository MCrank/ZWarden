using Bunit;
using ZWarden.Web.Tests.TestComponents;

namespace ZWarden.Web.Tests;

/// <summary>bUnit driven from plain C# - survives with or without reflection mode.</summary>
public class HarnessBadgeCSharpTests
{
    [Test]
    [Arguments(0, "empty")]
    [Arguments(3, "online")]
    public async Task Badge_class_follows_player_count(int players, string expectedClass)
    {
        using var ctx = new BunitContext();

        var cut = ctx.Render<HarnessBadge>(p => p.Add(c => c.PlayerCount, players));

        await Assert.That(cut.Find("span").ClassList).Contains(expectedClass);
        await Assert.That(cut.Find("span").TextContent).IsEqualTo($"{players} online");
    }
}
