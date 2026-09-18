using ZWarden.Domain.Servers;

namespace ZWarden.Domain.Tests.Servers;

/// <summary>
/// #114: the shared validation and formatting for a graceful-restart broadcast. A valid message returns
/// <c>null</c>; every injection vector against PZ's quote-stripping, whitespace-splitting tokenizer (research §7
/// quirk 10) is rejected. The countdown formatter is a pure, total function whose output is always printable
/// ASCII, so it round-trips through <see cref="BroadcastMessageRules.ValidateMessage"/>.
/// </summary>
public class BroadcastMessageRulesTests
{
    [Test]
    [Arguments("Server restarting in 5 minutes.")]
    [Arguments("multiple words are fine because the message is quoted")]
    [Arguments("Maintenance now - please log out")]
    public async Task A_clean_message_is_accepted(string message)
    {
        await Assert.That(BroadcastMessageRules.ValidateMessage(message)).IsNull();
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments("   ")]
    public async Task An_empty_message_is_rejected(string? message)
    {
        await Assert.That(BroadcastMessageRules.ValidateMessage(message)).IsNotNull();
    }

    [Test]
    [Arguments("said \"hi\"")]        // an embedded quote breaks the quoting
    [Arguments("line one\nline two")] // a newline could inject a second command / desync framing
    [Arguments("tab\there")]          // a tab is a control character
    [Arguments("café")]               // non-ASCII (PZ decodes with the platform default charset)
    [Arguments("restart — now")] // an em-dash is non-ASCII
    public async Task An_injection_message_is_rejected(string message)
    {
        await Assert.That(BroadcastMessageRules.ValidateMessage(message)).IsNotNull();
    }

    [Test]
    public async Task An_over_long_message_is_rejected()
    {
        string tooLong = new('a', BroadcastMessageRules.MaxBroadcastMessageLength + 1);
        await Assert.That(BroadcastMessageRules.ValidateMessage(tooLong)).IsNotNull();
    }

    [Test]
    public async Task A_message_at_the_length_bound_is_accepted()
    {
        string atBound = new('a', BroadcastMessageRules.MaxBroadcastMessageLength);
        await Assert.That(BroadcastMessageRules.ValidateMessage(atBound)).IsNull();
    }

    [Test]
    [Arguments(300, "Server restarting in 5 minutes.")]
    [Arguments(60, "Server restarting in 1 minute.")]
    [Arguments(120, "Server restarting in 2 minutes.")]
    [Arguments(30, "Server restarting in 30 seconds.")]
    [Arguments(10, "Server restarting in 10 seconds.")]
    [Arguments(90, "Server restarting in 90 seconds.")]
    [Arguments(1, "Server restarting in 1 second.")]
    public async Task The_countdown_reads_naturally(int seconds, string expected)
    {
        await Assert.That(BroadcastMessageRules.FormatCountdown(seconds)).IsEqualTo(expected);
    }

    [Test]
    public async Task A_reason_is_appended_to_the_countdown()
    {
        await Assert.That(BroadcastMessageRules.FormatCountdown(60, "Scheduled maintenance."))
            .IsEqualTo("Server restarting in 1 minute. Scheduled maintenance.");
    }

    [Test]
    public async Task A_formatted_countdown_is_always_a_valid_message()
    {
        await Assert.That(BroadcastMessageRules.ValidateMessage(BroadcastMessageRules.FormatCountdown(300))).IsNull();
        await Assert.That(BroadcastMessageRules.ValidateMessage(
            BroadcastMessageRules.FormatCountdown(30, "Applying mod changes."))).IsNull();
    }
}
