using ZWarden.Domain.Ids;

namespace ZWarden.Application.Agents;

/// <summary>
/// The in-memory record of which Agents are connected to <b>this</b> ZWarden.Web process right now (F10,
/// decision 2). It is authoritative for "connected now" and is the seam F11 dispatches commands through and
/// F9 revoke/disable drops a live connection through. It is deliberately <b>per-process</b>: v1.0 runs a
/// single Web instance (ADR 0005), so this is complete; a multi-instance deployment (F10A/v1.1) swaps in a
/// backplane-backed implementation behind this same interface, leaving the hub, dispatch and revoke callers
/// unchanged. The persisted <c>LastSeenAt</c>/<c>ConnectionState</c> on the Agent is the durable companion to
/// this volatile registry.
/// </summary>
public interface IAgentConnectionRegistry
{
    /// <summary>
    /// Records that <paramref name="agentId"/> is connected on <paramref name="connectionId"/>, with
    /// <paramref name="abort"/> the action that forcibly ends that connection. If the Agent already had a
    /// live connection (a lingering socket from before a reconnect), the prior one is aborted and replaced —
    /// last writer wins.
    /// </summary>
    void Register(AgentId agentId, string connectionId, Action abort);

    /// <summary>Removes the connection identified by <paramref name="connectionId"/>, if present. Idempotent.</summary>
    void Remove(string connectionId);

    /// <summary>True iff <paramref name="agentId"/> has a live connection on this process.</summary>
    bool IsConnected(AgentId agentId);

    /// <summary>
    /// The connection id currently held for <paramref name="agentId"/>, or <c>null</c> if none — the target
    /// F11 addresses a command to (<c>Clients.Client(connectionId)</c>).
    /// </summary>
    string? GetConnectionId(AgentId agentId);

    /// <summary>
    /// Forcibly ends <paramref name="agentId"/>'s live connection if it has one (F9 revoke/disable). Returns
    /// <c>true</c> if a connection was aborted, <c>false</c> if the Agent was not connected on this process.
    /// </summary>
    bool TryAbort(AgentId agentId);
}
