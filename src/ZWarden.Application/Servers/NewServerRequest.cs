using ZWarden.Domain.Ids;

namespace ZWarden.Application.Servers;

/// <summary>
/// What the new-server wizard asks for (#230): the host and name, the optional host port (#229), the optional JVM heap,
/// the optional initial settings, and whether the operator acknowledged creating it beyond the host's free memory.
/// </summary>
/// <param name="AgentId">The host (Agent) to create it on.</param>
/// <param name="Name">The operator-facing name.</param>
/// <param name="GamePort">The host game port, or <c>null</c> for the next free stride.</param>
/// <param name="HeapSizeBytes">The JVM heap, or <c>null</c> for the Agent's default.</param>
/// <param name="Settings">The initial <c>servertest.ini</c> settings, or <c>null</c> to leave them to PZ.</param>
/// <param name="AcknowledgeOvercommit">The operator confirmed creating the server although its memory limit exceeds the
/// host's free memory (D1). Required only when the Agent's capacity report says it does not fit.</param>
public sealed record NewServerRequest(
    AgentId AgentId,
    string Name,
    int? GamePort = null,
    long? HeapSizeBytes = null,
    NewServerSettings? Settings = null,
    bool AcknowledgeOvercommit = false);

/// <summary>The wizard's basic settings (#230), each optional; validated by <c>InitialSettingsRules</c> at register.
/// The password is plaintext only in memory — it is encrypted before it is stored on the Operation.</summary>
/// <param name="Public">List the server publicly.</param>
/// <param name="PublicName">The server-browser name.</param>
/// <param name="MaxPlayers">The player cap.</param>
/// <param name="Password">The join password; never printed by <see cref="ToString"/>.</param>
/// <param name="WelcomeMessage">The join message.</param>
public sealed record NewServerSettings(bool? Public, string? PublicName, int? MaxPlayers, string? Password, string? WelcomeMessage)
{
    private bool PrintMembers(System.Text.StringBuilder builder)
    {
        System.Globalization.CultureInfo invariant = System.Globalization.CultureInfo.InvariantCulture;
        builder.Append(invariant, $"Public = {Public}, PublicName = {PublicName}, MaxPlayers = {MaxPlayers}, ");
        builder.Append(invariant, $"Password = {(Password is null ? string.Empty : "***")}, WelcomeMessage = {WelcomeMessage}");
        return true;
    }
}
