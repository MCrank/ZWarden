using ZWarden.Agent.Players;
using ZWarden.Contracts.Protocol.Messages;

namespace ZWarden.Agent.Tests.Players;

/// <summary>
/// F19: parsing PZ's untrusted replies (trust-boundaries.md §8). The roster header/line format and the stable
/// kick strings are matched; prose is classified with a light heuristic; an empty reply is not a fault. Nothing
/// in a reply is ever interpreted — a hostile username is carried through verbatim for escaping at render (F28).
/// </summary>
public class PlayerResponseParserTests
{
    [Test]
    public async Task ParseRoster_reads_the_count_and_usernames()
    {
        PlayerRosterResult roster = PlayerResponseParser.ParseRoster("Players connected (2): \n-Bob\n-Alice\n");
        string[] expected = ["Bob", "Alice"];

        await Assert.That(roster.Count).IsEqualTo(2);
        await Assert.That(roster.Players).IsEquivalentTo(expected);
    }

    [Test]
    public async Task ParseRoster_handles_an_empty_server()
    {
        PlayerRosterResult roster = PlayerResponseParser.ParseRoster("Players connected (0): \n");

        await Assert.That(roster.Count).IsEqualTo(0);
        await Assert.That(roster.Players).IsEmpty();
    }

    [Test]
    public async Task ParseRoster_falls_back_to_counting_lines_without_a_header()
    {
        PlayerRosterResult roster = PlayerResponseParser.ParseRoster("-Bob\n-Alice");

        await Assert.That(roster.Count).IsEqualTo(2);
    }

    [Test]
    public async Task ParseRoster_of_an_empty_reply_is_empty()
    {
        PlayerRosterResult roster = PlayerResponseParser.ParseRoster(string.Empty);

        await Assert.That(roster.Count).IsEqualTo(0);
        await Assert.That(roster.Players).IsEmpty();
    }

    [Test]
    public async Task ParseRoster_carries_a_hostile_username_verbatim_without_interpreting_it()
    {
        PlayerRosterResult roster = PlayerResponseParser.ParseRoster("Players connected (1): \n-<script>alert(1)</script>\n");

        await Assert.That(roster.Players).Contains("<script>alert(1)</script>");
    }

    [Test]
    [Arguments("User Bob kicked.", PlayerActionOutcome.Applied)]
    [Arguments("User Bob doesn't exist.", PlayerActionOutcome.NotFound)]
    [Arguments("This user can't be kicked.", PlayerActionOutcome.Rejected)]
    [Arguments("something unexpected", PlayerActionOutcome.Unknown)]
    public async Task ParseKick_classifies_the_stable_strings(string reply, PlayerActionOutcome expected)
    {
        await Assert.That(PlayerResponseParser.ParseKick(reply).Outcome).IsEqualTo(expected);
    }

    [Test]
    public async Task ParseKick_carries_the_reply_as_detail()
    {
        await Assert.That(PlayerResponseParser.ParseKick("User Bob kicked.").Detail).IsEqualTo("User Bob kicked.");
    }

    [Test]
    [Arguments("User Bob banned.", PlayerActionOutcome.Applied)]
    [Arguments("User Bob doesn't exist.", PlayerActionOutcome.NotFound)]
    [Arguments("", PlayerActionOutcome.Unknown)]
    public async Task ParseProseAction_classifies_ban_unban_remove(string reply, PlayerActionOutcome expected)
    {
        await Assert.That(PlayerResponseParser.ParseProseAction(reply).Outcome).IsEqualTo(expected);
    }

    [Test]
    [Arguments("Option : Open is now : false", PlayerActionOutcome.Applied)]
    [Arguments("Option Open doesn't exist.", PlayerActionOutcome.Unknown)]
    public async Task ParseWhitelistMode_confirms_the_echoed_option(string reply, PlayerActionOutcome expected)
    {
        await Assert.That(PlayerResponseParser.ParseWhitelistMode(reply).Outcome).IsEqualTo(expected);
    }

    [Test]
    public async Task An_empty_action_reply_has_no_detail()
    {
        await Assert.That(PlayerResponseParser.ParseProseAction(string.Empty).Detail).IsNull();
    }
}
