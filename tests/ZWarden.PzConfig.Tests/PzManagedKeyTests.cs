using ZWarden.PzConfig.Validation;

namespace ZWarden.PzConfig.Tests;

/// <summary>
/// #228: the INI port keys are ZWarden-managed. The container template publishes PZ's fixed 16261/16262 udp and the
/// Agent dials RCON at 27015, so an operator edit to DefaultPort/UDPPort/RCONPort would make players unable to
/// connect or break every RCON feature. The schema marks them so the editor shows them read-only and every apply
/// path refuses a change.
/// </summary>
public class PzManagedKeyTests
{
    [Test]
    [Arguments("DefaultPort")]
    [Arguments("UDPPort")]
    [Arguments("RCONPort")]
    public async Task The_ini_port_keys_are_managed(string path)
    {
        await Assert.That(PzSchema.IsManaged(PzConfigKind.Ini, path)).IsTrue();
    }

    [Test]
    public async Task Ordinary_and_unknown_keys_are_not_managed()
    {
        await Assert.That(PzSchema.IsManaged(PzConfigKind.Ini, "MaxPlayers")).IsFalse();
        await Assert.That(PzSchema.IsManaged(PzConfigKind.Ini, "SomeModKey")).IsFalse();
        await Assert.That(PzSchema.IsManaged(PzConfigKind.SandboxVars, "DefaultPort")).IsFalse();
    }

    [Test]
    public async Task Validating_an_edit_to_a_managed_key_refuses_it()
    {
        PzConfigDiagnostic? refusal = PzConfigValidator.ValidateEdit(PzConfigKind.Ini, "RCONPort", "27016");

        await Assert.That(refusal).IsNotNull();
        await Assert.That(refusal!.Message).Contains("managed by ZWarden");
    }
}
