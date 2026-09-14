# Feature 24 Mini-Plan — Backup Foundation

**Status:** ready for implementation. Roadmap issue: [F24 (#45)](https://github.com/MCrank/ZWarden/issues/45). Track D — the feature that turns a Server's world data from an *unrecoverable live tree* into a **verifiable, restorable Backup** produced by a durable Operation. **Depends on F11 (operations engine), F14 (server inventory), F20a (config read)** — all merged. **Unblocks [F25 (#46)](https://github.com/MCrank/ZWarden/issues/46)** — Restore.

**Format:** PRD 60. **TDD is mandatory** (PRD 2.2). **Written against:** [`scope-and-sequencing.md`](../scope-and-sequencing.md) §6 (F24), ADR [0022](../adr/0022-operation-lifecycle-and-per-server-locking.md) (operation lifecycle + per-server lock + lease/reaper — a backup is a **mutating, server-scoped** Operation), ADR [0008](../adr/0008-docker-socket-access-via-wollomatic-socket-proxy.md) (the allowlist — **`exec`/`attach`/`archive` are denied**, so a backup is pure host-side I/O, never a `docker cp`), ADR [0020](../adr/0020-agent-protocol-versioning-and-catalogue.md) (additive-vs-breaking contract change), ADR [0016](../adr/0016-tenant-isolation-query-filter-and-default-tenant.md) (the Backup entity is tenant-owned, filtered on every read), ADR [0018](../adr/0018-zwarden-owned-rbac.md) (fail-closed server-scoped authorization — the `Backup.*` permissions already exist), ADR [0019](../adr/0019-audit-is-append-only-tenant-owned-and-binds-the-auth-sink.md) (audit), and the F13/F16/F17 plans (the host storage layout, the disk-usage file-walk, and the `UpdateServer` operation this one is modelled on). New ADR **0028** lands with the feature (below). The three load-bearing decisions were settled with the maintainer before writing.

## Objective

Make **backing up** a Server's world data a first-class, durable, authorized, audited Operation. A `Backup` Operation reads the Server's `/pz/data` world tree **host-side** (the Agent owns the bind mount — no `exec`, no `docker cp`), writes a compressed `.tar.gz` archive to a configurable local destination, computes a **SHA-256 checksum** over the produced archive, and reports the archive locator, byte size and checksum on completion. ZWarden.Web persists a tenant-owned **`Backup`** record carrying that integrity metadata plus **retention metadata** (why it was taken, when, an optional expiry hint) so a later scheduler (F26) can prune. F24 also ships the **pre-operation backup API** — a callable seam a risky Operation (F17 update, F20b apply, F25's protective backup) can invoke to take a backup first — without yet wiring any Operation to it. The archive it produces is the exact artifact **F25 Restore** will verify and unpack.

## The three load-bearing decisions (settled with the maintainer before writing)

- **D-DESTINATION — the local destination is a configurable, Agent-owned `BackupRoot`, per-server subdir.** The Agent gains `AgentOptions.BackupRoot` (mirroring `DataMountRoot`; absolute, validated), defaulting to an Agent-owned sibling of the data root (`DefaultAgentFile("backups")`). An archive lands at `<BackupRoot>/<serverId>/<archive-name>.tar.gz`. **Why configurable, not a fixed `<serverId>.backups` sibling of the data dir:** an operator with a large world will want backups on a *different* disk than the live game data (the same disk-separation concern F16/F17 already honour by keeping the ~6.72 GiB install out of the world tree). The Backup record stores a **relative** locator (the per-server archive name), never an absolute host path — the path is reconstructed from `BackupRoot + serverId + name`, so backups stay portable if the root moves. **Consequence:** one new option + validator entry; no allowlist change (host I/O is not socket-governed).
- **D-ARCHIVE — `.tar.gz` produced by the BCL, streamed, integrity-checked by SHA-256 over the produced file.** The writer uses `System.Formats.Tar.TarWriter` over a `GZipStream` (both net10.0 BCL; no new dependency). `.tar.gz` is Linux-native (the Agent runs on Linux hosts), streams the tree without a staging copy, and preserves the PZ file tree faithfully for F25 to untar. **The walk is bounded:** it archives **only** the `/pz/data` world tree (`<DataMountRoot>/<serverId>`), **excludes** the `<serverId>.server` SteamCMD install (a host *sibling*, deliberately outside the world tree), and **does not follow the `data/workshop` symlink** (it points into the ephemeral runtime tmpfs — following it would pull Workshop content that is not world data and may not exist at rest). The checksum is `SHA256` over the **produced `.tar.gz` bytes** (streamed via `IncrementalHash`/`SHA256.HashData(Stream)`), emitted as **lowercase hex** (`Convert.ToHexStringLower`, the repo idiom) — the exact value F25 re-verifies before restoring. The archive is written with the Agent's **atomic file-store idiom** (temp file in the destination dir → `File.Move(overwrite: true)`), so a crashed backup never leaves a half-archive at the final name.
- **D-SURFACE — one `Backup` Operation; F24 ships the pre-op API seam, retention *metadata*, and manual delete; retention *enforcement* is F26.** One `OperationKind.Backup` (mutating, server-scoped → per-server lock, so a backup never races a lifecycle Operation). The `Backup.*` permissions **already exist** (`Backup.View/Create/Restore/Delete`, server-scopable, role-bundled) — F24 wires `Backup.Create` (take) and `Backup.Delete` (manual delete), leaving `Backup.Restore` for F25. F24 ships **`IPreOperationBackup`** as a callable seam (take a backup, tagged `BackupReason.PreOperation`, before proceeding) but **wires no Operation to it** — each risky feature calls it on its own schedule, and F25's protective backup is the first real caller. **Retention metadata** (`BackupReason` Manual|PreOperation, `CreatedAt`, `SizeBytes`, `Sha256`, optional `ExpiresAt` hint) is recorded; **auto-pruning/enforcement is deferred to F26** (scheduling is explicitly out of F24). Manual delete is included because the permission already exists and F25/restore will want a "clean up" affordance; it removes both the record and the Agent-side archive.

## Scope, by PR

### PR-A — Engine: domain + contracts + persistence + Agent archive runner (branch `feat/f24-backup-foundation`)

1. **Domain (`ZWarden.Domain`):**
   - `Backups/Backup.cs` — `sealed class : ITenantOwned` (write-once record; no `IVersioned`). `BackupId Id` (prefix `bkp-`, **already in `TypedIds.cs`**), `ServerId ServerId`, `AgentId AgentId`, `string ArchiveName` (relative locator), `long SizeBytes`, `string Sha256`, `BackupReason Reason`, `DateTimeOffset CreatedAt`, `DateTimeOffset? ExpiresAt`. Static factory `Backup.Record(...)` validating non-empty checksum/name and non-negative size, leaving `TenantId` unset for the interceptor.
   - `Backups/BackupReason.cs` — enum `Manual = 0`, `PreOperation = 1` (stored by name).
   - `Operations/OperationKind.cs` — append `Backup = 16` with a doc summary (mutating, server-scoped, per-server lock; produces a checksummed `.tar.gz` host-side, no `exec`).
2. **Contracts (`ZWarden.Contracts`, all additive — ADR 0020, `ProtocolVersion.Current` stays 1):**
   - `Protocol/Messages/BackupServer.cs` — `[ProtocolMessage("lifecycle.backup-server")] public sealed record BackupServer : AgentCommand;` — **parameterless** (like `UpdateServer`): the target is the envelope's `ServerId`, and the `BackupReason` is control-plane retention metadata carried in `Operation.CommandPayload` for the completion-ingest to read, never sent to the Agent.
   - `OperationCompleted.cs` — new optional `BackupResult? Backup = null` slot + a `BackupResult(string ArchiveName, long SizeBytes, string Sha256, DateTimeOffset CreatedAt)` record (nullable ⇒ additive, mirrors `UpdateResult`).
   - Tests: `ClosedCommandVocabularyTests`/`ProtocolCompatibilityTests` (additivity + closed vocabulary), serialization round-trip.
3. **Persistence (`ZWarden.Infrastructure` + both migration assemblies):**
   - `Backups/BackupConfiguration.cs` — `ToTable("Backups")`, key, `.IsRequired()`/`.HasMaxLength()` on `ArchiveName`/`Sha256`, `Reason` via `.HasConversion<string>()`, index on `ServerId`. No id/tenant/version wiring (conventions handle it).
   - `Backups/BackupRepository.cs : TenantScopedRepository<Backup>` — `ListForServerAsync(ServerId)`, `FindByIdAsync(BackupId)`; `BackupsServiceCollectionExtensions.AddZWardenBackups` (`AddScoped<BackupRepository>()`).
   - `AddBackups` migration for **both** `src/ZWarden.Migrations.Sqlite` and `.Postgres` (CONTRIBUTING §Persistence). Operations table unchanged (`Backup` is a new enum value in the existing string column — no operations migration).
4. **Agent (`ZWarden.Agent`):**
   - `Configuration/AgentOptions.cs` — `BackupRoot` (default `DefaultAgentFile("backups")`) + `AgentOptionsValidator` absolute-path check.
   - `Backups/IServerBackupPaths.cs` / `ServerBackupPaths.cs` — resolves the source world dir (`<DataMountRoot>/<serverId>`) and the destination dir (`<BackupRoot>/<serverId>`); allocates a timestamped archive name.
   - `Backups/TarGzBackupArchiver.cs` — pure archiver: walk the world tree (skip the `.server` sibling by construction; **skip the `data/workshop` symlink**; skip in-progress temp files), stream into `TarWriter` over `GZipStream`, hash the produced file, atomic `File.Move`. Returns `(ArchiveName, SizeBytes, Sha256)`.
   - `Backups/IServerBackupRunner.cs` / `ServerBackupRunner.cs` — orchestrates paths + archiver, returns a terminal `ServerBackupOutcome`. DI in `HostingExtensions`.
   - `ControlPlane/AgentCommandProcessor.cs` — `case BackupServer:` — require `ServerId`, dedupe by `OperationId`, run the backup runner, `Completed(Succeeded, …, backup: new BackupResult(...))` or `Completed(Failed, reason)`.
   - Tests: archiver (excludes install; does not follow the workshop symlink; deterministic checksum; atomic rename; empty-world edge), runner, processor case with a fake runner (dedupe, failure), options validator.

### PR-B — Surface: enqueue + dispatch + ingest + pre-op API + endpoint + UI (closes #45; branch `feat/f24-backup-surface`)

1. **Application/Infrastructure orchestration:**
   - `Servers/IServerBackup.cs` / `ServerBackup.cs` — `CreateAsync(user, server, reason)` → resolve through the tenant filter → authorize `Permissions.BackupCreate` → `EnqueueAsync(new EnqueueOperationRequest(server.AgentId, OperationKind.Backup, IsMutating: true, key, ServerId: serverId, payload: reason))` → audit `ServerAuditActions.BackedUp` → `catch (ServerBusyException) ⇒ ServerBusy`. Near-verbatim copy of `ServerLifecycle.RunAsync`.
   - `Backups/IPreOperationBackup.cs` / impl — the seam: `EnsureBackupAsync(user, server)` taking a `PreOperation`-tagged backup via the same enqueue path. **No caller wired in F24.**
   - `IServerBackup.DeleteAsync(user, backupId)` → authorize `Permissions.BackupDelete` → enqueue a **non-mutating** `DeleteBackup` Operation on the backup's Agent (new `OperationKind.DeleteBackup`, new parameterized `DeleteBackup(ArchiveName)` command, path-traversal-guarded Agent handler); the record is removed at **completion ingest** (`BackupDeletionResult` signal → record id from the command payload). *(Chosen over a record-only delete so a failed cleanup never orphans the host archive.)*
2. **Dispatch + ingest (`ZWarden.Web`):**
   - `Operations/OperationDispatcher.cs` — `case OperationKind.Backup ⇒` build `new BackupServer(reason)` from `Operation.CommandPayload` (covered by `OperationDispatcherMapTests`).
   - `OperationCompleted` ingest — when `BackupResult` is present, `Backup.Record(...)` a new tenant-owned entity through the reconciler (create-on-completion; the in-flight state is the Operation).
3. **Authorization/audit:** no permission-catalogue change (`Backup.*` already present + role-bundled). Add `ServerAuditActions.BackedUp` (mirrors `Updated`).
4. **Endpoint/UI:** `POST /servers/{id}/backup` (copy the lifecycle endpoints, `202 Accepted` + `operationId`); a **Backups** panel on the server-detail view — a **`BbDataGrid`** of the Server's backups (created, reason, size, checksum, expiry) with a **Take backup** action and a **Delete** action, gated by `Backup.View`/`Backup.Create`/`Backup.Delete`. Per-PR chore: bump the Web.Tests floor in **both** the csproj and `ci.yml` `tier1-silent-drop-guard` ([[web-tests-discovery-floor-bump]]); `npm run build:css` + commit `wwwroot/app.css` if styles change.
5. **ADR 0028** — the backup archive contract: `.tar.gz` of the world tree only (install excluded, workshop symlink not followed), SHA-256-over-produced-file integrity, the configurable `BackupRoot` + relative locator, retention metadata vs deferred enforcement, and the pre-operation backup seam.

## Non-scope

- **Restore** (F25) — validation, archive safety, staging, protective backup, atomic restore, health verification. F24 produces the artifact F25 consumes; it never reads a backup back.
- **Remote destinations** (post-1.1) — S3/SFTP/etc. F24 is local-disk only.
- **Scheduling / automated backups** (F26) — F24 is operator-initiated (plus the callable pre-op seam); no timer, no cron, no auto-prune. Retention metadata is *recorded*, not *enforced*.
- **Wiring any Operation to the pre-op backup seam** — the seam ships; the first real caller is F25's protective backup, then F17/F20b at their own discretion.
- **Incremental / differential backups, dedup, encryption-at-rest of the archive** — a full `.tar.gz` each time; the archive inherits host-disk protection. (Archive encryption is a later hardening item.)
- **Backing up the SteamCMD install or Workshop content** — reinstallable via F17/F21; the world tree is the irreplaceable data.

## Domain / contract / persistence changes

- **Domain:** `Backup` entity + `BackupReason` enum; `OperationKind.Backup` (append).
- **Contracts (additive, no version bump):** parameterless `BackupServer` command; optional `BackupResult` on `OperationCompleted`. Assert `ProtocolVersion.Current == 1` holds.
- **Persistence:** one `AddBackups` migration per provider (new `Backups` table). Operations table unchanged.
- **Authorization:** none — `Backup.View/Create/Restore/Delete` already in the closed catalogue and role bundles.

## Test plan (TDD, per PR)

- **PR-A:** `Backup` factory/invariants (rejects empty checksum/name, negative size; leaves `TenantId` unset); `BackupReason` stored-by-name; Contracts serialization + additivity (`ProtocolVersion.Current == 1`, closed vocabulary); `BackupConfiguration` mapping + `BackupRepository` tenant-scoped reads (Infrastructure.Tests on SQLite; IntegrationTests parity on Postgres); `TarGzBackupArchiver` (round-trips a temp world tree; **excludes** a sibling `.server` dir; **does not follow** a `data/workshop` symlink; identical bytes ⇒ identical lowercase-hex checksum; atomic rename leaves no temp on success; empty world ⇒ valid empty-ish archive, not a crash); `ServerBackupRunner`; `AgentCommandProcessor` backup case with a fake runner (progress/terminal, dedupe on redelivery, failure ⇒ Failed); `AgentOptionsValidator` (relative `BackupRoot` rejected).
- **PR-B:** `ServerBackup.CreateAsync` (not-found/foreign ⇒ ServerNotFound, unauthorized, ServerBusy, success audits `BackedUp`); `IPreOperationBackup` tags `PreOperation`; delete (unauthorized, not-found, success removes record + requests archive removal); `OperationDispatcherMapTests` covers `Backup`; completion-ingest creates a `Backup` from `BackupResult`; enqueue-endpoint + server-detail Backups-grid render tests (bUnit, loose JSInterop) gated by the three permissions.

## Diagnostics

- **A backup's integrity is verifiable, not assumed** — the SHA-256 over the produced `.tar.gz` is stored on the `Backup` and is the exact value F25 re-checks before restoring; a mismatch is a first-class restore refusal (F25).
- **Why a backup failed is legible** — a disk-full / permission / missing-world failure is the Operation's `FailureReason`; the Server is untouched (a backup only reads the world tree).
- **The world disk is protected from backup growth** — `BackupRoot` can point at a separate disk; the F16 disk meter still measures world-only, so backups never inflate the reported world size.
- **A stalled backup** stops extending the lease and the `OperationReaper` fails it, freeing the per-server lock without operator action (ADR 0022).

## Documentation

- `src/ZWarden.Agent/README.md` (or the options doc) — the `BackupRoot` option, the `<BackupRoot>/<serverId>/<name>.tar.gz` layout, and the world-tree-only archive contract.
- **ADR 0028** as above.
- CONTRIBUTING note if the archive/atomic idiom is reused pattern-wise.

## Acceptance criteria

1. A `Backup` Operation, authorized by `Permissions.BackupCreate` and serialized by the per-server lock, archives the Server's `/pz/data` world tree **host-side** (no `exec`, no `docker cp`) to `<BackupRoot>/<serverId>/…tar.gz`.
2. The archive **excludes** the SteamCMD install and **does not follow** the `data/workshop` symlink; it is written atomically (temp → rename).
3. A **SHA-256** checksum (lowercase hex) over the produced archive and its byte size are computed and reported, and a tenant-owned **`Backup`** record persists them plus **retention metadata** (reason, createdAt, optional expiry).
4. The **pre-operation backup API** (`IPreOperationBackup`) exists and takes a `PreOperation`-tagged backup, with **no** Operation wired to it in F24.
5. A backup **failure never harms the Server** — it only reads the world tree; the Operation is `Failed`, the Server unchanged, the reason honest.
6. Retention is **recorded, not enforced** (no auto-prune); manual **delete** (`Backup.Delete`) removes both the record and the Agent-side archive.
