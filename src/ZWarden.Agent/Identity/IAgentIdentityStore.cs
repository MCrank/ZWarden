using ZWarden.Domain.Ids;

namespace ZWarden.Agent.Identity;

/// <summary>
/// Reads and writes the Agent's persisted self-identity (F8). Persistence is a single local file,
/// never a database — a compromised Agent must not be able to reach persistence
/// (trust-boundaries.md §9 rule 1), which the reference-direction architecture test enforces.
/// </summary>
public interface IAgentIdentityStore
{
    /// <summary>
    /// Loads the persisted identity, or <see langword="null"/> if none has been stored yet. Throws
    /// <see cref="AgentIdentityException"/> if a file exists but does not contain a valid identity —
    /// it is never silently replaced.
    /// </summary>
    Task<AgentId?> TryLoadAsync(CancellationToken cancellationToken = default);

    /// <summary>Persists <paramref name="agentId"/> atomically and BOM-lessly, replacing any prior value.</summary>
    Task SaveAsync(AgentId agentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the persisted identity, generating and persisting a new one on first run. The result
    /// is stable across restarts.
    /// </summary>
    Task<AgentId> LoadOrCreateAsync(CancellationToken cancellationToken = default);
}
