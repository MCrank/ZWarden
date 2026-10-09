# Issue #377 Mini-Plan — backups and restore work end to end

**Status:** one PR (branch `fix/377-backup-restore-work`). Epic #381; #379 (automatic backups + retention) builds on it.

**Written against:** issue #377; F24 (`ServerBackupRunner`, `TarGzBackupArchiver`, `BackupRecorder`); F25
(`ServerRestoreRunner`, `RestoreArchiveExtractor`); F18 (`IRconEndpointResolver`, `IRconConnectionFactory`); F28
(`ConsoleAdministration`, the same RCON path); #368 (`HostReplacementService` moves Backups); ADR 0028 (backup
format/location), ADR 0029 (restore safety), ADR 0020 (additive wire members).

## Objective

A manual backup of any server succeeds. A running server's world is saved over RCON first. If RCON is down, the
backup still runs and says, on its row in the Backups list, that the world wasn't saved first. Restore works end to
end, and an operator guide covers the lifecycle and doubles as the DMZ checklist.

## Facts found (2026-10-09)

- `pz_admin_password` (`pz-lib.sh`) writes `.zwarden-adminpw` under `umask 077` (0600). The Agent (10001:10000)
  can't read it, so `TarGzBackupArchiver.WriteTree` → `tar.WriteEntry` throws `UnauthorizedAccessException`. The
  runner maps that to a failed backup. Restore's inline protective backup fails the same way, so restore aborts before
  the swap.
- An existing file returns early through `cat`. The chmod has to sit on that path too, not only on the write path.
- The entrypoint runs `set -euo pipefail` as `pzserver` (10000:10000). A world restored by the Agent holds files
  **owned by 10001**, which `pzserver` can't chmod. A bare `chmod` would kill the container on start, so the fix
  must tolerate that.
- `TarEntry.ExtractToFile` on Linux applies the entry's mode (under the process umask, `0002`). A 0640 file restored
  by the Agent is 10001:10000 0640, which PZ can read through group 10000. That's fine.
- PZ's `save` (research §runtime, `SaveCommand`) replies `World saved` once it has **queued** the save. No reply marks
  completion.
- `OperationCompleted` has no success-message field. `BackupResult(ArchiveName, SizeBytes, Sha256, CreatedAt)` is
  the backup payload.
- `ServerRestoreRunner` refuses a running container and already has `IsContainerRunningAsync` (via
  `ListManagedAsync`). Restore only runs stopped, so its protective backup never needs a save.
- `docs/operations/` doesn't exist; the operator guides live in `docs/deployment/`.

## Decisions

- **D1 — the password file is 0640, fixed on every start.**
  - `pz_admin_password` writes under `umask 027`.
  - A new `pz_fix_admin_password_mode <data_dir>` runs from the entrypoint before `pz_admin_password`. When the file
    exists and isn't 0640, it runs `chmod 0640`, tolerating failure (`|| true`, with a log line when it can't). The
    content never changes.
  - An operator-set `ZW_PZ_ADMIN_PASSWORD` still writes no file.
- **D2 — the archiver stays fail-closed.** Any other unreadable file still fails the whole backup. A backup that
  silently drops a file is worse than one that refuses, and the failure message names the path.
- **D3 — save first, through an Agent `IWorldSaver`.**
  - `SaveAsync(serverId, ct)` returns `WorldSaveResult { Saved, NotRunning, Failed(reason) }`.
  - It resolves the RCON endpoint (NoContainer → NotRunning; RconDisabled → Failed), connects, and sends `save`,
    bounded by `AgentOptions.Backup.SaveTimeout` (default 60 s).
  - It then waits a **fixed settle**, `AgentOptions.Backup.SaveSettle` (default 10 s, via `TimeProvider`), because
    PZ only queues the save. The default gets timed on the DMZ.
  - It uses the RCON client directly, not `ConsoleAdministration`. This is an Agent-authored command, not operator
    input, so the console policy doesn't apply. The connection is always disposed.
  - Whether the server is running is decided by the managed container's state (as in restore), not by RCON. A
    stopped server sends no save.
- **D4 — the backup carries an optional warning.**
  - `ServerBackupRunner.RunAsync` calls the saver before archiving. On `Failed`, it archives anyway and sets
    `Warning = "The world could not be saved before this backup (<reason>), so the most recent changes may be
    missing."`.
  - `ServerBackupOutcome` and the wire `BackupResult` gain `string? Warning = null` (additive, ADR 0020; an old Agent
    sends none). The value is Agent-authored and untrusted at render.
- **D5 — the warning is stored and shown on the row.**
  - `Backup.Warning` is a nullable column (≤ 512, truncated like `Operation.MaxReportedTextLength`). Migrations for
    SQLite and Postgres.
  - `BackupRecorder.RecordCreatedAsync` stores it and adds it to the audit entry.
  - `BackupsSection` shows a warning `BbBadge` ("Not saved first") with the text as a tooltip.
  - The restore protective backup takes no save (the server is stopped), so it never has a warning.
- **D6 — the guide is `docs/deployment/backups.md`**, linked from getting-started and the compose reference. It
  covers:
  - what an archive holds and excludes (install, the `workshop` symlink);
  - where archives live (`/srv/zwarden/pz-backups/<serverId>/`);
  - the save-first behaviour and its warning;
  - automatic backups (today only restore's protective one; #379 adds more);
  - retention (none yet, #379);
  - restore step by step;
  - failure messages and what to do;
  - the role and permission matrix;
  - the DMZ checklist.

## Out of scope

- Automatic pre-operation backups and retention (#379); schedules, caps and a retain flag (#378).
- Skipping unreadable files (D2), and log-watching for save completion (D3 picks the fixed settle).

## Tests (TDD slices)

1. **bats** (`tests/pzserver/layout_launch.bats`):
   - a generated file is `640`;
   - an existing `600` file becomes `640` with the same content;
   - a file the caller can't chmod doesn't fail the function.
2. **Agent `WorldSaver`**, with fake resolver, connection and `TimeProvider`:
   - Saved → sent `save` and waited the settle;
   - NoContainer → NotRunning, nothing sent;
   - RconDisabled, auth failure, timeout or transport failure → Failed with a reason;
   - the connection is always disposed.
3. **Agent `ServerBackupRunner`:**
   - running + Saved → archive, no warning;
   - running + Failed → archive written, warning set;
   - stopped → no save, no warning;
   - the save always runs before `Create` (call order).
4. **Agent `TarGzBackupArchiver`** (Linux-only, skipped on Windows like the other mode tests): a 0640 file owned by
   the test user is archived, and the archive contains it.
5. **Agent integration round trip:** backup a temp world → mutate it → restore through `ServerRestoreRunner` → the
   world equals the backup and a protective archive exists.
6. **Contracts:** `BackupResult` round-trips `Warning`, and a payload without it reads as null.
7. **Domain/Infra:**
   - `Backup.Record` keeps and truncates the warning;
   - the recorder persists it and puts it in the audit entry;
   - the migration applies on both providers.
8. **Web (bUnit):** the row shows "Not saved first" when a warning is present, and nothing otherwise.
9. **Permissions:** confirm the existing Viewer/Operator coverage for take, restore and delete, and add any that's
   missing (no new rules expected).
10. Floors: bump the discovery floors in csproj and `ci.yml` to the exact counts.

## Live check (DMZ) — rebuild web, agent **and** the PZ image

1. Restart Biggie's server. `.zwarden-adminpw` becomes 0640, and the admin password still works.
2. Take a backup while running. The log shows `save` and then the archive, and the row has no warning.
3. Stop RCON reachability (or test against a server mid-boot), take a backup, and check that the row shows "Not
   saved first".
4. Make an in-game change, stop, and restore the earlier backup. The protective backup appears, the server starts,
   the change is gone, and the admin password works.
5. Optional: tamper with an archive's bytes, then restore. It's refused with the checksum message.
6. Restore from Y the backup the #368 Replace moved, and check that the Replace audit said "1 backup(s) moved".
7. Time a real `save` on the DMZ world to confirm the 10 s settle.
