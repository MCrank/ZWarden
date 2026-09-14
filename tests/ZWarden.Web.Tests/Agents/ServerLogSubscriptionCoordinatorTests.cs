using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using ZWarden.Application.Agents;
using ZWarden.Contracts.Protocol;
using ZWarden.Domain.Ids;
using ZWarden.Web.Agents;

namespace ZWarden.Web.Tests.Agents;

/// <summary>
/// F27: the viewer ref-count coordinator. It drives the owning Agent's follow on the first viewer and off the
/// last, sends nothing in between, and is a no-op toward an offline Agent.
/// </summary>
public class ServerLogSubscriptionCoordinatorTests
{
    private const string Conn = "conn-1";

    private static (ServerLogSubscriptionCoordinator Coordinator, RecordingHubClients Sends) Build(
        AgentId agent, bool online = true)
    {
        var registry = new FakeRegistry();
        if (online)
        {
            registry.Connect(agent, Conn);
        }

        var clients = new RecordingHubClients();
        var coordinator = new ServerLogSubscriptionCoordinator(
            registry, new FakeHubContext(clients), NullLogger<ServerLogSubscriptionCoordinator>.Instance);
        return (coordinator, clients);
    }

    [Test]
    public async Task The_first_viewer_starts_the_stream_on_the_owning_agents_connection()
    {
        AgentId agent = AgentId.New();
        ServerId server = ServerId.New();
        (ServerLogSubscriptionCoordinator coordinator, RecordingHubClients sends) = Build(agent);

        await coordinator.SubscribeAsync(server, agent, CancellationToken.None);

        await Assert.That(sends.Sent).HasSingleItem();
        await Assert.That(sends.Sent[0].ConnectionId).IsEqualTo(Conn);
        await Assert.That(sends.Sent[0].Method).IsEqualTo(AgentHubProtocol.StartServerLogStream);
        await Assert.That(sends.Sent[0].Argument).IsEqualTo(server.ToString());
    }

    [Test]
    public async Task A_second_viewer_does_not_start_the_stream_again()
    {
        AgentId agent = AgentId.New();
        ServerId server = ServerId.New();
        (ServerLogSubscriptionCoordinator coordinator, RecordingHubClients sends) = Build(agent);

        await coordinator.SubscribeAsync(server, agent, CancellationToken.None);
        await coordinator.SubscribeAsync(server, agent, CancellationToken.None);

        await Assert.That(sends.Sent).HasSingleItem();
    }

    [Test]
    public async Task Stopping_happens_only_when_the_last_viewer_leaves()
    {
        AgentId agent = AgentId.New();
        ServerId server = ServerId.New();
        (ServerLogSubscriptionCoordinator coordinator, RecordingHubClients sends) = Build(agent);

        await coordinator.SubscribeAsync(server, agent, CancellationToken.None);
        await coordinator.SubscribeAsync(server, agent, CancellationToken.None);
        await coordinator.UnsubscribeAsync(server, agent, CancellationToken.None); // one viewer remains
        await Assert.That(sends.Sent.Count(s => s.Method == AgentHubProtocol.StopServerLogStream)).IsEqualTo(0);

        await coordinator.UnsubscribeAsync(server, agent, CancellationToken.None); // last viewer leaves
        await Assert.That(sends.Sent.Count(s => s.Method == AgentHubProtocol.StopServerLogStream)).IsEqualTo(1);
    }

    [Test]
    public async Task An_offline_agent_is_a_no_op()
    {
        AgentId agent = AgentId.New();
        ServerId server = ServerId.New();
        (ServerLogSubscriptionCoordinator coordinator, RecordingHubClients sends) = Build(agent, online: false);

        await coordinator.SubscribeAsync(server, agent, CancellationToken.None);

        await Assert.That(sends.Sent).IsEmpty();
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

    private sealed record Send(string ConnectionId, string Method, object? Argument);

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
            owner.Sent.Add(new Send(connectionId, method, args.Length > 0 ? args[0] : null));
            return Task.CompletedTask;
        }
    }
}
