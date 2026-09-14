using ZWarden.Domain.Players;

namespace ZWarden.Domain.Tests.Players;

/// <summary>
/// F19 D-3: the shared username/reason validation that makes RCON command injection impossible by construction.
/// A valid argument returns <c>null</c>; every injection vector against PZ's quote-stripping, whitespace-splitting
/// tokenizer (research §7 quirk 10) — an embedded quote, whitespace, a control character, a leading dash, a
/// non-ASCII byte, an over-long value — returns a rejection reason. These rules are enforced on both the Web edge
/// and the Agent, so this is the one place the contract is pinned.
/// </summary>
public class PlayerCommandRulesTests
{
    [Test]
    [Arguments("Bob")]
    [Arguments("Bob_123")]
    [Arguments("player-two")]
    [Arguments("a.b.c")]
    public async Task A_clean_username_is_accepted(string username)
    {
        await Assert.That(PlayerCommandRules.ValidateUsername(username)).IsNull();
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments("   ")]
    public async Task An_empty_username_is_rejected(string? username)
    {
        await Assert.That(PlayerCommandRules.ValidateUsername(username)).IsNotNull();
    }

    [Test]
    [Arguments("Bob Smith")]          // whitespace splits the token
    [Arguments("Bob\tSmith")]         // tab is whitespace
    [Arguments("Bob\"; quit")]        // an embedded quote breaks the quoting
    [Arguments("Bob\nquit")]          // a newline could inject a second command / desync framing
    [Arguments("-ip")]                // a leading dash reads as a command flag
    [Arguments("-r")]
    [Arguments("Björn")]              // non-ASCII (PZ decodes with the platform default charset)
    public async Task An_injection_username_is_rejected(string username)
    {
        await Assert.That(PlayerCommandRules.ValidateUsername(username)).IsNotNull();
    }

    [Test]
    public async Task An_over_long_username_is_rejected()
    {
        string tooLong = new('a', PlayerCommandRules.MaxUsernameLength + 1);
        await Assert.That(PlayerCommandRules.ValidateUsername(tooLong)).IsNotNull();
    }

    [Test]
    public async Task A_username_at_the_length_bound_is_accepted()
    {
        string atBound = new('a', PlayerCommandRules.MaxUsernameLength);
        await Assert.That(PlayerCommandRules.ValidateUsername(atBound)).IsNull();
    }

    [Test]
    [Arguments(null)]                 // an omitted reason is valid
    [Arguments("griefing")]
    [Arguments("multiple words are fine because the reason is quoted")]
    public async Task A_clean_or_absent_reason_is_accepted(string? reason)
    {
        await Assert.That(PlayerCommandRules.ValidateReason(reason)).IsNull();
    }

    [Test]
    [Arguments("said \"hi\"")]        // an embedded quote breaks the quoting
    [Arguments("line one\nline two")] // a newline / control character
    [Arguments("café")]               // non-ASCII
    public async Task An_injection_reason_is_rejected(string reason)
    {
        await Assert.That(PlayerCommandRules.ValidateReason(reason)).IsNotNull();
    }

    [Test]
    public async Task An_over_long_reason_is_rejected()
    {
        string tooLong = new('a', PlayerCommandRules.MaxReasonLength + 1);
        await Assert.That(PlayerCommandRules.ValidateReason(tooLong)).IsNotNull();
    }
}
