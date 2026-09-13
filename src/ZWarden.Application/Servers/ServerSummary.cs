using ZWarden.Domain.Ids;
using ZWarden.Domain.Servers;

namespace ZWarden.Application.Servers;

/// <summary>An operator-facing view of a <see cref="Server"/> (<c>srv-</c>) for the inventory dashboard (F14).
/// Carries only non-secret metadata and the coarse last-reported run-state; never RCON or any credential.</summary>
public sealed record ServerSummary(
    ServerId Id,
    AgentId AgentId,
    string Name,
    string? Description,
    int? GamePort,
    int? QueryPort,
    ServerRunState LastRunState,
    DateTimeOffset? LastStateReportedAt,
    ServerHealth? LastHealth,
    DateTimeOffset? LastHealthReportedAt);
