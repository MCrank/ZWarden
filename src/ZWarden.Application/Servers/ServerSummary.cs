using ZWarden.Domain.Ids;
using ZWarden.Domain.Servers;

namespace ZWarden.Application.Servers;

/// <summary>An operator-facing view of a <see cref="Server"/> (<c>srv-</c>) for the inventory dashboard (F14).
/// Carries only non-secret metadata and the coarse last-reported run-state; never RCON or any credential.
/// <see cref="GameVersion"/> is the game version from the boot log (#262); <see cref="InstalledBuildId"/> is the Steam
/// build id. <see cref="HeapSizeBytes"/> is the JVM heap its container runs with (#230), <c>null</c> until reported.</summary>
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
    DateTimeOffset? LastHealthReportedAt,
    string? InstalledBuildId,
    string? GameVersion = null,
    long? HeapSizeBytes = null);
