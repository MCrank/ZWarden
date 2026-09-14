using ZWarden.Application.Players;
using ZWarden.Domain.Ids;
using ZWarden.Infrastructure.Players;

namespace ZWarden.Infrastructure.Tests.Players;

/// <summary>
/// F19: the in-memory roster cache holds the newest roster per Server and is ownership-guarded — a roster is
/// returned only to a caller that names the Agent that reported it (trust-boundaries.md §8), and the last write
/// per Server wins.
/// </summary>
public class PlayerRosterCacheTests
{
    private static readonly DateTimeOffset At = new(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task It_returns_the_latest_roster_to_the_owning_agent()
    {
        AgentId agent = AgentId.New();
        ServerId server = ServerId.New();
        PlayerRosterCache cache = new();
        cache.Record(new PlayerRoster(server, agent, 1, ["Bob"], At));
        cache.Record(new PlayerRoster(server, agent, 2, ["Bob", "Alice"], At.AddSeconds(1)));

        PlayerRoster? latest = cache.GetLatest(server, agent);

        await Assert.That(latest).IsNotNull();
        await Assert.That(latest!.Count).IsEqualTo(2);
    }

    [Test]
    public async Task It_withholds_a_roster_from_a_non_owning_agent()
    {
        AgentId owner = AgentId.New();
        ServerId server = ServerId.New();
        PlayerRosterCache cache = new();
        cache.Record(new PlayerRoster(server, owner, 1, ["Bob"], At));

        await Assert.That(cache.GetLatest(server, AgentId.New())).IsNull();
        await Assert.That(cache.GetLatest(ServerId.New(), owner)).IsNull();
    }
}
