using ZWarden.Agent.Servers;

namespace ZWarden.Agent.Tests.Servers;

/// <summary>
/// #114: the Agent-side <c>servermsg</c> builder quotes the message (research §7 quirk 10) and refuses a message
/// that fails validation before any byte reaches RCON — so an operator (or a countdown reason) can never break out
/// of the quotes into a second command. The wire text is asserted verbatim.
/// </summary>
public class ServerBroadcastCommandBuilderTests
{
    [Test]
    public async Task ServerMessage_quotes_the_message()
    {
        await Assert.That(ServerBroadcastCommandBuilder.ServerMessage("Server restarting in 5 minutes."))
            .IsEqualTo("servermsg \"Server restarting in 5 minutes.\"");
    }

    [Test]
    [Arguments("said \"quit\"")]      // an embedded quote would break out of the quoting
    [Arguments("line\nquit")]         // a newline could inject a second command
    [Arguments("")]                   // empty is meaningless
    [Arguments("café")]               // non-ASCII
    public async Task ServerMessage_refuses_an_injection_message(string message)
    {
        await Assert.That(() => ServerBroadcastCommandBuilder.ServerMessage(message))
            .Throws<BroadcastCommandException>();
    }
}
