using System.Text.Json;
using ZWarden.Agent.Console;
using ZWarden.Contracts.Protocol;
using ZWarden.Contracts.Protocol.Messages;
using ZWarden.Domain.Ids;

namespace ZWarden.Agent.Tests.Console;

/// <summary>
/// #232 budget audit: the largest one-shot Agent→Web result — a console reply at its output cap, made entirely of
/// characters the protocol JSON escapes to six bytes — must still fit under the hub's receive limit, or sending it
/// would close the Agent's connection.
/// </summary>
public class ConsoleReplySizeTests
{
    [Test]
    public async Task A_worst_case_console_reply_fits_the_hub_receive_limit()
    {
        var completed = new OperationCompleted(
            OperationOutcome.Succeeded,
            ConsoleCommand: new ConsoleCommandResult(new string('<', ConsoleAdministration.MaxOutputLength), Truncated: true));
        Envelope<OperationCompleted> envelope = Envelope.Create(
            completed, DateTimeOffset.UnixEpoch, AgentId.New(), ServerId.New(), OperationId.New());

        int bytes = JsonSerializer.SerializeToUtf8Bytes(envelope, ProtocolJson.Options).Length;

        await Assert.That(bytes).IsGreaterThan(32 * 1024);
        await Assert.That((long)bytes).IsLessThan(AgentHubProtocol.MaxReceiveMessageBytes);
    }
}
