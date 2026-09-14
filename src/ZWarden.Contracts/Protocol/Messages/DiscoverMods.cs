namespace ZWarden.Contracts.Protocol.Messages;

/// <summary>
/// Discover the Workshop content and mods a Server actually has on disk (F21). Like <see cref="ListPlayers"/> this is
/// a non-mutating, <b>per-server</b> command carrying <b>no payload</b> — the target Server rides the envelope's
/// <see cref="Envelope{TPayload}.ServerId"/> and the operation is its <c>OperationId</c>. The Agent walks the
/// Server's Workshop content subtree (<c>steamapps/workshop/content/108600/&lt;id&gt;/mods/&lt;folder&gt;/mod.info</c>),
/// reads the config's <c>WorkshopItems=</c>/<c>Mods=</c> lists through the F20a read seam, and reports an
/// <see cref="OperationCompleted"/> carrying a <see cref="ModDiscoveryResult"/>. Read-only: no <c>exec</c>
/// (ADR 0008), no writes, no external network call — mutation is F22 and Workshop metadata/search is #110. The
/// discovered ids and names are <b>untrusted</b> PZ/Workshop output (trust-boundaries.md §8), carried verbatim for
/// escaping at render; it carries no free-form command.
/// </summary>
[ProtocolMessage("mods.discover")]
public sealed record DiscoverMods : AgentCommand;
