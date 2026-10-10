# Issue #379 Mini-Plan — automatic backups before risky changes, keeping the last 5

**Status:** one PR (branch `feat/379-automatic-backups`). Epic #381; builds on #377 (backups work, save first).

**Written against:** issue #379; F24 (`ServerBackupRunner`, `BackupRecorder`, `IPreOperationBackup`); F25
(`ServerRestoreRunner`'s inline protective backup, ADR 0029); F20b/F20c (`ConfigApply`, `ConfigApplyRaw`); F22/#273
(`ServerModManager` — mod list edits are config applies, *Update mods* is a safe restart); F17 (`UpdateServer`);
ADR 0022 (one Operation, one lock), ADR 0020 (additive wire members), ADR 0028.

## Objective

Before a config apply (form or raw), a mod change, or a game update, the Agent takes a `PreOperation` backup of the
world. If that backup fails, nothing is changed and the Operation fails with the reason. After each automatic backup
is recorded, the oldest automatic backups of that server beyond `ZWarden:Backups:KeepAutomatic` (default 5) are
deleted and audited. Manual backups are never pruned.

## Facts found (2026-10-10)

- `IPreOperationBackup.EnsureBackupAsync` (Web side) only **enqueues** a separate mutating Backup Operation. Calling it
  before a config apply would need a second Operation, a wait for it, and the per-server lock handed between the two.
- Restore already does the right shape on the Agent: one Operation, inline protective backup, abort on failure
  (ADR 0029). The completion carries the backup facts and the hub records them as `PreOperation`.
- **Mod install/remove/enable/disable/reorder/parts/undo are all `ConfigApply`** on the INI (`ServerModManager` →
  `ConfigApplyEnqueuer`). Covering ConfigApply covers every mod change.
- **Update mods is a plain `RestartServer`** (`ServerModManager.UpdateModsAsync`, #273): PZ pulls Workshop updates at
  boot. A plain restart must **not** back up, so the restart needs a flag.
- **There is no branch-change operation.** The branch is fixed at create (`ServerBranchRules`, #258); Update and
  Recreate keep it. "Game update or branch change" is `UpdateServer` only.
- `DeleteWorkshopContent` (#293) removes re-downloadable Workshop files, not world data — no backup.
- A server that never started may have no `<DataMountRoot>/<id>` yet; `ServerBackupRunner.RunAsync` fails on that.
- The Operation lease is 5 min and is renewed only by progress reports. A big archive without progress could be
  reaped mid-apply.
- Infrastructure options (`ModRefreshOptions`, …) are registered with `AddOptions<T>()` but never bound to config.
  `KeepAutomatic` must be bound (`BindConfiguration("ZWarden:Backups")`) to be deploy-configurable.
- `Backup.CreatedAt` is a `DateTimeOffset`; SQLite can't `ORDER BY` it, so ordering is in memory (the per-server set
  is small).

## Decisions

- **D1 — the backup runs inside the Operation, on the Agent** (the restore shape). One Operation, one lock, and no
  window between the backup and the change. Not the Web-side `IPreOperationBackup` (two Operations and a lock handoff).
- **D2 — the Web decides, the Agent obeys: an additive `BackupFirst` flag** (ADR 0020, default `false`) on
  `ConfigApply`, `ConfigApplyRaw`, `UpdateServer` and `RestartServer`.
  - The dispatcher always sets it for ConfigApply, ConfigApplyRaw and UpdateServer.
  - For RestartServer it comes from the stored `GracefulRestartPayload.BackupFirst`, which only *Update mods* sets.
    The payload's `WarningLeadSeconds` becomes optional so a backup-only payload means "default countdown".
  - Start, stop, plain restart, Recreate, Delete, manual Backup and Restore never set it.
- **D3 — `IServerBackupRunner.RunPreOperationAsync(serverId, operationId, ct)`**: the same save-first archive as a
  manual backup, named `world-<utc>-<opId>-pre-op.tar.gz`. It returns `null` when the server has no data directory
  yet (nothing to protect), and the Operation then runs without a backup.
- **D4 — `AgentCommandProcessor` runs the backup first** for a flagged command, after the drift-free preconditions it
  can check cheaply (target Server, dedupe, raw text staged):
  - It reports progress "Taking an automatic backup first…" and keeps the lease alive with a progress heartbeat
    every 60 s while archiving.
  - On failure: `Failed` with `No backup could be taken first: <reason>. Nothing was changed.` and the command never
    runs.
  - On success: the command runs, and its completion — succeeded **or failed** — carries the backup in a new additive
    `OperationCompleted.PreOperationBackup` (`BackupResult`, warning included).
  - Mod-update restart: the backup runs before the countdown starts (the world is saved over RCON first, as for a
    manual backup).
- **D5 — the hub records it whatever the outcome**, like Provision: `IBackupRecorder.RecordPreOperationBackupAsync`
  (the generalised `RecordRestoreProtectiveBackupAsync`, now carrying the warning; restore uses it too). It audits
  `Server.BackedUp` ("automatic, before operation …").
- **D6 — retention: `IBackupRetention.PruneAsync(serverId, ct)`** in Infrastructure, called by the hub after every
  `PreOperation` record — the new automatic ones **and** restore's protective one (both are `PreOperation`).
  - Keeps the newest `KeepAutomatic` `PreOperation` backups (by `CreatedAt`, then id), and for each older one enqueues
    the existing non-mutating `DeleteBackup` Operation (the record goes on its confirmed completion) and audits
    `Server.BackupPruned` with the system actor.
  - No permission check: retention is policy, not an operator action.
  - Never touches a `Manual` backup. A delete that fails leaves the record, and the next prune retries it.
- **D7 — `BackupOptions { KeepAutomatic = 5 }`** bound to `ZWarden:Backups`, validated `>= 1` on start.
- **D8 — the operator sees the policy**: Settings → Backups gains a "Automatic backups" row ("Last 5 per server, taken
  before config, mod and game-update changes; manual backups are kept until deleted"), and the server's Backups panel
  shows the same line under its list. Both read `IOptions<BackupOptions>`.
- **D9 — docs**: amend ADR 0028 (the seam is superseded by inline Agent backups for these operations; retention is
  enforced for `PreOperation` only), fill in `docs/deployment/backups.md` (automatic backups, retention, DMZ steps), and
  note in ADR 0029 that restore's protective backup now counts toward retention.

## Slices (TDD)

1. Contracts: `BackupFirst` on the four commands, `OperationCompleted.PreOperationBackup`; round-trip tests.
2. Agent: `ServerBackupRunner.RunPreOperationAsync` (name marker, null with no data dir, failure reason).
3. Agent: processor pre-op step for ConfigApply / ConfigApplyRaw / UpdateServer / RestartServer — flagged runs back up
   first; unflagged never; failure aborts with the message and the writer/runner is never called; completion carries
   the backup on success and on a failed command; heartbeat progress.
4. Web: dispatcher sets the flag per D2; `GracefulRestartPayload.BackupFirst`; `UpdateModsAsync` sets it.
5. Infrastructure: `RecordPreOperationBackupAsync` (+ warning), `BackupOptions`, `BackupRetention.PruneAsync`
   (keeps N, never Manual, audits each, ownership/tenant-scoped).
6. Web hub: ingest `PreOperationBackup` on success and failure, audit, prune; restore path prunes too.
7. UI: Settings → Backups row and Backups panel line (bUnit).
8. Docs: ADR 0028/0029 amendments, backups guide, floors bumped.

## DMZ live pass (after CI, before merge)

Rebuild web + agent. Then: a form config change, a raw config change, a mod install and *Update mods*, and a game
update each add a "Pre-operation" row first; a refused apply when the backup can't be written (e.g. make
`<BackupRoot>/<id>` read-only) leaves the config unchanged with the "No backup could be taken first" reason; after
the 6th automatic backup the oldest automatic one disappears (audit shows `Server.BackupPruned`) and no Manual
backup is touched.
