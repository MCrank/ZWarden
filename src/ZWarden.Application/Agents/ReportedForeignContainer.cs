using ZWarden.Domain.Ids;

namespace ZWarden.Application.Agents;

/// <summary>
/// #368: a canonical PZ container an Agent found on its Host but doesn't own, because it is stamped with another
/// Agent's id (the machine was wiped and enrolled again). Agent-reported and untrusted (trust-boundaries.md §3): it
/// is shown to the Owner and lets Replace host be offered, and Replace re-checks it, but it is never an authorization.
/// </summary>
/// <param name="ContainerId">The Docker short id.</param>
/// <param name="ServerId">The Server it hosts, by its label.</param>
/// <param name="LabelledAgentId">The Agent id it is stamped with.</param>
/// <param name="State">The Docker state string.</param>
public sealed record ReportedForeignContainer(string ContainerId, ServerId ServerId, AgentId LabelledAgentId, string State);
