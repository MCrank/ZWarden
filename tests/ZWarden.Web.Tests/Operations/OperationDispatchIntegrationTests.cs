using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using ZWarden.Application.Agents;
using ZWarden.Application.Operations;
using ZWarden.Contracts.Protocol;
using ZWarden.Contracts.Protocol.Messages;
using ZWarden.Domain.Agents;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Operations;
using ZWarden.Infrastructure.Agents;
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
