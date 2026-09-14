using ZWarden.Domain.Ids;

namespace ZWarden.Application.Players;

/// <summary>
/// The connected-player roster a Server last reported over RCON (F19) — the observed answer to a
/// <c>ListPlayers</c> Operation. Transient display data, like F16's metrics/health: it is the newest roster per
/// Server, held in the in-memory <see cref="IPlayerRosterCache"/> and never persisted. The usernames are
/// <b>untrusted</b> PZ output (trust-boundaries.md §8), carried verbatim for escaping at render.
/// </summary>
/// <param name="ServerId">The Server the roster belongs to.</param>
/// <param name="AgentId">The Agent that reported it — the cache ownership-guard key.</param>
/// <param name="Count">The connected-player count PZ reported.</param>
/// <param name="Players">The connected usernames, in PZ's order (may be empty).</param>
/// <param name="ObservedAt">When the Agent observed the roster (UTC).</param>
public sealed record PlayerRoster(
    ServerId ServerId,
    AgentId AgentId,
    int Count,
    IReadOnlyList<string> Players,
    DateTimeOffset ObservedAt);
