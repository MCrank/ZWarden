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
using ZWarden.Infrastructure.Tenancy;

namespace ZWarden.Web.Tests.Operations;

/// <summary>
/// #271: the delete completion over the real F10/F11 pipeline. A <see cref="OperationKind.DeleteServer"/> the Agent
/// reports <see cref="OperationOutcome.Succeeded"/> removes the Server from the fleet (the container is gone); a failed
/// one leaves the Server exactly where it was. Tier-1 (in-memory <c>TestServer</c>).
/// </summary>
public class DeleteServerCompletionTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 27, 10, 0, 0, TimeSpan.Zero);

    [Test]
    [Arguments(OperationOutcome.Succeeded, false)]
    [Arguments(OperationOutcome.Failed, true)]
    public async Task A_delete_completion_removes_the_server_only_when_it_succeeded(OperationOutcome outcome, bool serverRemains)
    {
        await using ZWardenWebAppFactory factory = new();
        (AgentId agentId, string credential) = await SeedTrustedAgentAsync(factory);
        ServerId serverId = await SeedServerAsync(factory, agentId);
        await using HubConnection connection = BuildConnection(factory, credential);

        connection.On<string>(AgentHubProtocol.ReceiveCommand, async json =>
        {
            Envelope<IProtocolMessage> command = ProtocolJson.Deserialize(json);
            if (command.Payload is DeleteServer && command.OperationId is { } operationId)
            {
                Envelope<OperationCompleted> reply = Envelope.Create(
                    new OperationCompleted(outcome, outcome == OperationOutcome.Failed ? "Docker: boom" : null), Now,
                    serverId: command.ServerId, operationId: operationId);
                await connection.SendAsync(AgentHubProtocol.OperationCompleted, reply);
            }
        });

        await connection.StartAsync();
        await connection.InvokeAsync<ProtocolNegotiationResult>(AgentHubProtocol.Hello, Hello(agentId));

        OperationId operationId;
        using (AsyncServiceScope scope = factory.Services.CreateSystemScope())
        {
            IOperationCoordinator coordinator = scope.ServiceProvider.GetRequiredService<IOperationCoordinator>();
            Operation op = await coordinator.EnqueueAsync(
                new EnqueueOperationRequest(
                    agentId, OperationKind.DeleteServer, IsMutating: true, "delete-server", ServerId: serverId));
            operationId = op.Id;
        }

        OperationState expected = outcome == OperationOutcome.Succeeded ? OperationState.Succeeded : OperationState.Failed;
        Operation? final = await WaitForStateAsync(factory, operationId, expected);
        await Assert.That(final).IsNotNull();

        await Assert.That(await ServerExistsAsync(factory, serverId)).IsEqualTo(serverRemains);

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
        using AsyncServiceScope scope = factory.Services.CreateSystemScope();
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
        using AsyncServiceScope scope = factory.Services.CreateSystemScope();
        ZWardenDbContext context = scope.ServiceProvider.GetRequiredService<ZWardenDbContext>();
        Domain.Servers.Server server = Domain.Servers.Server.Import(agentId, ServerId.New(), "alpha", Now);
        context.Add(server);
        await context.SaveChangesAsync();
        return server.Id;
    }


    private static async Task<bool> ServerExistsAsync(ZWardenWebAppFactory factory, ServerId serverId)
    {
        using AsyncServiceScope scope = factory.Services.CreateSystemScope();
        ZWardenDbContext context = scope.ServiceProvider.GetRequiredService<ZWardenDbContext>();
        return await context.Set<Domain.Servers.Server>().AnyAsync(s => s.Id == serverId);
    }

    private static async Task<Operation?> WaitForStateAsync(
        ZWardenWebAppFactory factory, OperationId operationId, OperationState state)
    {
        for (int i = 0; i < 100; i++)
        {
            using AsyncServiceScope scope = factory.Services.CreateSystemScope();
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
