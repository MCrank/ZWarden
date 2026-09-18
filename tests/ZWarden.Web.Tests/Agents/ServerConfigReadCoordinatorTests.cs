using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using ZWarden.Application.Agents;
using ZWarden.Application.Configuration;
using ZWarden.Contracts.Protocol;
using ZWarden.Contracts.Protocol.Messages;
using ZWarden.Domain.Configuration;
using ZWarden.Domain.Ids;
using ZWarden.Web.Agents;

namespace ZWarden.Web.Tests.Agents;

/// <summary>
/// F20c (ADR 0041): the live-configuration read coordinator. It sends a correlated request to the owning Agent's
/// connection and reassembles the chunked reply the hub feeds back, completing the awaiting read. An offline Agent
/// is reported at once; a connected-but-silent one times out; a reply is accepted only from the Agent the read was
/// routed to and only against a live correlation id (the ownership guard).
/// </summary>
public class ServerConfigReadCoordinatorTests
{
    private const string Conn = "conn-1";
    private static readonly DateTimeOffset Now = new(2026, 9, 18, 12, 0, 0, TimeSpan.Zero);

    private static ConfigReadPayload Payload(ServerId server, string rawText) =>
        new(
            server, PzConfigFile.SandboxVars, ConfigReadStatus.Read,
            [new ConfigSettingValue("Zombies", ConfigValueKind.Number, "3", "How fast the zombies move.")],
            rawText, "hash-1", []);

    private static (ServerConfigReadCoordinator Coordinator, RecordingHubClients Sends) Build(
        AgentId agent, bool online = true, TimeSpan? timeout = null)
    {
        var registry = new FakeRegistry();
        if (online)
        {
            registry.Connect(agent, Conn);
        }

        var clients = new RecordingHubClients();
        var coordinator = new ServerConfigReadCoordinator(
            registry, new FakeHubContext(clients), TimeProvider.System,
            NullLogger<ServerConfigReadCoordinator>.Instance)
        {
            ReadTimeout = timeout ?? ServerConfigReadCoordinator.DefaultReadTimeout,
        };
        return (coordinator, clients);
    }

    private static void Reply(
        ServerConfigReadCoordinator coordinator, AgentId reportingAgent, ServerId server, string correlationId,
        ConfigReadPayload payload, bool reverse = false)
    {
        List<ServerConfigContent> chunks = [.. ServerConfigContentCodec.Encode(correlationId, payload)];
        if (reverse)
        {
            chunks.Reverse();
        }

        foreach (ServerConfigContent chunk in chunks)
        {
            coordinator.AcceptChunk(reportingAgent, Envelope.Create(chunk, Now, reportingAgent, server));
        }
    }

    [Test]
    public async Task An_offline_agent_is_reported_at_once()
    {
        AgentId agent = AgentId.New();
        (ServerConfigReadCoordinator coordinator, RecordingHubClients sends) = Build(agent, online: false);

        ConfigReadTransfer transfer = await coordinator.ReadAsync(ServerId.New(), agent, PzConfigFile.SandboxVars);

        await Assert.That(transfer.Status).IsEqualTo(ConfigTransferStatus.AgentOffline);
        await Assert.That(sends.Sent).IsEmpty();
    }

    [Test]
    public async Task A_reply_from_the_owning_agent_reassembles_into_the_view()
    {
        AgentId agent = AgentId.New();
        ServerId server = ServerId.New();
        (ServerConfigReadCoordinator coordinator, RecordingHubClients sends) = Build(agent);

        Task<ConfigReadTransfer> read = coordinator.ReadAsync(server, agent, PzConfigFile.SandboxVars);

        // The request went to the owning Agent's connection with the file and a correlation id.
        await Assert.That(sends.Sent).HasSingleItem();
        await Assert.That(sends.Sent[0].ConnectionId).IsEqualTo(Conn);
        await Assert.That(sends.Sent[0].Method).IsEqualTo(AgentHubProtocol.RequestServerConfigRead);
        await Assert.That(sends.Sent[0].Args[1]).IsEqualTo(PzConfigFile.SandboxVars.ToString());
        string correlationId = (string)sends.Sent[0].Args[2]!;

        Reply(coordinator, agent, server, correlationId, Payload(server, "SandboxVars = {}\n"));

        ConfigReadTransfer transfer = await read;
        await Assert.That(transfer.Status).IsEqualTo(ConfigTransferStatus.Read);
        await Assert.That(transfer.RawText).IsEqualTo("SandboxVars = {}\n");
        await Assert.That(transfer.BaselineHash).IsEqualTo("hash-1");
        await Assert.That(transfer.Settings.Single().Path).IsEqualTo("Zombies");
        await Assert.That(transfer.Settings.Single().Kind).IsEqualTo(ConfigEditKind.Number);
        await Assert.That(transfer.Settings.Single().Comment).IsEqualTo("How fast the zombies move.");
    }

    [Test]
    public async Task A_multi_chunk_reply_reassembles_regardless_of_arrival_order()
    {
        AgentId agent = AgentId.New();
        ServerId server = ServerId.New();
        (ServerConfigReadCoordinator coordinator, RecordingHubClients sends) = Build(agent);

        Task<ConfigReadTransfer> read = coordinator.ReadAsync(server, agent, PzConfigFile.SandboxVars);
        string correlationId = (string)sends.Sent[0].Args[2]!;

        // A large raw text forces several chunks; feed them in reverse.
        Reply(coordinator, agent, server, correlationId, Payload(server, new string('x', 90_000)), reverse: true);

        ConfigReadTransfer transfer = await read;
        await Assert.That(transfer.Status).IsEqualTo(ConfigTransferStatus.Read);
        await Assert.That(transfer.RawText.Length).IsEqualTo(90_000);
    }

    [Test]
    public async Task A_reply_from_a_foreign_agent_is_ignored()
    {
        AgentId agent = AgentId.New();
        AgentId foreign = AgentId.New();
        ServerId server = ServerId.New();
        (ServerConfigReadCoordinator coordinator, RecordingHubClients sends) = Build(agent);

        Task<ConfigReadTransfer> read = coordinator.ReadAsync(server, agent, PzConfigFile.SandboxVars);
        string correlationId = (string)sends.Sent[0].Args[2]!;

        // A reply forged by another Agent for this read is dropped: the read stays pending.
        Reply(coordinator, foreign, server, correlationId, Payload(server, "forged"));
        await Assert.That(read.IsCompleted).IsFalse();

        // The true owner's reply completes it.
        Reply(coordinator, agent, server, correlationId, Payload(server, "genuine"));
        ConfigReadTransfer transfer = await read;
        await Assert.That(transfer.RawText).IsEqualTo("genuine");
    }

    [Test]
    public async Task A_reply_for_an_unknown_correlation_is_ignored()
    {
        AgentId agent = AgentId.New();
        ServerId server = ServerId.New();
        (ServerConfigReadCoordinator coordinator, _) = Build(agent);

        // No read is outstanding: a stray chunk must be a harmless no-op, not a throw.
        Reply(coordinator, agent, server, "no-such-correlation", Payload(server, "stray"));
    }

    [Test]
    public async Task A_silent_agent_times_out()
    {
        AgentId agent = AgentId.New();
        (ServerConfigReadCoordinator coordinator, _) = Build(agent, timeout: TimeSpan.FromMilliseconds(100));

        ConfigReadTransfer transfer = await coordinator.ReadAsync(ServerId.New(), agent, PzConfigFile.SandboxVars);

        await Assert.That(transfer.Status).IsEqualTo(ConfigTransferStatus.TimedOut);
    }

    private sealed class FakeRegistry : IAgentConnectionRegistry
    {
        private readonly Dictionary<AgentId, string> _connections = [];

        public void Connect(AgentId agentId, string connectionId) => _connections[agentId] = connectionId;

        public string? GetConnectionId(AgentId agentId) => _connections.GetValueOrDefault(agentId);

        public bool IsConnected(AgentId agentId) => _connections.ContainsKey(agentId);

        public void Register(AgentId agentId, string connectionId, Action abort) => throw new NotSupportedException();

        public void Remove(string connectionId) => throw new NotSupportedException();

        public bool TryAbort(AgentId agentId) => throw new NotSupportedException();
    }

    private sealed record Send(string ConnectionId, string Method, object?[] Args);

    private sealed class FakeHubContext(RecordingHubClients clients) : IHubContext<AgentHub>
    {
        public IHubClients Clients { get; } = clients;

        public IGroupManager Groups => throw new NotSupportedException();
    }

    private sealed class RecordingHubClients : IHubClients
    {
        public List<Send> Sent { get; } = [];

        public IClientProxy Client(string connectionId) => new RecordingProxy(connectionId, this);

        public IClientProxy All => throw new NotSupportedException();

        public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => throw new NotSupportedException();

        public IClientProxy Clients(IReadOnlyList<string> connectionIds) => throw new NotSupportedException();

        public IClientProxy Group(string groupName) => throw new NotSupportedException();

        public IClientProxy Groups(IReadOnlyList<string> groupNames) => throw new NotSupportedException();

        public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => throw new NotSupportedException();

        public IClientProxy User(string userId) => throw new NotSupportedException();

        public IClientProxy Users(IReadOnlyList<string> userIds) => throw new NotSupportedException();
    }

    private sealed class RecordingProxy(string connectionId, RecordingHubClients owner) : IClientProxy
    {
        public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
        {
            owner.Sent.Add(new Send(connectionId, method, args));
            return Task.CompletedTask;
        }
    }
}
