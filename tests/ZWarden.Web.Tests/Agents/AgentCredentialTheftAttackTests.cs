using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ZWarden.Application.Agents;
using ZWarden.Contracts.Protocol;
using ZWarden.Contracts.Protocol.Messages;
using ZWarden.Domain.Agents;
using ZWarden.Domain.Ids;
using ZWarden.Infrastructure.Agents;
using ZWarden.Infrastructure.Persistence;
using ZWarden.Web.Agents;
using ZWarden.Web.Tests.Account;

namespace ZWarden.Web.Tests.Agents;

/// <summary>
/// F40 release gate — trust-boundaries.md §10 attack 1 (the sharpest named weakness): <b>Agent credential
/// theft.</b> Until mTLS lands in v1.1, a stolen per-Agent credential authenticates on a compromised host,
/// so the only mitigation the control plane holds is that the credential is <b>revocable and rotatable</b>
/// and a stale one is refused at the hub handshake before any hub method runs. This suite is the behavioral
/// proof of that mitigation, composing the F9 credential lifecycle (<see cref="Agent.RevokeCredential"/> /
/// <see cref="Agent.RotateCredential"/>) with the F10 "Agent" authentication scheme end to end over a real
/// SignalR client. Tier-1 (in-memory <c>TestServer</c>; LongPolling keeps it to the in-process handler).
/// </summary>
public class AgentCredentialTheftAttackTests
{
    [Test]
    public async Task A_stolen_but_revoked_credential_cannot_reconnect_to_the_hub()
    {
        await using ZWardenWebAppFactory factory = new();
        (AgentId agentId, string stolenCredential) = await SeedTrustedAgentAsync(factory);

        // Baseline: before revocation the (soon-to-be-stolen) credential is genuinely valid.
        await using (HubConnection legit = BuildConnection(factory, stolenCredential))
        {
            await legit.StartAsync();
            await legit.StopAsync();
        }

        // The operator revokes the credential (the F9 mitigation for a suspected theft).
        await RevokeCredentialAsync(factory, agentId);

        // The attacker holds the same credential and tries to (re)connect. A reconnect re-presents the
        // credential at a fresh handshake, so this is exactly what an automatic reconnect would attempt.
        await using HubConnection attacker = BuildConnection(factory, stolenCredential);
        await Assert.That(async () => await attacker.StartAsync()).ThrowsException();
    }

    [Test]
    public async Task Rotating_the_credential_refuses_the_old_and_admits_the_new()
    {
        await using ZWardenWebAppFactory factory = new();
        (AgentId agentId, string oldCredential) = await SeedTrustedAgentAsync(factory);

        // Rotation issues a fresh secret; the old one must stop matching immediately.
        string newCredential = await RotateCredentialAsync(factory, agentId);

        await using (HubConnection withOld = BuildConnection(factory, oldCredential))
        {
            await Assert.That(async () => await withOld.StartAsync()).ThrowsException();
        }

        await using HubConnection withNew = BuildConnection(factory, newCredential);
        await withNew.StartAsync();
        ProtocolNegotiationResult negotiation = await withNew.InvokeAsync<ProtocolNegotiationResult>(
            AgentHubProtocol.Hello, Hello(agentId));
        await Assert.That(negotiation.IsCompatible).IsTrue();
        await withNew.StopAsync();
    }

    [Test]
    public async Task A_disabled_agent_is_refused_even_holding_a_valid_credential()
    {
        await using ZWardenWebAppFactory factory = new();
        (AgentId agentId, string credential) = await SeedTrustedAgentAsync(factory);

        await MutateAgentAsync(factory, agentId, agent => agent.Disable());

        await using HubConnection connection = BuildConnection(factory, credential);
        await Assert.That(async () => await connection.StartAsync()).ThrowsException();
    }

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

    private static Envelope<AgentHello> Hello(AgentId agentId) => new()
    {
        ProtocolVersion = ProtocolVersion.Current,
        MessageId = MessageId.New(),
        Timestamp = DateTimeOffset.UtcNow,
        AgentId = agentId,
        Payload = new AgentHello(agentId),
    };

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

    private static Task RevokeCredentialAsync(ZWardenWebAppFactory factory, AgentId agentId)
        => MutateAgentAsync(factory, agentId, agent => agent.RevokeCredential(DateTimeOffset.UtcNow));

    private static async Task<string> RotateCredentialAsync(ZWardenWebAppFactory factory, AgentId agentId)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ICredentialHasher hasher = scope.ServiceProvider.GetRequiredService<ICredentialHasher>();
        ZWardenDbContext context = scope.ServiceProvider.GetRequiredService<ZWardenDbContext>();

        Domain.Security.SecretString fresh = hasher.Generate("zwa");
        Agent agent = await context.Set<Agent>().FirstAsync(a => a.Id == agentId);
        agent.RotateCredential(hasher.Hash(fresh), DateTimeOffset.UtcNow);
        await context.SaveChangesAsync();
        return fresh.Reveal();
    }

    private static async Task MutateAgentAsync(ZWardenWebAppFactory factory, AgentId agentId, Action<Agent> mutate)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ZWardenDbContext context = scope.ServiceProvider.GetRequiredService<ZWardenDbContext>();
        Agent agent = await context.Set<Agent>().FirstAsync(a => a.Id == agentId);
        mutate(agent);
        await context.SaveChangesAsync();
    }
}
