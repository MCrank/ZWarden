using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using ZWarden.Application.Agents;
using ZWarden.Application.Configuration;
using ZWarden.Application.Mods;
using ZWarden.Application.Operations;
using ZWarden.Contracts.Protocol;
using ZWarden.Contracts.Protocol.Messages;
using ZWarden.Domain.Agents;
using ZWarden.Domain.Audit;
using ZWarden.Domain.Configuration;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Operations;
using ZWarden.Infrastructure.Agents;
using ZWarden.Infrastructure.Configuration;
using ZWarden.Infrastructure.Operations;
using ZWarden.Infrastructure.Persistence;
using ZWarden.Web.Agents;
using ZWarden.Web.Tests.Account;

namespace ZWarden.Web.Tests.Operations;

/// <summary>
/// F11 PR-B end-to-end over a real SignalR client against the booted host: an operator enqueues a
/// <c>Diagnostics.Ping</c>, the dispatcher sends the command down the F10 connection, the Agent (its real
/// <see cref="AgentCommandProcessor"/>) replies, and the hub ingest drives the operation to
/// <see cref="OperationState.Succeeded"/>. With no connected Agent the operation stays
/// <see cref="OperationState.Pending"/>. LongPolling keeps the test on the in-memory <c>TestServer</c>.
/// </summary>
public class OperationDispatchIntegrationTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 12, 10, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task A_ping_runs_end_to_end_to_succeeded()
    {
        await using ZWardenWebAppFactory factory = new();
        (AgentId agentId, string credential) = await SeedTrustedAgentAsync(factory);
        await using HubConnection connection = BuildConnection(factory, credential);

        // Stand in for the Agent: receive the dispatched command and report the result on the same operation.
        // (The Agent's real command processing is unit-tested in ZWarden.Agent.Tests.)
        connection.On<string>(AgentHubProtocol.ReceiveCommand, async json =>
        {
            Envelope<IProtocolMessage> command = ProtocolJson.Deserialize(json);
            if (command.Payload is PingAgent && command.OperationId is { } operationId)
            {
                Envelope<OperationCompleted> reply = Envelope.Create(
                    new OperationCompleted(OperationOutcome.Succeeded), Now, operationId: operationId);
                await connection.SendAsync(AgentHubProtocol.OperationCompleted, reply);
            }
        });

        await connection.StartAsync();
        await connection.InvokeAsync<ProtocolNegotiationResult>(AgentHubProtocol.Hello, Hello(agentId));

        OperationId operationId;
        using (IServiceScope scope = factory.Services.CreateScope())
        {
            IOperationCoordinator coordinator = scope.ServiceProvider.GetRequiredService<IOperationCoordinator>();
            Operation op = await coordinator.EnqueueAsync(
                new EnqueueOperationRequest(agentId, OperationKind.DiagnosticsPing, IsMutating: false, "e2e-ping"));
            operationId = op.Id;
        }

        Operation? final = await WaitForStateAsync(factory, operationId, OperationState.Succeeded);
        await Assert.That(final).IsNotNull();
        await Assert.That(final!.State).IsEqualTo(OperationState.Succeeded);
        await Assert.That(final.PercentComplete).IsEqualTo(100);
    }

    [Test]
    public async Task A_docker_health_probe_runs_end_to_end_to_succeeded()
    {
        await using ZWardenWebAppFactory factory = new();
        (AgentId agentId, string credential) = await SeedTrustedAgentAsync(factory);
        await using HubConnection connection = BuildConnection(factory, credential);

        // The dispatcher must map DiagnosticsDockerHealth to the ProbeDockerHealth command (F13). The stand-in
        // Agent verifies it received exactly that, then reports success. (The real probe is unit-tested.)
        bool receivedDockerHealthCommand = false;
        connection.On<string>(AgentHubProtocol.ReceiveCommand, async json =>
        {
            Envelope<IProtocolMessage> command = ProtocolJson.Deserialize(json);
            if (command.Payload is ProbeDockerHealth && command.OperationId is { } operationId)
            {
                receivedDockerHealthCommand = true;
                Envelope<OperationCompleted> reply = Envelope.Create(
                    new OperationCompleted(OperationOutcome.Succeeded), Now, operationId: operationId);
                await connection.SendAsync(AgentHubProtocol.OperationCompleted, reply);
            }
        });

        await connection.StartAsync();
        await connection.InvokeAsync<ProtocolNegotiationResult>(AgentHubProtocol.Hello, Hello(agentId));

        OperationId operationId;
        using (IServiceScope scope = factory.Services.CreateScope())
        {
            IOperationCoordinator coordinator = scope.ServiceProvider.GetRequiredService<IOperationCoordinator>();
            Operation op = await coordinator.EnqueueAsync(
                new EnqueueOperationRequest(agentId, OperationKind.DiagnosticsDockerHealth, IsMutating: false, "e2e-docker-health"));
            operationId = op.Id;
        }

        Operation? final = await WaitForStateAsync(factory, operationId, OperationState.Succeeded);
        await Assert.That(receivedDockerHealthCommand).IsTrue();
        await Assert.That(final).IsNotNull();
        await Assert.That(final!.State).IsEqualTo(OperationState.Succeeded);
    }

    [Test]
    public async Task A_config_apply_runs_end_to_end_and_records_a_revision()
    {
        await using ZWardenWebAppFactory factory = new();
        (AgentId agentId, string credential) = await SeedTrustedAgentAsync(factory);
        ServerId serverId = await SeedServerAsync(factory, agentId);
        await using HubConnection connection = BuildConnection(factory, credential);

        // The stand-in Agent verifies it received a ConfigApply carrying the file, baseline and edits from the
        // command payload, then reports the recorded revision. (The real drift-checked write is unit-tested.)
        ConfigApply? received = null;
        connection.On<string>(AgentHubProtocol.ReceiveCommand, async json =>
        {
            Envelope<IProtocolMessage> command = ProtocolJson.Deserialize(json);
            if (command.Payload is ConfigApply apply && command.OperationId is { } operationId)
            {
                received = apply;
                Envelope<OperationCompleted> reply = Envelope.Create(
                    new OperationCompleted(
                        OperationOutcome.Succeeded,
                        Config: new ConfigApplyResult(apply.File, "new-hash-42", "[[\"Zombies\",\"n:1:i\"]]", 1)),
                    Now, serverId: command.ServerId, operationId: operationId);
                await connection.SendAsync(AgentHubProtocol.OperationCompleted, reply);
            }
        });

        await connection.StartAsync();
        await connection.InvokeAsync<ProtocolNegotiationResult>(AgentHubProtocol.Hello, Hello(agentId));

        OperationId operationId;
        using (IServiceScope scope = factory.Services.CreateScope())
        {
            IOperationCoordinator coordinator = scope.ServiceProvider.GetRequiredService<IOperationCoordinator>();
            string payload = new ConfigApplyPayload(
                PzConfigFile.SandboxVars, "base-1", [new ConfigApplyEdit("Zombies", ConfigEditKind.Number, "1")]).ToJson();
            Operation op = await coordinator.EnqueueAsync(
                new EnqueueOperationRequest(
                    agentId, OperationKind.ConfigApply, IsMutating: true, "e2e-config-apply",
                    ServerId: serverId, CommandPayload: payload));
            operationId = op.Id;
        }

        Operation? final = await WaitForStateAsync(factory, operationId, OperationState.Succeeded);
        await Assert.That(final).IsNotNull();
        await Assert.That(received).IsNotNull();
        await Assert.That(received!.File).IsEqualTo(PzConfigFile.SandboxVars);
        await Assert.That(received.BaselineHash).IsEqualTo("base-1");
        await Assert.That(received.Edits[0].Path).IsEqualTo("Zombies");

        // The completion recorded a Configuration Revision (the new drift baseline) against the Server.
        using (IServiceScope scope = factory.Services.CreateScope())
        {
            ConfigurationRevisionRepository revisions =
                new(scope.ServiceProvider.GetRequiredService<ZWardenDbContext>());
            ConfigurationRevision? latest = await revisions.FindLatestAsync(serverId, PzConfigFile.SandboxVars);
            await Assert.That(latest).IsNotNull();
            await Assert.That(latest!.SnapshotHash).IsEqualTo("new-hash-42");
            await Assert.That(latest.CanonicalSnapshot).IsEqualTo("[[\"Zombies\",\"n:1:i\"]]");
            // #225: a sandbox write is never reloaded live — the result line says it waits for a restart.
            await Assert.That(final!.StatusLine!).Contains("next restart");
        }

        await connection.StopAsync();
    }

    [Test]
    public async Task A_live_reloaded_ini_apply_says_so_on_the_operation_and_in_the_audit_trail()
    {
        // #225: the Agent reports the INI write was made live with reloadoptions; the control plane records that as
        // the Operation's result line and a Server.ConfigurationLiveReload audit entry.
        await using ZWardenWebAppFactory factory = new();
        (AgentId agentId, string credential) = await SeedTrustedAgentAsync(factory);
        ServerId serverId = await SeedServerAsync(factory, agentId);
        await using HubConnection connection = BuildConnection(factory, credential);

        connection.On<string>(AgentHubProtocol.ReceiveCommand, async json =>
        {
            Envelope<IProtocolMessage> command = ProtocolJson.Deserialize(json);
            if (command.Payload is ConfigApply apply && command.OperationId is { } operationId)
            {
                Envelope<OperationCompleted> reply = Envelope.Create(
                    new OperationCompleted(
                        OperationOutcome.Succeeded,
                        Config: new ConfigApplyResult(
                            apply.File, "ini-hash", "[[\"MaxPlayers\",\"s:20\"]]", 1, ConfigReloadOutcome.Reloaded)),
                    Now, serverId: command.ServerId, operationId: operationId);
                await connection.SendAsync(AgentHubProtocol.OperationCompleted, reply);
            }
        });

        await connection.StartAsync();
        await connection.InvokeAsync<ProtocolNegotiationResult>(AgentHubProtocol.Hello, Hello(agentId));

        OperationId operationId;
        using (IServiceScope scope = factory.Services.CreateScope())
        {
            IOperationCoordinator coordinator = scope.ServiceProvider.GetRequiredService<IOperationCoordinator>();
            string payload = new ConfigApplyPayload(
                PzConfigFile.Ini, "base-1", [new ConfigApplyEdit("MaxPlayers", ConfigEditKind.Number, "20")]).ToJson();
            Operation op = await coordinator.EnqueueAsync(
                new EnqueueOperationRequest(
                    agentId, OperationKind.ConfigApply, IsMutating: true, "e2e-config-reload",
                    ServerId: serverId, CommandPayload: payload));
            operationId = op.Id;
        }

        Operation? final = await WaitForStateAsync(factory, operationId, OperationState.Succeeded);
        await Assert.That(final!.StatusLine).IsEqualTo("Applied and reloaded live on the running server.");

        using (IServiceScope scope = factory.Services.CreateScope())
        {
            ZWardenDbContext db = scope.ServiceProvider.GetRequiredService<ZWardenDbContext>();
            AuditEvent? reload = db.Set<AuditEvent>().AsEnumerable()
                .FirstOrDefault(e => e.Action == ConfigurationAuditActions.LiveReload && e.ServerId == serverId);
            await Assert.That(reload).IsNotNull();
            await Assert.That(reload!.Outcome).IsEqualTo(AuditOutcome.Succeeded);
            await Assert.That(reload.Detail!).Contains("reloaded live");
        }

        await connection.StopAsync();
    }

    [Test]
    public async Task A_mod_discovery_runs_end_to_end_and_caches_the_inventory()
    {
        await using ZWardenWebAppFactory factory = new();
        (AgentId agentId, string credential) = await SeedTrustedAgentAsync(factory);
        ServerId serverId = await SeedServerAsync(factory, agentId);
        await using HubConnection connection = BuildConnection(factory, credential);

        // The dispatcher must map ModDiscovery to the DiscoverMods command (F21). The stand-in Agent verifies it
        // received exactly that, then reports an observed inventory. (The real disk walk is unit-tested.)
        bool receivedDiscoverMods = false;
        connection.On<string>(AgentHubProtocol.ReceiveCommand, async json =>
        {
            Envelope<IProtocolMessage> command = ProtocolJson.Deserialize(json);
            if (command.Payload is DiscoverMods && command.OperationId is { } operationId)
            {
                receivedDiscoverMods = true;
                Envelope<OperationCompleted> reply = Envelope.Create(
                    new OperationCompleted(
                        OperationOutcome.Succeeded,
                        Mods: new ModDiscoveryResult(
                            InstalledItems: [new DiscoveredWorkshopItem("111", [new DiscoveredMod("ModA", "Mod A")])],
                            ConfiguredWorkshopIds: ["111"],
                            EnabledModIds: ["ModA"],
                            Findings: [new ModCompatFinding(ModCompatKind.InstalledButInactive, "Idle", null)])),
                    Now, serverId: command.ServerId, operationId: operationId);
                await connection.SendAsync(AgentHubProtocol.OperationCompleted, reply);
            }
        });

        await connection.StartAsync();
        await connection.InvokeAsync<ProtocolNegotiationResult>(AgentHubProtocol.Hello, Hello(agentId));

        OperationId operationId;
        using (IServiceScope scope = factory.Services.CreateScope())
        {
            IOperationCoordinator coordinator = scope.ServiceProvider.GetRequiredService<IOperationCoordinator>();
            Operation op = await coordinator.EnqueueAsync(
                new EnqueueOperationRequest(
                    agentId, OperationKind.ModDiscovery, IsMutating: false, "e2e-mod-discovery", ServerId: serverId));
            operationId = op.Id;
        }

        Operation? final = await WaitForStateAsync(factory, operationId, OperationState.Succeeded);
        await Assert.That(final).IsNotNull();
        await Assert.That(receivedDiscoverMods).IsTrue();

        // The completion cached the observed inventory (recorded before the operation is marked done),
        // ownership-guarded by the reporting Agent.
        IModInventoryCache cache = factory.Services.GetRequiredService<IModInventoryCache>();
        ModInventory? inventory = cache.GetLatest(serverId, agentId);
        await Assert.That(inventory).IsNotNull();
        await Assert.That(inventory!.InstalledItems.Single().Mods.Single().ModId).IsEqualTo("ModA");
        await Assert.That(inventory.Issues.Single().Kind).IsEqualTo(ModCompatIssueKind.InstalledButInactive);
        await Assert.That(cache.GetLatest(serverId, AgentId.New())).IsNull();

        await connection.StopAsync();
    }

    [Test]
    public async Task An_operation_for_a_disconnected_agent_stays_pending()
    {
        await using ZWardenWebAppFactory factory = new();
        _ = factory.Services; // force host start

        using IServiceScope scope = factory.Services.CreateScope();
        IOperationCoordinator coordinator = scope.ServiceProvider.GetRequiredService<IOperationCoordinator>();
        Operation op = await coordinator.EnqueueAsync(
            new EnqueueOperationRequest(AgentId.New(), OperationKind.DiagnosticsPing, IsMutating: false, "offline"));

        await Assert.That(op.State).IsEqualTo(OperationState.Pending);
        await Assert.That(op.StartedAt).IsNull();
    }

    private static Envelope<AgentHello> Hello(AgentId agentId) => new()
    {
        ProtocolVersion = ProtocolVersion.Current,
        MessageId = MessageId.New(),
        Timestamp = Now,
        AgentId = agentId,
        Payload = new AgentHello(agentId),
    };

    private static HubConnection BuildConnection(ZWardenWebAppFactory factory, string credential)
        => new HubConnectionBuilder()
            .WithUrl(new Uri(factory.Server.BaseAddress, AgentHubProtocol.Path), options =>
            {
                options.Transports = HttpTransportType.LongPolling;
                options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                options.AccessTokenProvider = () => Task.FromResult<string?>(credential);
            })
            .AddJsonProtocol(o => o.PayloadSerializerOptions = ProtocolJson.Options)
            .Build();

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

    private static async Task<ServerId> SeedServerAsync(ZWardenWebAppFactory factory, AgentId agentId)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ZWardenDbContext context = scope.ServiceProvider.GetRequiredService<ZWardenDbContext>();
        // The ownership interceptor stamps the ambient (default) tenant on insert (ADR 0016), the same tenant the
        // hub records the revision under — so the completion resolves to this Server.
        Domain.Servers.Server server = Domain.Servers.Server.Import(agentId, ServerId.New(), "alpha", Now);
        context.Add(server);
        await context.SaveChangesAsync();
        return server.Id;
    }

    private static async Task<Operation?> WaitForStateAsync(
        ZWardenWebAppFactory factory,
        OperationId operationId,
        OperationState state)
    {
        for (int i = 0; i < 100; i++)
        {
            using IServiceScope scope = factory.Services.CreateScope();
            OperationRepository repo = new(scope.ServiceProvider.GetRequiredService<ZWardenDbContext>());
            Operation? op = await repo.FindByIdAsync(operationId);
            if (op is not null && op.State == state)
            {
                return op;
            }

            await Task.Delay(50);
        }

        return null;
    }
}
