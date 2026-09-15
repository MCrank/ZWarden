using ZWarden.Domain.Console;

namespace ZWarden.Domain.Tests.Console;

/// <summary>
/// F28 D-1: the input-safety and command-policy rules for the remote console, shared by the Web edge (fast
/// operator feedback before an Operation is enqueued) and the Agent (defence in depth, where the command is
/// actually sent — trust-boundaries.md §8). Input safety keeps a submitted line to a single, bounded, printable
/// ASCII command (research §7 quirk 11 — PZ decodes request bodies with the platform default charset; a newline
/// or control byte desyncs framing). The command policy is a denylist that refuses the two dangerous families a
/// full-admin RCON console must not run: commands that mint or expose credentials (so the console can never be
/// used to read or set a password — "never exposing RCON credentials"), and <c>quit</c>, which bypasses F15's
/// safe stop. Everything else runs. Both functions are pure so the two enforcement sites share one contract.
/// </summary>
public class ConsoleCommandRulesTests
{
    [Test]
    [Arguments("players")]
    [Arguments("save")]
    [Arguments("servermsg \"hello world\"")]
    [Arguments("showoptions")]
    [Arguments("help")]
    [Arguments("changeoption Open false")]
    [Arguments("kickuser \"Bob\" -r \"griefing\"")]
    public async Task A_clean_command_passes_input_safety(string input)
    {
        await Assert.That(ConsoleCommandRules.ValidateInput(input)).IsNull();
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments("   ")]
    public async Task An_empty_command_is_rejected(string? input)
    {
        await Assert.That(ConsoleCommandRules.ValidateInput(input)).IsNotNull();
    }

    [Test]
    [Arguments("save\nquit")]            // a newline would submit a second line / desync framing
    [Arguments("save\r\nquit")]          // CRLF likewise
    [Arguments("save\tworld")]           // a tab is a control character
    [Arguments("save\0")]                // a NUL terminates the RCON body early
    [Arguments("servermsg café")]        // non-ASCII (PZ decodes with the platform default charset)
    public async Task An_unsafe_command_is_rejected(string input)
    {
        await Assert.That(ConsoleCommandRules.ValidateInput(input)).IsNotNull();
    }

    [Test]
    public async Task An_over_long_command_is_rejected()
    {
        string tooLong = new('a', ConsoleCommandRules.MaxInputLength + 1);
        await Assert.That(ConsoleCommandRules.ValidateInput(tooLong)).IsNotNull();
    }

    [Test]
    public async Task A_command_at_the_length_bound_is_accepted()
    {
        string atBound = new('a', ConsoleCommandRules.MaxInputLength);
        await Assert.That(ConsoleCommandRules.ValidateInput(atBound)).IsNull();
    }

    [Test]
    [Arguments("players")]
    [Arguments("save")]
    [Arguments("servermsg \"hi\"")]
    [Arguments("showoptions")]
    [Arguments("reloadoptions")]
    [Arguments("changeoption Open false")]          // a non-credential option is fine
    [Arguments("changeoption PublicName \"My Server\"")]
    [Arguments("kickuser \"Bob\"")]                  // F19 verbs are allowed here too (still audited)
    [Arguments("banuser \"Mallory\"")]
    public async Task An_allowed_command_passes_the_policy(string input)
    {
        await Assert.That(ConsoleCommandRules.EvaluatePolicy(input)).IsNull();
    }

    [Test]
    [Arguments("adduser \"bob\" \"password\"")]      // mints an account credential
    [Arguments("setpassword \"bob\" \"newpw\"")]     // sets an account password
    [Arguments("setaccesslevel \"bob\" \"admin\"")]  // grants privilege
    [Arguments("grantadmin \"bob\"")]
    [Arguments("removeadmin \"bob\"")]
    [Arguments("addsteamid \"bob\" 7656119")]
    [Arguments("removesteamid \"bob\" 7656119")]
    public async Task A_credential_command_is_denied(string input)
    {
        await Assert.That(ConsoleCommandRules.EvaluatePolicy(input)).IsNotNull();
    }

    [Test]
    public async Task Quit_is_denied_because_it_bypasses_the_safe_stop()
    {
        await Assert.That(ConsoleCommandRules.EvaluatePolicy("quit")).IsNotNull();
    }

    [Test]
    [Arguments("changeoption RCONPassword \"secret\"")]
    [Arguments("changeoption Password \"secret\"")]
    [Arguments("changeoption DiscordToken \"secret\"")]
    [Arguments("changeoption \"RCONPassword\" \"secret\"")]  // quoted option key
    public async Task Changeoption_targeting_a_credential_key_is_denied(string input)
    {
        await Assert.That(ConsoleCommandRules.EvaluatePolicy(input)).IsNotNull();
    }

    [Test]
    [Arguments("QUIT")]                              // command names match case-insensitively (research §7 quirk 10)
    [Arguments("Quit")]
    [Arguments("  adduser bob pw")]                  // leading whitespace does not evade the policy
    [Arguments("ADDUSER bob pw")]
    [Arguments("changeoption rconpassword x")]       // option key matches case-insensitively
    public async Task The_policy_is_case_and_whitespace_insensitive(string input)
    {
        await Assert.That(ConsoleCommandRules.EvaluatePolicy(input)).IsNotNull();
    }
}
