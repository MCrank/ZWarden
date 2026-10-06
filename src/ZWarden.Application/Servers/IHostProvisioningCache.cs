using ZWarden.Domain.Ids;

namespace ZWarden.Application.Servers;

/// <summary>
/// Whether each connected Agent's PZ image can provision a server (#364), as it reported on hello: transient, in
/// memory, last hello wins. A Host can only take a server while connected, and a connected Agent has said hello to
/// this process, so no persistence is needed. <b>Observed, not trusted</b> (trust-boundaries.md §3): it lets the
/// control plane refuse a Host that would only fail; the Agent re-checks on every provision.
/// </summary>
public interface IHostProvisioningCache
{
    /// <summary>Records what <paramref name="agentId"/> (the authenticated connection, never the payload) reported.</summary>
    void Record(AgentId agentId, bool pzImageReady);

    /// <summary>Whether <paramref name="agentId"/> reported a usable PZ image, or <c>null</c> when unknown (no hello
    /// yet, or an Agent built before #364).</summary>
    bool? IsPzImageReady(AgentId agentId);
}
