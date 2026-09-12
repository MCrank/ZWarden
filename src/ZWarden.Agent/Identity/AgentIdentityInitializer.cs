using Microsoft.Extensions.Hosting;
using ZWarden.Domain.Ids;

namespace ZWarden.Agent.Identity;

/// <summary>
/// Resolves the Agent's self-identity at startup (F8), before the worker runs. Registered ahead of
/// <c>AgentWorker</c> so <see cref="IAgentIdentity"/> is populated by the time any other service
/// reads it.
/// </summary>
public sealed class AgentIdentityInitializer : IHostedService
{
    private readonly IAgentIdentityStore _store;
    private readonly AgentIdentityHolder _holder;

    /// <summary>Creates the initializer over the identity store and the shared identity holder.</summary>
    public AgentIdentityInitializer(IAgentIdentityStore store, AgentIdentityHolder holder)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(holder);
        _store = store;
        _holder = holder;
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        AgentId agentId = await _store.LoadOrCreateAsync(cancellationToken).ConfigureAwait(false);
        _holder.Set(agentId);
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
