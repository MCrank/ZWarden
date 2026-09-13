using ZWarden.Domain.Ids;
using ZWarden.Domain.Servers;

namespace ZWarden.Application.Servers;

/// <summary>A discovered-but-unregistered container together with the Agent (host) it was found on (F14) —
/// the import picker's row: which host, which Server id to adopt, and its observed run-state.</summary>
public sealed record DiscoveredServerOnAgent(AgentId AgentId, ServerId ServerId, ServerRunState RunState);
