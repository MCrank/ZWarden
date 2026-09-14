using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ZWarden.Application.Agents;
using ZWarden.Application.Servers;
using ZWarden.Contracts.Protocol;
using ZWarden.Contracts.Protocol.Messages;
using ZWarden.Domain.Agents;
using ZWarden.Domain.Audit;
using ZWarden.Domain.Ids;
using ZWarden.Infrastructure.Agents;
using ZWarden.Infrastructure.Persistence;
using ZWarden.Web.Agents;
using ZWarden.Web.Tests.Account;

namespace ZWarden.Web.Tests.Agents;

/// <summary>
/// F10 S4: end-to-end over a real SignalR client against the booted host. The handshake authenticates the
/// Agent by its F9 per-Agent credential (untrusted is rejected before any hub method), the first
/// <see cref="AgentHub.Hello"/> negotiates the protocol version (ADR 0020), the connection is tracked in the
/// registry and persisted, heartbeats advance last-seen, and disconnect clears it — all audited. LongPolling
/// transport keeps the test to the in-memory <c>TestServer</c> handler.
/// </summary>
public class AgentHubIntegrationTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 12, 10, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task An_unauthenticated_connection_is_rejected_at_the_handshake()
    {
        await using ZWardenWebAppFactory factory = new();
        _ = factory.Services; // force host start
        await using HubConnection connection = BuildConnection(factory, credential: null);

        await Assert.That(async () => await connection.StartAsync()).ThrowsException();
    }

    [Test]
    public async Task An_invalid_credential_is_rejected_and_audited()
    {
        await using ZWardenWebAppFactory factory = new();
        _ = factory.Services;
        await using HubConnection connection = BuildConnection(factory, credential: "zwa_not-a-real-credential");

        await Assert.That(async () => await connection.StartAsync()).ThrowsException();
        await Assert.That(await HasAuditActionAsync(factory, AgentConnectionAuditActions.ConnectionRejected)).IsTrue();
    }

    [Test]
    public async Task An_enrolled_agent_connects_negotiates_heartbeats_and_disconnects()
    {
        await using ZWardenWebAppFactory factory = new();
        (AgentId agentId, string credential) = await SeedTrustedAgentAsync(factory);
        await using HubConnection connection = BuildConnection(factory, credential);

        await connection.StartAsync();

        ProtocolNegotiationResult negotiation = await connection.InvokeAsync<ProtocolNegotiationResult>(
            AgentHubProtocol.Hello, Hello(agentId, ProtocolVersion.Current));
        await Assert.That(negotiation.IsCompatible).IsTrue();

        IAgentConnectionRegistry registry = factory.Services.GetRequiredService<IAgentConnectionRegistry>();
        await Assert.That(registry.IsConnected(agentId)).IsTrue();

        await connection.InvokeAsync(
            AgentHubProtocol.StateSnapshot,
            Snapshot(agentId, AgentStateSnapshot.Empty));
        await connection.InvokeAsync(AgentHubProtocol.Heartbeat, Heartbeat(agentId));

        Agent afterHeartbeat = await LoadAgentAsync(factory, agentId);
        await Assert.That(afterHeartbeat.ConnectionState).IsEqualTo(AgentConnectionState.Connected);
        await Assert.That(afterHeartbeat.LastProtocolVersion).IsEqualTo(ProtocolVersion.Current);
        await Assert.That(afterHeartbeat.LastSeenAt).IsNotNull();

        await Assert.That(await HasAuditActionAsync(factory, AgentConnectionAuditActions.Connected)).IsTrue();

        await connection.StopAsync();

        // OnDisconnectedAsync clears the registry then persists; poll the durable state so we don't race the write.
        await WaitUntilAsync(async () =>
            !registry.IsConnected(agentId)
            && (await LoadAgentAsync(factory, agentId)).ConnectionState == AgentConnectionState.Disconnected);
        await Assert.That(registry.IsConnected(agentId)).IsFalse();
        await Assert.That((await LoadAgentAsync(factory, agentId)).ConnectionState)
            .IsEqualTo(AgentConnectionState.Disconnected);
    }

    [Test]
    public async Task An_incompatible_protocol_version_is_rejected_at_hello()
    {
        await using ZWardenWebAppFactory factory = new();
        (AgentId agentId, string credential) = await SeedTrustedAgentAsync(factory);
        await using HubConnection connection = BuildConnection(factory, credential);

        await connection.StartAsync();

        ProtocolNegotiationResult negotiation = await connection.InvokeAsync<ProtocolNegotiationResult>(
            AgentHubProtocol.Hello, Hello(agentId, ProtocolVersion.Current + 1));

        await Assert.That(negotiation.IsCompatible).IsFalse();
        await Assert.That(negotiation.Status).IsEqualTo(ProtocolCompatibilityStatus.AboveCurrent);
        await Assert.That(negotiation.RejectionReason).IsNotNull();
    }

    [Test]
    public async Task An_agent_reports_state_and_health_that_persist_on_an_owned_server()
    {
        await using ZWardenWebAppFactory factory = new();
        (AgentId agentId, string credential) = await SeedTrustedAgentAsync(factory);
        ServerId serverId = await SeedServerAsync(factory, agentId);
        await using HubConnection connection = BuildConnection(factory, credential);

        await connection.StartAsync();
        await connection.InvokeAsync<ProtocolNegotiationResult>(
            AgentHubProtocol.Hello, Hello(agentId, ProtocolVersion.Current));

        // A full snapshot carrying health, then an incremental health transition.
        await connection.InvokeAsync(
            AgentHubProtocol.StateSnapshot,
            Snapshot(agentId, new AgentStateSnapshot(
                [new ServerState(serverId, ServerRunState.Running, ServerHealth.Healthy)])));
        await connection.InvokeAsync(
            AgentHubProtocol.HealthChanged,
            Envelope.Create(
                new HealthChanged(serverId, ServerHealth.Degraded, "a port is unreachable", Breakdown()),
                Now, agentId, serverId));
        await connection.InvokeAsync(
            AgentHubProtocol.ServerStateChanged,
            Envelope.Create(new ServerStateChanged(serverId, ServerRunState.Stopping), Now, agentId, serverId));

        await WaitUntilAsync(async () =>
        {
            Domain.Servers.Server s = await LoadServerAsync(factory, serverId);
            return s is { LastHealth: Domain.Servers.ServerHealth.Degraded, LastRunState: Domain.Servers.ServerRunState.Stopping };
        });

        Domain.Servers.Server server = await LoadServerAsync(factory, serverId);
        await Assert.That(server.LastHealth).IsEqualTo(Domain.Servers.ServerHealth.Degraded);
        await Assert.That(server.LastRunState).IsEqualTo(Domain.Servers.ServerRunState.Stopping);
        await Assert.That(server.LastHealthReportedAt).IsNotNull();

        // The live health cache also carries the rollup + reason for the interactive detail panel.
        IServerHealthCache healthCache = factory.Services.GetRequiredService<IServerHealthCache>();
        ServerLiveHealth? live = healthCache.GetLatest(serverId, agentId);
        await Assert.That(live).IsNotNull();
        await Assert.That(live!.Health).IsEqualTo(Domain.Servers.ServerHealth.Degraded);
        await Assert.That(live.Reason).IsEqualTo("a port is unreachable");

        await connection.StopAsync();
    }

    [Test]
    public async Task An_agent_metrics_report_lands_in_the_cache_under_ownership()
    {
        await using ZWardenWebAppFactory factory = new();
        (AgentId agentId, string credential) = await SeedTrustedAgentAsync(factory);
        ServerId serverId = await SeedServerAsync(factory, agentId);
        await using HubConnection connection = BuildConnection(factory, credential);

        await connection.StartAsync();
        await connection.InvokeAsync<ProtocolNegotiationResult>(
            AgentHubProtocol.Hello, Hello(agentId, ProtocolVersion.Current));

        await connection.InvokeAsync(
            AgentHubProtocol.MetricsReport,
            Envelope.Create(
                new ServerMetricsReport(
                    [new ServerMetricsSample(serverId, 37.5, 2_000_000_000, 4_000_000_000, 10L, 100L, null, Now)]),
                Now, agentId));

        IServerMetricsCache cache = factory.Services.GetRequiredService<IServerMetricsCache>();
        await WaitUntilAsync(() => Task.FromResult(cache.GetLatest(serverId, agentId) is not null));

        Application.Servers.ServerMetrics? latest = cache.GetLatest(serverId, agentId);
        await Assert.That(latest).IsNotNull();
        await Assert.That(latest!.CpuPercent).IsEqualTo(37.5);
        // Ownership guard: another Agent cannot read this Server's sample.
        await Assert.That(cache.GetLatest(serverId, AgentId.New())).IsNull();

        await connection.StopAsync();
    }

    [Test]
    public async Task An_agent_log_batch_lands_in_the_buffer_under_ownership()
    {
        await using ZWardenWebAppFactory factory = new();
        (AgentId agentId, string credential) = await SeedTrustedAgentAsync(factory);
        ServerId serverId = await SeedServerAsync(factory, agentId);
        await using HubConnection connection = BuildConnection(factory, credential);

        await connection.StartAsync();
        await connection.InvokeAsync<ProtocolNegotiationResult>(
            AgentHubProtocol.Hello, Hello(agentId, ProtocolVersion.Current));

        await connection.InvokeAsync(
            AgentHubProtocol.ServerLogBatch,
            Envelope.Create(
                new ServerLogBatch(
                    serverId,
                    [new ServerLogLine(1, Now, LogStreamKind.Stderr, "java exception", Truncated: false)],
                    Dropped: false),
                Now, agentId, serverId));

        IServerLogBuffer buffer = factory.Services.GetRequiredService<IServerLogBuffer>();
        await WaitUntilAsync(() => Task.FromResult(buffer.Read(serverId, agentId, 0).Lines.Count > 0));

        ServerLogSlice slice = buffer.Read(serverId, agentId, 0);
        await Assert.That(slice.Lines[0].Text).IsEqualTo("java exception");
        await Assert.That(slice.Lines[0].IsStderr).IsTrue();
        // Ownership guard: another Agent cannot read this Server's lines.
        await Assert.That(buffer.Read(serverId, AgentId.New(), 0).Lines).IsEmpty();

        await connection.StopAsync();
    }

    private static HealthBreakdown Breakdown() => new(
        new ProbeCheck(ProbeStatus.Pass),
        new ProbeCheck(ProbeStatus.Pass),
        new ProbeCheck(ProbeStatus.Pass),
        new ProbeCheck(ProbeStatus.Fail, "query port unreachable"));

    private static HubConnection BuildConnection(ZWardenWebAppFactory factory, string? credential)
        => new HubConnectionBuilder()
            .WithUrl(new Uri(factory.Server.BaseAddress, AgentHubProtocol.Path), options =>
            {
                options.Transports = HttpTransportType.LongPolling;
                options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                if (credential is not null)
                {
                    options.AccessTokenProvider = () => Task.FromResult<string?>(credential);
                }
            })
            .AddJsonProtocol(o => o.PayloadSerializerOptions = ProtocolJson.Options)
            .Build();

    private static Envelope<AgentHello> Hello(AgentId agentId, int protocolVersion) => new()
    {
        ProtocolVersion = protocolVersion,
        MessageId = MessageId.New(),
        Timestamp = Now,
        AgentId = agentId,
        Payload = new AgentHello(agentId),
    };

    private static Envelope<AgentHeartbeat> Heartbeat(AgentId agentId)
        => Envelope.Create(new AgentHeartbeat(AgentHealthStatus.Healthy), Now, agentId);

    private static Envelope<AgentStateSnapshot> Snapshot(AgentId agentId, AgentStateSnapshot snapshot)
        => Envelope.Create(snapshot, Now, agentId);

    private static async Task<(AgentId AgentId, string Credential)> SeedTrustedAgentAsync(ZWardenWebAppFactory factory)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ICredentialHasher hasher = scope.ServiceProvider.GetRequiredService<ICredentialHasher>();
        ZWardenDbContext context = scope.ServiceProvider.GetRequiredService<ZWardenDbContext>();

        Domain.Security.SecretString credential = hasher.Generate("zwa");
        Agent agent = Agent.Enroll(hasher.Hash(credential), EnrollmentId.New(), DateTimeOffset.UtcNow);
        context.Add(agent);
        await context.SaveChangesAsync();
        return (agent.Id, credential.Reveal());
    }

    private static async Task<Agent> LoadAgentAsync(ZWardenWebAppFactory factory, AgentId agentId)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        AgentRepository repo = new(scope.ServiceProvider.GetRequiredService<ZWardenDbContext>());
        return (await repo.FindByIdAsync(agentId))!;
    }

    private static async Task<ServerId> SeedServerAsync(ZWardenWebAppFactory factory, AgentId agentId)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ZWardenDbContext context = scope.ServiceProvider.GetRequiredService<ZWardenDbContext>();
        // The ownership interceptor stamps the ambient (default) tenant on insert (ADR 0016), the same tenant the
        // hub reconciles under — so the reported transitions resolve to this Server.
        Domain.Servers.Server server = Domain.Servers.Server.Import(agentId, ServerId.New(), "alpha", Now);
        context.Add(server);
        await context.SaveChangesAsync();
        return server.Id;
    }

    private static async Task<Domain.Servers.Server> LoadServerAsync(ZWardenWebAppFactory factory, ServerId serverId)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ZWardenDbContext context = scope.ServiceProvider.GetRequiredService<ZWardenDbContext>();
        return (await context.Set<Domain.Servers.Server>().FirstAsync(s => s.Id == serverId));
    }

    private static async Task<bool> HasAuditActionAsync(ZWardenWebAppFactory factory, string action)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ZWardenDbContext context = scope.ServiceProvider.GetRequiredService<ZWardenDbContext>();
        return await context.Set<AuditEvent>().AnyAsync(e => e.Action == action);
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        for (int i = 0; i < 50 && !await condition(); i++)
        {
            await Task.Delay(50);
        }
    }
}
