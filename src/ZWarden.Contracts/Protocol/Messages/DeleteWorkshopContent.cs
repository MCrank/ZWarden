namespace ZWarden.Contracts.Protocol.Messages;

/// <summary>
/// Delete unused Workshop downloads from a Server's install volume (#293). The target Server rides the envelope's
/// <see cref="Envelope{TPayload}.ServerId"/>; <see cref="WorkshopIds"/> are the items whose folders under
/// <c>steamapps/workshop/content/108600/</c> to remove. The Agent guards every id itself: the Server must be one it
/// owns, each id must be a bare numeric Workshop id (a malformed id fails the whole command before anything is
/// touched), an id still listed in <c>WorkshopItems=</c> is refused, and a folder that is a link is refused. An
/// already-absent folder counts as deleted, so a redelivered command is idempotent. It touches the install volume that
/// updates and a booting server write, so it claims the per-server lock (ADR 0022); it never stops the server — PZ
/// holds no Workshop file open (spike #293), and only unreferenced items are deleted.
/// </summary>
/// <param name="WorkshopIds">The Workshop ids whose downloaded folders to delete (validated Agent-side).</param>
[ProtocolMessage("mods.delete-workshop-content")]
public sealed record DeleteWorkshopContent(IReadOnlyList<string> WorkshopIds) : AgentCommand;
