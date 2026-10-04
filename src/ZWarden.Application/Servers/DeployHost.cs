using ZWarden.Domain.Ids;

namespace ZWarden.Application.Servers;

/// <summary>
/// A Host a new Server can be deployed on (#338): a trusted (enabled, credentialed) Agent in the current tenant. The
/// Label and Hostname are untrusted, observed strings, and only reach a caller who may view Hosts (<c>Agent.View</c>,
/// #336 D3); anyone else gets <c>null</c> and names the Host by its short id.
/// </summary>
/// <param name="Id">The Agent (Host) identifier.</param>
/// <param name="Label">The operator-set enrollment label, when the caller may see it.</param>
/// <param name="Hostname">The Host's self-reported machine name, when the caller may see it.</param>
public sealed record DeployHost(AgentId Id, string? Label, string? Hostname);
