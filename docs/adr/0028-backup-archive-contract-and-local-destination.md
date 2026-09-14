# 28. A backup is a checksummed tar.gz of the world tree, written host-side to a local destination

A ZWarden backup is a **compressed `.tar.gz` archive of a Server's `/pz/data` world tree, produced by the Agent
with direct host I/O — never `exec` or `docker cp` — and verified by a SHA-256 over the produced archive** (F24).
The Agent archives **only** the world tree (`<DataMountRoot>/<serverId>`): it **excludes** the SteamCMD install
(the host sibling `<serverId>.server`, reinstallable via F17) by construction, and it **never follows the
`data/workshop` symlink** (it points into ephemeral runtime storage, not world data). The archive is written to a
**configurable, Agent-owned `BackupRoot`**, one subdirectory per Server (`<BackupRoot>/<serverId>/<name>.tar.gz`),
through the Agent's **atomic temp-then-rename** idiom, so a crash never leaves a half-archive at the final name.
The control plane persists a tenant-owned **`Backup`** record (`bkp-`) holding the **relative** archive locator, the
byte size, the **lowercase-hex SHA-256**, and **retention metadata** (why it was taken, when, an optional expiry
hint) — never the bytes, and never an absolute host path. Taking a backup is a **mutating, server-scoped Operation**
(per-server lock); deleting one is a **non-mutating** Operation that removes the archive host-side. F24 **records**
retention metadata; it **enforces** none (no auto-pruning — scheduling is F26).

- Status: accepted
- Decided in: F24 (#45)
- Bears on: PRD 42–44 (backups and backup integrity); ADR [0008](./0008-docker-socket-access-via-wollomatic-socket-proxy.md)
  (the allowlist — `exec`/`attach`/`PUT archive` are denied, so a backup is pure host I/O); ADR
  [0022](./0022-operation-lifecycle-and-per-server-locking.md) (a take is mutating and locks the Server, a delete
  is non-mutating); ADR [0025](./0025-steamcmd-update-orchestration.md) (the same install-vs-world storage split
  the update path established); ADR [0016](./0016-tenant-isolation-query-filter-and-default-tenant.md) (the Backup
  record is tenant-owned); ADR [0020](./0020-agent-protocol-versioning-and-catalogue.md) (the additive
  `BackupServer`/`DeleteBackup` commands and `BackupResult`/`BackupDeletionResult`). Unblocks F25 (restore).

## Context

Project Zomboid's world, saves, player database, logs and config all live under one user-data tree that F13 mounts
at `/pz/data` and F17 keeps as a persistent host bind (`docs/research/project-zomboid-runtime.md` §5, §23). This is
the **irreplaceable** data — the SteamCMD install beside it is reinstallable, and the Workshop content under the
Steam root is re-downloadable. A backup must copy the world tree somewhere durable and be able to prove, later, that
the copy is intact.

Two constraints shape *how*. First, **the Agent cannot reach into a container to copy files**: ADR 0008's allowlist
denies `exec`, `attach`, and `PUT /containers/{id}/archive`, so there is no `docker cp`. But the Agent runs on the
host and **owns the bind-mount directory** (`DataMountRoot/<serverId>`), so it can read the world tree with ordinary
`System.IO` — the same route F17's control-file drop and F20b's config writes already take. Second, **exit codes and
opaque success are not enough** (the F24 sibling of ADR 0009's stdout-parsing rule): a restore (F25) must be able to
*refuse* a corrupt archive, which means the backup has to carry a verifiable checksum computed over exactly the bytes
that will be restored.

The `/pz/data` tree also contains a `workshop` **symlink** into the ephemeral runtime tmpfs. A naive recursive archive
would follow it and pull in Workshop content that is neither world data nor guaranteed to exist at rest.

## Decision

- **Source = the world tree only.** Archive `<DataMountRoot>/<serverId>` (bound at `/pz/data`). The SteamCMD install
  is the host *sibling* `<serverId>.server` — outside the source, so excluded by construction. **Symlinks are never
  followed**, so `data/workshop` is skipped. Directory structure is reconstructed on restore from the file paths;
  empty directories are not preserved (world saves never rely on them).
- **Format = `.tar.gz`** via the BCL (`System.Formats.Tar.TarWriter` over `GZipStream`) — Linux-native (the Agent runs
  on Linux hosts), streamed without a staging copy, and faithful to the PZ file tree for F25 to untar. No new package.
- **Integrity = SHA-256 over the produced archive**, emitted as **lowercase hex** (`Convert.ToHexStringLower`, the
  repo idiom). This is the exact value F25 re-verifies before restoring.
- **Destination = a configurable, Agent-owned `BackupRoot`.** A new `AgentOptions.BackupRoot` (validated absolute,
  defaulting to an Agent-owned sibling of the data root) holds `<BackupRoot>/<serverId>/<name>.tar.gz`, so an operator
  can point backups at a different disk than the live world. Kept off the world-data path the F16 disk meter measures.
- **Locator = relative.** The `Backup` record stores the archive **file name** only; the full path is reconstructed
  from `BackupRoot + serverId + name`, so a backup stays findable if the root is relocated and no host path leaks to
  the control plane.
- **Atomic write.** The archive is written to a temp file in the destination dir and `File.Move`d to the final name
  after it is fully written and hashed.
- **Operations.** A take is `OperationKind.Backup` — **mutating, server-scoped** (per-server lock, ADR 0022) — reporting
  a `BackupResult` (locator, size, checksum). A delete is `OperationKind.DeleteBackup` — **non-mutating** — carrying the
  bare archive name (path-traversal-guarded Agent-side) and removing the file; the control plane removes the record on
  confirmed completion. The retention reason and the record id ride the Operation's command payload.
- **Retention is recorded, not enforced.** The `Backup` carries `BackupReason` (`Manual`/`PreOperation`), `CreatedAt`,
  and an optional `ExpiresAt` hint. F24 runs no pruner — automated retention is F26.
- **Pre-operation backup API.** `IPreOperationBackup` is a callable seam that takes a `PreOperation`-tagged backup
  before a risky Operation. F24 ships the seam and wires **no** caller; F25's protective backup is the first.

## Alternatives considered

- **`docker cp` / `exec tar` out of the container.** Rejected: denied by the ADR 0008 allowlist, and it would couple a
  backup to a running container. Host I/O over the owned bind mount is both permitted and simpler.
- **`.zip` instead of `.tar.gz`.** Trivially inspectable cross-platform, but weaker on Unix permission/symlink fidelity.
  `.tar.gz` is the Linux-native choice for a Linux-origin tree; the checksum and restore path are format-agnostic, so
  `.zip` stays a possible future option if a Windows-native runtime (F38) ever needs it.
- **A fixed `<serverId>.backups` sibling of the data dir** (mirroring F17's `.server`). Simpler, but it forces backups
  onto the same disk as the world. A configurable root is one option and one validator line, and lets an operator
  separate the disks — the concern F16/F17 already honour for the install.
- **Storing an absolute host path on the record.** Rejected: it leaks host layout to the control plane and breaks if the
  root moves. A relative locator resolved Agent-side is portable and non-revealing.
- **Enforcing retention now.** Out of scope — scheduling is F26. Recording the metadata is enough to let F26 prune later.

## Consequences

- **A backup is only as safe as the host disk it lands on** until remote destinations (post-1.1) exist. `BackupRoot`
  can at least be a different local disk. The archive is **not encrypted at rest** in v1.0 — a later hardening item;
  the archive inherits whatever protection the host directory has.
- **Backups accumulate.** With no pruner (F26 deferred), a busy Server's `BackupRoot` grows until an operator deletes
  old backups by hand (the `Backup.Delete` surface F24 ships). We accept this for v1.0.
- **Deleting a backup is asynchronous and best-effort-consistent.** The record is removed only on the Agent's confirmed
  file deletion; if the Agent is offline the deletion Operation stays pending and the record remains until it runs.
- **The archive-name guard is defense in depth.** The name originates from a record the Agent itself authored, but the
  delete handler still refuses anything but a bare file name, so a tampered payload cannot traverse out of `BackupRoot`.
- **F25 depends on this contract exactly.** Restore verifies the stored SHA-256 against the archive before unpacking and
  relies on the world-tree-only, symlink-free shape; changing the archive contract is a breaking change for restore.
