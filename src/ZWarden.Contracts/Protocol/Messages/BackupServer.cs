namespace ZWarden.Contracts.Protocol.Messages;

/// <summary>
/// Back up a Server's world data (F24). The target Server rides the envelope's
/// <see cref="Envelope{TPayload}.ServerId"/>; the command carries no payload — a backup takes no parameters the
/// Agent needs, and <i>why</i> it was taken (<c>BackupReason</c>) is control-plane retention metadata the manager
/// records from the Operation, never something the Agent decides. It is a <b>mutating, server-scoped</b> Operation,
/// so it claims the per-server lock (ADR 0022) — a backup never races a lifecycle Operation. The Agent reads the
/// Server's <c>/pz/data</c> world tree <b>host-side</b> (it owns the bind mount — no <c>exec</c>, no <c>docker cp</c>;
/// ADR 0008), writes a compressed <c>.tar.gz</c> to its configured <c>BackupRoot</c> — excluding the SteamCMD
/// install and not following the Workshop symlink (ADR 0028) — computes a SHA-256 over the produced archive, and
/// reports <see cref="OperationCompleted"/> with a <see cref="BackupResult"/> (the archive locator, byte size, and
/// lowercase-hex checksum). No free-form command crosses the boundary (trust-boundaries.md §9 rule 3).
/// </summary>
[ProtocolMessage("lifecycle.backup-server")]
public sealed record BackupServer : AgentCommand;
