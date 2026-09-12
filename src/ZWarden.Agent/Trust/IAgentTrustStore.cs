namespace ZWarden.Agent.Trust;

/// <summary>
/// Persists the Agent's <see cref="AgentTrustMaterial"/> in a <b>single local file, never a database</b> —
/// the Agent must not reach persistence (trust-boundaries.md §9 rule 1). Unlike the F8 identity file, this
/// file holds a real credential; it is written atomically and BOM-less, and a malformed file fails typed
/// rather than being silently replaced.
/// </summary>
public interface IAgentTrustStore
{
    /// <summary>Loads the stored trust material, or <c>null</c> when the Agent has not yet enrolled.</summary>
    Task<AgentTrustMaterial?> TryLoadAsync(CancellationToken cancellationToken = default);

    /// <summary>Persists trust material atomically, replacing any prior file.</summary>
    Task SaveAsync(AgentTrustMaterial material, CancellationToken cancellationToken = default);
}
