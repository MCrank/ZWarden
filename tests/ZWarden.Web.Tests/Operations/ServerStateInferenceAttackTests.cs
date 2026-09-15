using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ZWarden.Application.Agents;
using ZWarden.Application.Operations;
using ZWarden.Contracts.Protocol;
using ZWarden.Contracts.Protocol.Messages;
using ZWarden.Domain.Agents;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Operations;
using ZWarden.Infrastructure.Operations;
using ZWarden.Infrastructure.Persistence;
using ZWarden.Web.Agents;
using ZWarden.Web.Tests.Account;

namespace ZWarden.Web.Tests.Operations;

/// <summary>
/// F40 release gate — trust-boundaries.md §10 attack 4: <b>state inference.</b> ZWarden.Web may never infer a
/// state transition from a command's success (trust-boundaries.md §3) — a <c>StartServer</c> operation that
/// returns <see cref="OperationOutcome.Succeeded"/> means the command was <i>accepted</i>, not that the server
/// is running. Run-state advances only on an Agent-<b>observed</b> report (<see cref="ServerStateChanged"/> /
/// snapshot), which is why F16 (health) exists at all. This suite drives a real StartServer operation to
/// Succeeded over the F10/F11 pipeline while the stand-in Agent deliberately reports <i>no</i> observed state,
/// and asserts the server stays <see cref="Domain.Servers.ServerRunState.Unknown"/> — then that an observed
/// report is what moves it. Tier-1 (in-memory <c>TestServer</c>).
/// </summary>
public class ServerStateInferenceAttackTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 12, 10, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task A_succeeded_start_operation_does_not_advance_run_state_until_an_observed_report()
    {
        await using ZWardenWebAppFactory factory = new();
        (AgentId agentId, string credential) = await SeedTrustedAgentAsync(factory);
        ServerId serverId = await SeedServerAsync(factory, agentId);
        await using HubConnection connection = BuildConnection(factory, credential);

        // The stand-in Agent accepts and "runs" the StartServer command and reports success — but reports NO
        // observed state change. An Agent that ran `start` has not yet observed the PZ server as running.
        connection.On<string>(AgentHubProtocol.ReceiveCommand, async json =>
        {
            Envelope<IProtocolMessage> command = ProtocolJson.Deserialize(json);
            if (command.Payload is StartServer && command.OperationId is { } operationId)
            {
                Envelope<OperationCompleted> reply = Envelope.Create(
                    new OperationCompleted(OperationOutcome.Succeeded), Now,
                    serverId: command.ServerId, operationId: operationId);
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
                    agentId, OperationKind.StartServer, IsMutating: true, "attack-start", ServerId: serverId));
            operationId = op.Id;
        }

        Operation? final = await WaitForStateAsync(factory, operationId, OperationState.Succeeded);
        await Assert.That(final).IsNotNull();
        await Assert.That(final!.State).IsEqualTo(OperationState.Succeeded);

        // The attack: a successful start must NOT have promoted the server to Running. It is still Unknown,
        // because no Agent-observed state has arrived.
        Domain.Servers.Server afterSuccess = await LoadServerAsync(factory, serverId);
        await Assert.That(afterSuccess.LastRunState).IsEqualTo(Domain.Servers.ServerRunState.Unknown);
        await Assert.That(afterSuccess.LastStateReportedAt).IsNull();

        // Only an observed report advances it. Now the Agent reports the server running, and the state follows.
        await connection.InvokeAsync(
            AgentHubProtocol.ServerStateChanged,
            Envelope.Create(new ServerStateChanged(serverId, ServerRunState.Running), Now, agentId, serverId));

        await WaitUntilAsync(async () =>
            (await LoadServerAsync(factory, serverId)).LastRunState == Domain.Servers.ServerRunState.Running);

        Domain.Servers.Server afterObserved = await LoadServerAsync(factory, serverId);
        await Assert.That(afterObserved.LastRunState).IsEqualTo(Domain.Servers.ServerRunState.Running);
        await Assert.That(afterObserved.LastStateReportedAt).IsNotNull();

        await connection.StopAsync();
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
        Domain.Servers.Server server = Domain.Servers.Server.Import(agentId, ServerId.New(), "alpha", Now);
        context.Add(server);
        await context.SaveChangesAsync();
        return server.Id;
    }

    private static async Task<Domain.Servers.Server> LoadServerAsync(ZWardenWebAppFactory factory, ServerId serverId)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ZWardenDbContext context = scope.ServiceProvider.GetRequiredService<ZWardenDbContext>();
        return await context.Set<Domain.Servers.Server>().FirstAsync(s => s.Id == serverId);
    }

    private static async Task<Operation?> WaitForStateAsync(
        ZWardenWebAppFactory factory, OperationId operationId, OperationState state)
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

    private static async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        for (int i = 0; i < 100 && !await condition(); i++)
        {
            await Task.Delay(50);
        }
    }
}
