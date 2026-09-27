using System.Text.Json.Nodes;
using ZWarden.Contracts.Protocol;
using ZWarden.Contracts.Protocol.Messages;
using ZWarden.Domain.Ids;

namespace ZWarden.Contracts.Tests.Protocol;

/// <summary>
/// #258: <see cref="CreateServer"/> gains the optional Build 42 Steam branch the server installs. Additive under
/// ADR 0020, so there's no bump: an older caller omits it and the Agent installs the public branch.
/// </summary>
public class ServerBranchMessagesTests
{
    private static readonly DateTimeOffset At = new(2026, 9, 27, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task CreateServer_round_trips_a_branch()
    {
        Envelope<CreateServer> original = Envelope.Create(
            new CreateServer(Branch: "42.19"), At, serverId: ServerId.New(), operationId: OperationId.New());

        Envelope<CreateServer> back = ProtocolJson.Deserialize<CreateServer>(ProtocolJson.Serialize(original));

        await Assert.That(back.Payload.Branch).IsEqualTo("42.19");
    }

    [Test]
    public async Task CreateServer_from_an_older_caller_has_no_branch()
    {
        string json = ProtocolJson.Serialize(Envelope.Create(
            new CreateServer(GamePort: 27015, Branch: "unstable"), At, serverId: ServerId.New(), operationId: OperationId.New()));
        JsonNode node = JsonNode.Parse(json)!;
        node["payload"]!.AsObject().Remove("branch");

        Envelope<CreateServer> back = ProtocolJson.Deserialize<CreateServer>(node.ToJsonString());

        await Assert.That(back.Payload.GamePort).IsEqualTo(27015);
        await Assert.That(back.Payload.Branch).IsNull();
    }
}
