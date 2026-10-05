using ZWarden.Domain.Ids;

namespace ZWarden.Application.Servers;

/// <summary>
/// One Host's containers with no Server record (#339/#357): what the Fleet adopt banner counts. The Label and Hostname
/// are untrusted, observed strings, and only reach a caller who may view Hosts (<c>Agent.View</c>, #336 D3); anyone else
/// gets <c>null</c> and names the Host by its short id.
/// </summary>
/// <param name="AgentId">The Agent (Host) reporting the containers.</param>
/// <param name="Label">The operator-set enrollment label, when the caller may see it.</param>
/// <param name="Hostname">The Host's self-reported machine name, when the caller may see it.</param>
/// <param name="Count">How many PZ containers it reports that no Server record claims.</param>
public sealed record UnmanagedHostCount(AgentId AgentId, string? Label, string? Hostname, int Count);
