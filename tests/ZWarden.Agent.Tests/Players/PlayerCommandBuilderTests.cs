using ZWarden.Agent.Players;

namespace ZWarden.Agent.Tests.Players;

/// <summary>
/// F19 D-3: the Agent-side command builder quotes every argument (research §7 quirk 10) and refuses an argument
/// that fails validation before any byte reaches RCON — so a malicious username can never break out of its quotes
/// into a second command. The command text itself is asserted verbatim, because F28's console and the networked
/// integration test both depend on the exact wire form.
/// </summary>
public class PlayerCommandBuilderTests
{
    [Test]
    public async Task List_is_the_bare_players_command()
    {
        await Assert.That(PlayerCommandBuilder.List()).IsEqualTo("players");
    }

    [Test]
    public async Task Kick_quotes_the_username_and_reason()
    {
        await Assert.That(PlayerCommandBuilder.Kick("Bob", "griefing")).IsEqualTo("kickuser \"Bob\" -r \"griefing\"");
    }

    [Test]
    public async Task Kick_omits_the_reason_clause_when_there_is_none()
    {
        await Assert.That(PlayerCommandBuilder.Kick("Bob", null)).IsEqualTo("kickuser \"Bob\"");
    }

    [Test]
    public async Task Ban_quotes_the_username_and_reason()
    {
        await Assert.That(PlayerCommandBuilder.Ban("Bob", "cheating")).IsEqualTo("banuser \"Bob\" -r \"cheating\"");
    }

    [Test]
    public async Task Ban_omits_the_reason_clause_when_there_is_none()
    {
        await Assert.That(PlayerCommandBuilder.Ban("Bob", null)).IsEqualTo("banuser \"Bob\"");
    }

    [Test]
    public async Task Unban_quotes_the_username()
    {
        await Assert.That(PlayerCommandBuilder.Unban("Bob")).IsEqualTo("unbanuser \"Bob\"");
    }

    [Test]
    public async Task RemoveFromWhitelist_quotes_the_username()
    {
        await Assert.That(PlayerCommandBuilder.RemoveFromWhitelist("Bob")).IsEqualTo("removeuserfromwhitelist \"Bob\"");
    }

    [Test]
    [Arguments(true, "changeoption Open true")]
    [Arguments(false, "changeoption Open false")]
    public async Task SetWhitelistMode_toggles_the_open_option(bool open, string expected)
    {
        await Assert.That(PlayerCommandBuilder.SetWhitelistMode(open)).IsEqualTo(expected);
    }

    [Test]
    public async Task An_injection_username_is_refused_before_it_is_built()
    {
        await Assert.That(() => PlayerCommandBuilder.Kick("Bob\" -r \"x\"; quit", null))
            .Throws<PlayerCommandException>();
    }

    [Test]
    public async Task An_injection_reason_is_refused_before_it_is_built()
    {
        await Assert.That(() => PlayerCommandBuilder.Ban("Bob", "he said \"hi\""))
            .Throws<PlayerCommandException>();
    }

    [Test]
    public async Task A_username_with_whitespace_is_refused()
    {
        await Assert.That(() => PlayerCommandBuilder.Unban("Bob Smith")).Throws<PlayerCommandException>();
    }
}
