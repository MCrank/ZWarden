using ZWarden.Application.Servers;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Servers;
using ZWarden.Web.Components.Servers;

namespace ZWarden.Web.Tests.Servers;

/// <summary>
/// #257: the fleet board's per-server facts and KPI tiles, computed once in C# for both the first render and the
/// batched status poll (which sends the formatted uptime and sample age). Players and uptime come from the ownership-guarded
/// metrics cache and read as <c>—</c> when there is no sample or the owning Agent is offline; Version shows the game
/// version (#262, persisted then cached) with the Steam build id as its tooltip, falling back to the build id.
/// </summary>
public class FleetFactsTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);

    private static ServerSummary Summary(
        ServerRunState state = ServerRunState.Running, ServerHealth? health = null, string? build = null, string name = "alpha",
        string? gameVersion = null) =>
        new(ServerId.New(), AgentId.New(), name, null, null, null, state, Now, health, Now, build, gameVersion);

    private static ServerMetrics Sample(
        ServerSummary s, int? players = null, DateTimeOffset? started = null, string? build = null, string? gameVersion = null) =>
        new(s.AgentId, s.Id, 40, 1_000, 2_000, null, null, players, Now, players is null ? null : Now.AddMinutes(-2), started, build,
            gameVersion);

    [Test]
    public async Task A_sampled_server_with_its_agent_online_carries_every_fact()
    {
        ServerSummary s = Summary(build: "111");
        FleetServerFacts facts = FleetFacts.For(s, Sample(s, players: 4, started: Now.AddHours(-3), build: "222"), agentOnline: true);

        await Assert.That(facts.Players).IsEqualTo(4);
        await Assert.That(facts.PlayersSampledAt).IsEqualTo(Now.AddMinutes(-2));
        await Assert.That(facts.StartedAt).IsEqualTo(Now.AddHours(-3));
        await Assert.That(facts.Version).IsEqualTo("111"); // the persisted build wins
        await Assert.That(facts.CpuPercent).IsEqualTo(40d);
        await Assert.That(facts.IsRunning).IsTrue();
        await Assert.That(facts.NeedsAttention).IsFalse();
    }

    [Test]
    public async Task Version_falls_back_to_the_cached_manifest_build()
    {
        ServerSummary s = Summary(build: null);

        await Assert.That(FleetFacts.For(s, Sample(s, build: "222"), agentOnline: true).Version).IsEqualTo("222");
        await Assert.That(FleetFacts.For(s, null, agentOnline: true).Version).IsNull();
    }

    [Test]
    public async Task The_game_version_is_shown_with_the_steam_build_as_its_tooltip()
    {
        // #262: persisted game version first, then the cached one; the build id moves to the tooltip.
        ServerSummary persisted = Summary(build: "111", gameVersion: "42.20.4");
        FleetServerFacts shown = FleetFacts.For(persisted, Sample(persisted, gameVersion: "42.21.0"), agentOnline: true);
        await Assert.That(shown.Version).IsEqualTo("42.20.4");
        await Assert.That(shown.SteamBuild).IsEqualTo("111");
        await Assert.That(FleetFacts.FormatVersionTitle(shown)).IsEqualTo("Steam build 111");

        ServerSummary fresh = Summary();
        FleetServerFacts cached = FleetFacts.For(fresh, Sample(fresh, build: "222", gameVersion: "42.20.4"), agentOnline: true);
        await Assert.That(cached.Version).IsEqualTo("42.20.4");
        await Assert.That(cached.SteamBuild).IsEqualTo("222");
    }

    [Test]
    public async Task Without_a_game_version_the_build_is_shown_and_the_tooltip_says_so()
    {
        ServerSummary s = Summary(build: "111");
        FleetServerFacts facts = FleetFacts.For(s, null, agentOnline: true);

        await Assert.That(facts.Version).IsEqualTo("111");
        await Assert.That(FleetFacts.FormatVersionTitle(facts)).Contains("not been read yet");
        await Assert.That(FleetFacts.FormatVersionTitle(FleetFacts.For(Summary(), null, true))).IsNull();
    }

    [Test]
    public async Task An_offline_agent_blanks_players_and_uptime()
    {
        ServerSummary s = Summary();
        FleetServerFacts facts = FleetFacts.For(s, Sample(s, players: 4, started: Now.AddHours(-3)), agentOnline: false);

        await Assert.That(facts.Players).IsNull();
        await Assert.That(facts.PlayersSampledAt).IsNull();
        await Assert.That(facts.StartedAt).IsNull();
    }

    [Test]
    [Arguments(ServerRunState.Failed, null)]
    [Arguments(ServerRunState.Running, ServerHealth.Degraded)]
    [Arguments(ServerRunState.Running, ServerHealth.Failed)]
    public async Task Failed_run_state_or_impaired_health_needs_attention(ServerRunState state, ServerHealth? health)
    {
        await Assert.That(FleetFacts.For(Summary(state, health), null, agentOnline: true).NeedsAttention).IsTrue();
    }

    [Test]
    public async Task The_kpis_count_running_and_attention_and_sum_the_known_players()
    {
        ServerSummary a = Summary(name: "alpha");
        ServerSummary b = Summary(ServerRunState.Failed, name: "bravo");
        ServerSummary c = Summary(ServerRunState.Stopped, name: "charlie");
        FleetKpis kpis = FleetFacts.Kpis(
        [
            (a, FleetFacts.For(a, Sample(a, players: 3), true)),
            (b, FleetFacts.For(b, null, true)),
            (c, FleetFacts.For(c, Sample(c, players: 2), true)),
        ]);

        await Assert.That(kpis.Running).IsEqualTo(1);
        await Assert.That(kpis.NeedsAttention).IsEqualTo(1);
        await Assert.That(kpis.AttentionName).IsEqualTo("bravo");
        await Assert.That(kpis.PlayersOnline).IsEqualTo(5);
    }

    [Test]
    public async Task With_no_player_count_anywhere_the_kpi_is_unknown_not_zero()
    {
        ServerSummary a = Summary();

        await Assert.That(FleetFacts.Kpis([(a, FleetFacts.For(a, null, true))]).PlayersOnline).IsNull();
    }

    [Test]
    [Arguments(-30, "<1m")]
    [Arguments(30, "<1m")]
    [Arguments(14 * 60 + 5, "14m")]
    [Arguments(2 * 3600 + 14 * 60, "2h 14m")]
    [Arguments(3 * 86400 + 4 * 3600 + 59 * 60, "3d 4h")]
    public async Task Uptime_is_compact(int seconds, string expected)
    {
        await Assert.That(FleetFacts.FormatUptime(Now.AddSeconds(-seconds), Now)).IsEqualTo(expected);
    }

    [Test]
    public async Task No_start_time_is_a_dash()
    {
        await Assert.That(FleetFacts.FormatUptime(null, Now)).IsEqualTo("—");
    }

    [Test]
    [Arguments(20, "as of just now")]
    [Arguments(150, "as of 2 min ago")]
    [Arguments(2 * 3600 + 60, "as of 2 h ago")]
    public async Task The_player_sample_age_reads_as_a_phrase(int seconds, string expected)
    {
        await Assert.That(FleetFacts.FormatSampleAge(Now.AddSeconds(-seconds), Now)).IsEqualTo(expected);
    }
}
