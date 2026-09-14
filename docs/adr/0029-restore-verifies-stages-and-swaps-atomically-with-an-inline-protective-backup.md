# 29. A restore verifies the archive, stages it, and swaps it in atomically after an inline protective backup

A ZWarden restore rebuilds a Server's `/pz/data` world tree from one of its backups (F25), performed by the Agent
with direct host I/O — never `exec` or `docker cp` (ADR 0008) — as a single **mutating, server-scoped** Operation
(per-server lock, ADR 0022). Before it touches the live world the Agent **re-verifies** the archive: it recomputes
the SHA-256 over the archive on disk and **refuses** the restore on any mismatch against the checksum the control
plane recorded (ADR 0028), and it **refuses while the Server's container is running** (a live server is writing the
world). It then takes an **inline protective backup** of the current world — a full `.tar.gz` written to the same
`BackupRoot`, tagged `PreOperation` — **unpacks the archive into a staging tree** beside the world (never onto the
live tree), verifies the extraction produced world data, and **atomically swaps** the staging tree into place by
renaming the current world aside and the staging tree in. Any failure rolls the world back and leaves it untouched.
Extraction is **defensive**: an entry whose resolved path escapes the staging directory, a symlink/hard link, or any
non-file entry type is refused, so a tampered archive can never traverse out of the staging tree. The restore's
completion carries the protective backup's facts, which the control plane records as a normal tenant-owned `Backup`
so a mistaken restore can itself be undone.

- Status: accepted
- Decided in: F25 (#46)
- Bears on: PRD 42–44 (backups and restore integrity); Criterion 10 (create **and restore** backups); ADR
  [0028](./0028-backup-archive-contract-and-local-destination.md) (the archive contract restore depends on exactly —
  world-tree-only, symlink-free, checksummed); ADR [0022](./0022-operation-lifecycle-and-per-server-locking.md) (a
  restore is mutating and locks the Server); ADR [0008](./0008-docker-socket-access-via-wollomatic-socket-proxy.md)
  (the allowlist denies `exec`/`attach`, so a restore is pure host I/O over the owned bind mount); ADR
  [0016](./0016-tenant-isolation-query-filter-and-default-tenant.md) (the protective `Backup` is tenant-owned); ADR
  [0020](./0020-agent-protocol-versioning-and-catalogue.md) (the additive `RestoreServer` command and
  `RestoreResult`). Supersedes F24's stated plan for the first `IPreOperationBackup` caller (see Decision).

## Context

A restore is the one irreversible-feeling operation ZWarden performs on the data that matters: it overwrites the
world tree F24 exists to protect. Three things must hold. First, it must **prove the archive before trusting it** —
ADR 0028 made the backup carry a SHA-256 over exactly the bytes that will be restored precisely so restore can refuse
a corrupt archive rather than unpack garbage over a live world. Second, a restore that fails **partway** — a disk
error mid-unpack, a crash — must **never leave a half-written world**; the operator must get back either the fully
restored world or the world they had. Third, even a *successful* restore might be the **wrong** one (the operator
picked the wrong backup), so there must be a way back to the pre-restore state.

F24 shipped `IPreOperationBackup`, a control-plane seam that enqueues a separate `PreOperation`-tagged backup
Operation, and named F25's protective backup its first caller. But a restore holds the per-server lock as one mutating
Operation, and there is no operation-saga machinery to sequence a *separate* backup Operation ahead of it: doing so
would mean a two-Operation workflow with a lock-release window between them and a new class of partial-failure states
(the protective backup succeeds, the restore never enqueues or fails). The atomic-swap the second requirement already
forces gives the protective backup for free — the world moved aside *is* the pre-restore copy — so the protective
backup wants to live **inside** the one restore Operation, not ahead of it.

## Decision

- **Verify before touching anything.** The Agent recomputes the SHA-256 over the named archive under
  `<BackupRoot>/<serverId>/` and refuses the restore on a mismatch or a missing archive, before any mutation. The
  archive name is guarded as a bare file name (no separators, no `..`), mirroring the delete guard.
- **Refuse while running.** The Agent lists the canonical containers it owns and refuses if the Server's container is
  in the `running` state — a live server is writing the world, and swapping under it corrupts both trees. This is the
  **authoritative** stopped precondition (the Agent owns ground truth); the control plane adds an **advisory**
  last-observed-state pre-check (`ServerRunning`) so the operator gets an immediate, friendly refusal rather than a
  round-trip. The operator stops the Server first; the restore leaves it stopped.
- **Inline protective backup.** Before the swap the Agent archives the current world (reusing the F24
  `IBackupArchiver`) into `BackupRoot` as a `PreOperation` backup, and reports its facts on completion; the control
  plane records it as a tenant-owned `Backup`. A never-provisioned host with no world yet yields an empty protective
  archive rather than a failure (disaster recovery onto a fresh host is valid).
- **Stage, then swap atomically.** The archive is unpacked into a sibling `<serverId>.restore-<guid>` staging tree
  (same volume as the world → rename is atomic), never onto the live tree. The swap moves the live world to
  `<serverId>.pre-restore-<guid>` then moves staging into place; on any failure the world is moved back. The aside
  copy is discarded on success (the protective archive is the durable rollback source).
- **Health verification.** A real world backup always has files: a zero-file extraction is refused as "no world data"
  before the swap (so an empty or garbage archive never replaces a live world), and the swapped-in world is
  re-checked as non-empty afterwards, rolling back if it is not.
- **Defensive extraction.** The extractor refuses an entry whose resolved path escapes the staging root (a rooted or
  `..` path), a symlink or hard link, or any non-file entry type — defense in depth over an archive ZWarden itself
  produced (ADR 0028 already never *follows* a symlink into a backup; restore never *writes* one out).
- **One additive Operation.** `RestoreServer(ArchiveName, Sha256)` is a new leaf of the closed `AgentCommand`
  vocabulary; the completion carries an additive `RestoreResult` (the restored archive and the protective
  `BackupResult`). No protocol version bump (ADR 0020). `OperationKind.Restore` is mutating and server-scoped.

## Alternatives considered

- **Orchestrate a separate protective-backup Operation via `IPreOperationBackup` (F24's stated plan).** Rejected for
  v1.0: it needs operation-saga infrastructure that does not exist, introduces a lock-release window between the
  backup and the restore, and multiplies partial-failure states. The inline protective backup is atomic under one
  lock and reuses the same archiver. The `IPreOperationBackup` seam remains for a future *non-destructive* risky
  Operation that genuinely wants a separate, independently-recorded backup ahead of it (e.g. a config or update path);
  F25 simply is not that caller.
- **Auto-stop the container as the first restore step.** Rejected: it would implicitly disconnect connected players
  during a destructive operation the operator may not realize is disruptive. Refusing while running keeps the
  destructive act explicit — the operator stops the Server deliberately.
- **Unpack in place over the live world (no staging).** Rejected: a failure mid-unpack leaves a half-written world
  with no clean rollback. Staging plus an atomic rename guarantees the observer only ever sees the whole old world or
  the whole new one.
- **Trust the archive because ZWarden authored it (skip the traversal/symlink/type checks).** Rejected as the cheap
  half of defense in depth: the checks are a few lines, and a restore that *writes* files is a more dangerous place to
  trust a path than a backup that only reads them.

## Consequences

- **A restore is safe against corruption, crashes, and mistakes.** A bad checksum, a running server, an empty archive,
  or an unsafe entry each refuse before the live world is harmed; a crash mid-restore rolls back; a wrong-backup
  restore is undone from the protective backup the restore itself recorded.
- **Every restore leaves a protective backup behind.** These accumulate under `BackupRoot` exactly like manual
  backups, with no pruner (F26 deferred), until an operator deletes them — the same accepted trade-off as F24.
- **The stopped precondition is a manual step.** The operator must stop the Server before restoring; a running server
  is refused at both layers. This is deliberate — a restore is destructive and explicit.
- **The archive contract is load-bearing.** Restore relies on ADR 0028's world-tree-only, symlink-free, checksummed
  shape; changing that contract is a breaking change for restore, as ADR 0028 already noted.
- **`IPreOperationBackup` ships F24-complete but F25 is not its caller.** The seam stays available and unused by
  restore; a later feature that wants a separate pre-Operation backup is its first real caller.
