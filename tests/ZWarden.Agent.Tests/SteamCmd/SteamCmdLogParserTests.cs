using ZWarden.Agent.SteamCmd;

namespace ZWarden.Agent.Tests.SteamCmd;

/// <summary>
/// F17: the pure SteamCMD log parser. The Agent cannot exec SteamCMD (ADR 0008), so it reads the container's
/// <c>docker logs</c>; this maps that text to progress + a terminal outcome. Outcome is the entrypoint's
/// authoritative <c>end (success|failure)</c> banner — never an exit code (ADR 0009) — bounded to one session.
/// </summary>
public class SteamCmdLogParserTests
{
    private const string Session = "op-abc123";

    private static string Log(params string[] lines) => string.Join('\n', lines);

    [Test]
    public async Task Before_the_begin_banner_the_session_is_pending_with_no_progress()
    {
        SteamCmdUpdateState state = SteamCmdLogParser.Parse("server booting...\n", Session);

        await Assert.That(state.Outcome).IsEqualTo(SteamCmdOutcome.Pending);
        await Assert.That(state.LatestProgress).IsNull();
    }

    [Test]
    public async Task Mid_update_it_reports_the_latest_progress_and_stays_pending()
    {
        string log = Log(
            $"[zwarden] steamcmd update session {Session} begin",
            " Update state (0x61) downloading, progress: 6.40 (458103807 / 7160173345)",
            " Update state (0x61) downloading, progress: 42.80 (3064531200 / 7160173345)");

        SteamCmdUpdateState state = SteamCmdLogParser.Parse(log, Session);

        await Assert.That(state.Outcome).IsEqualTo(SteamCmdOutcome.Pending);
        await Assert.That(state.LatestProgress!.Percent).IsEqualTo(43); // last line, rounded
        await Assert.That(state.LatestProgress!.Status).IsEqualTo("downloading");
    }

    [Test]
    public async Task It_parses_the_verifying_phase()
    {
        string log = Log(
            $"[zwarden] steamcmd update session {Session} begin",
            " Update state (0x5) verifying install, progress: 88.10 (100 / 113)");

        SteamCmdProgress progress = SteamCmdLogParser.Parse(log, Session).LatestProgress!;

        await Assert.That(progress.Status).IsEqualTo("verifying install");
        await Assert.That(progress.Percent).IsEqualTo(88);
    }

    [Test]
    public async Task A_success_end_banner_is_a_succeeded_outcome()
    {
        string log = Log(
            $"[zwarden] steamcmd update session {Session} begin",
            " Update state (0x61) downloading, progress: 99.90 (7160000000 / 7160173345)",
            "Success! App '380870' fully installed",
            $"[zwarden] steamcmd update session {Session} end (success)",
            "[zwarden] launching: ..."); // server output after the session must be ignored

        SteamCmdUpdateState state = SteamCmdLogParser.Parse(log, Session);

        await Assert.That(state.Outcome).IsEqualTo(SteamCmdOutcome.Succeeded);
        await Assert.That(state.FailureReason).IsNull();
    }

    [Test]
    public async Task A_failure_end_banner_is_a_failed_outcome_with_the_error_line_as_reason()
    {
        string log = Log(
            $"[zwarden] steamcmd update session {Session} begin",
            "Error! App '380870' state is 0x202 after update job.",
            $"[zwarden] steamcmd update session {Session} end (failure)");

        SteamCmdUpdateState state = SteamCmdLogParser.Parse(log, Session);

        await Assert.That(state.Outcome).IsEqualTo(SteamCmdOutcome.Failed);
        await Assert.That(state.FailureReason).IsEqualTo("Error! App '380870' state is 0x202 after update job.");
    }

    [Test]
    public async Task Progress_is_clamped_to_0_100()
    {
        string log = Log(
            $"[zwarden] steamcmd update session {Session} begin",
            " Update state (0x61) downloading, progress: 250.00 (x / y)");

        await Assert.That(SteamCmdLogParser.Parse(log, Session).LatestProgress!.Percent).IsEqualTo(100);
    }

    [Test]
    public async Task Another_sessions_banners_are_ignored()
    {
        string log = Log(
            "[zwarden] steamcmd update session op-OTHER begin",
            " Update state (0x61) downloading, progress: 50.00 (x / y)",
            "[zwarden] steamcmd update session op-OTHER end (success)");

        SteamCmdUpdateState state = SteamCmdLogParser.Parse(log, Session);

        await Assert.That(state.Outcome).IsEqualTo(SteamCmdOutcome.Pending);
        await Assert.That(state.LatestProgress).IsNull();
    }
}
