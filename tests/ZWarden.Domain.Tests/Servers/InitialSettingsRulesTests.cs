using ZWarden.Domain.Servers;

namespace ZWarden.Domain.Tests.Servers;

/// <summary>
/// #230: the wizard's basic settings are seeded verbatim as <c>key=value</c> lines, so a value is accepted only when it
/// cannot end the line (no control characters) and stays printable ASCII; <c>MaxPlayers</c> follows PZ's 1–254.
/// </summary>
public class InitialSettingsRulesTests
{
    [Test]
    [Arguments(1)]
    [Arguments(32)]
    [Arguments(254)]
    public async Task MaxPlayers_in_PZs_range_is_valid(int players)
    {
        await Assert.That(InitialSettingsRules.ValidateMaxPlayers(players)).IsNull();
    }

    [Test]
    [Arguments(0)]
    [Arguments(255)]
    [Arguments(-3)]
    public async Task MaxPlayers_outside_PZs_range_is_rejected(int players)
    {
        await Assert.That(InitialSettingsRules.ValidateMaxPlayers(players)).IsNotNull();
    }

    [Test]
    public async Task Absent_and_ordinary_text_values_are_valid()
    {
        await Assert.That(InitialSettingsRules.ValidatePublicName(null)).IsNull();
        await Assert.That(InitialSettingsRules.ValidatePublicName("Friends of Knox = fun")).IsNull();
        await Assert.That(InitialSettingsRules.ValidatePassword("p@ss word!")).IsNull();
        await Assert.That(InitialSettingsRules.ValidateWelcomeMessage("Welcome <LINE> be nice")).IsNull();
    }

    [Test]
    [Arguments("Knox\nRCONPassword=pwned")]
    [Arguments("Knox\rX=1")]
    [Arguments("tab\there")]
    [Arguments("Knöx")]
    public async Task A_value_that_could_break_the_line_or_is_not_printable_ASCII_is_rejected(string value)
    {
        await Assert.That(InitialSettingsRules.ValidatePublicName(value)).IsNotNull();
        await Assert.That(InitialSettingsRules.ValidateWelcomeMessage(value)).IsNotNull();
    }

    [Test]
    public async Task Over_long_values_are_rejected()
    {
        await Assert.That(InitialSettingsRules.ValidatePublicName(new string('a', 65))).IsNotNull();
        await Assert.That(InitialSettingsRules.ValidatePassword(new string('a', 65))).IsNotNull();
        await Assert.That(InitialSettingsRules.ValidateWelcomeMessage(new string('a', 501))).IsNotNull();
    }

    [Test]
    public async Task A_rejected_password_reason_never_echoes_the_password()
    {
        string? reason = InitialSettingsRules.ValidatePassword("secret\nvalue");

        await Assert.That(reason).IsNotNull();
        await Assert.That(reason!).DoesNotContain("secret");
    }
}
