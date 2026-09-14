namespace ZWarden.Contracts.Protocol.Messages;

/// <summary>
/// Delete a Server's backup archive from the Agent host (F24). The target Server rides the envelope's
/// <see cref="Envelope{TPayload}.ServerId"/>; <see cref="ArchiveName"/> is the archive's file name under the
/// Agent's <c>&lt;BackupRoot&gt;/&lt;ServerId&gt;/</c> (the relative locator recorded when the backup was taken —
/// never a path). It is a <b>non-mutating, server-scoped</b> Operation (it removes a file, never touching the
/// running Server or its world), so it does not take the per-server lock (ADR 0022). The Agent validates that the
/// name is a bare file name (no directory separators, no <c>..</c>) before deleting, and treats an already-absent
/// file as success (idempotent). The control plane removes the backup record on the Operation's confirmed
/// completion.
/// </summary>
/// <param name="ArchiveName">The backup archive's file name to delete (a bare name; validated Agent-side).</param>
[ProtocolMessage("lifecycle.delete-backup")]
public sealed record DeleteBackup(string ArchiveName) : AgentCommand;
