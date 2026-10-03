# Feature #293 Mini-Plan — Delete unused Workshop downloads from disk

**Status:** in progress — D1–D6 accepted as recommended (2026-10-03). v1.0, epic [#289](https://github.com/MCrank/ZWarden/issues/289). Needs #292
(done: #320 + #321), which built the `LeftoverDownloads` footer with Delete hidden.

**Written against:** issue #293; [F292](./F292-mods-variant-b-ui.md) (`ModTable.Leftovers`, `LeftoverDownloads`);
[F290](./F290-mod-installed-data.md) (`ModChangeSet`, `ModChangeStatus.Leftover`); [F291](./F291-one-click-install.md)
(slice 0 spike log); [F271](./F271-delete-server.md) (confirm dialog); F24 `DeleteBackup` (the closest Agent
analogue: a host-side file delete); ADR 0008 (no exec, Agent code is the control); ADR 0022 (per-server lock).

## Objective

An operator can delete the files of Workshop items the server neither loads nor will load, one at a time or all at
once, from the "unused downloads" footer. The Agent does the delete and refuses anything still referenced, anything
that isn't a plain Workshop id, and any server it doesn't own. Every delete is audited.

## Open questions, settled

### 1. Where PZ downloads Workshop items → the install volume. Discovery is right; `pz-lib.sh` is stale.

- The F291 spike log (PZ 42.21, local container) shows PZ downloading during startup to
  `installed to /pz/server/steamapps/workshop/content/108600/2553809727`. `/pz/server` is the install volume, which is
  bound from the host's `<DataMountRoot>/<serverId>.server`. That is exactly what
  `ServerInstallPaths.GetWorkshopContentRoot` returns, so discovery and delete already use the same location.
- `pz_create_layout` makes `/pz/runtime/steamapps/workshop/content/108600` and symlinks `/pz/data/workshop` to it.
  That came from the F12 research ("Workshop lives under the Steam root"), which is true for a standalone SteamCMD
  `workshop_download_item`, but the server downloads relative to its own install dir. Nothing reads or writes that
  directory, `/pz/runtime` isn't a volume, and so the symlink points at an empty folder.
- **Fix (in PR-A):** point the `data/workshop` symlink at `/pz/server/steamapps/workshop/content/108600` and stop
  creating the runtime folder. Update the bats test, the PZServer README table, and the ADR 0009 note so the docs
  match the evidence. Delete uses `GetWorkshopContentRoot`; it does not get its own path.

### 2. Deleting while the server runs → safe, for leftovers only.

- A leftover is, by definition, neither in `WorkshopItems=` now nor in the set the server booted with
  (`ModChangeSet`). PZ loads only the items it booted with, so it has no reason to open a leftover's files.
- Linux also keeps an open file's data alive until it is closed (`rm` only unlinks the name). So even a stray open
  handle can't crash the JVM; at worst the space comes back once it's closed.
- The risky case is an item that was **booted with but has since been removed** (`RemovedOnRestart`). It may still be
  loaded, and PZ can read mod files lazily (textures, sounds). Two layers keep it out:
  - the control plane accepts only ids whose status is `Leftover`;
  - the Agent refuses any id in `WorkshopItems=`. It can't know the booted set, but this is the issue's hard guard.
- So delete is allowed whether the server is running or stopped. No restart is involved.
- **One thing to verify** (slice 0, or else the DMZ pass): after deleting an item's folder, does Reinstall + restart
  download it again? Steam keeps `steamapps/workshop/appworkshop_108600.acf` listing installed items. If a stale entry
  makes Steam skip the download, the Agent must also drop that entry. The plan assumes Steam notices the missing
  folder; the spike decides.

### 3. Confirm-dialog pattern → `BbAlertDialog`, without typed confirmation.

#271 started as a native `<dialog>` with JS while the page was static SSR. Since #299, `OverviewSection` uses
`BbAlertDialog` (`@bind-Open`, `BbAlertDialogCancel`, a destructive action button). Delete reuses that shape:

- **Delete** (per row) → "Delete the files for *Title* (1.2 GB)? The server isn't using them. Reinstall downloads
  them again."
- **Delete all** (footer, shown when there are 2+ leftovers) → "Delete N unused downloads (total size)?" plus the
  list of titles.
- No typed name. Typing the name is for deleting a server and its world. This can be undone by a re-download.

## Decisions (accepted 2026-10-03)

- **D1 — Operation shape.** New Contract `DeleteWorkshopContent(IReadOnlyList<string> WorkshopIds)` and
  `OperationKind.DeleteWorkshopContent = 25`, one Operation per click (Delete all = one Operation with all ids). The
  Agent deletes what it may and reports per-id results (`deleted` / `refused: referenced` / `absent`). Absent counts as
  success, so a redelivered command is idempotent. A bad id (non-numeric) fails the whole command before anything is
  touched.
- **D2 — Take the per-server lock.** Unlike `DeleteBackup`, this touches the install volume that Update/SteamCMD and
  a booting server write. Taking the lock rules out racing a Reinstall + restart. The cost is that Delete is refused
  while another Operation runs, and the UI already disables actions while busy.
- **D3 — Ownership.** The Agent resolves the server's container with `FindOwnedAsync` (the `ContainerOwnershipGuard`
  path every Docker verb uses). No owned container → refused. This also refuses a deleted server whose volumes
  linger (#283 covers that cleanup).
- **D4 — Path safety.** Each id must match `^[0-9]{1,20}$`. The target is `Path.GetFullPath(Path.Combine(root, id))`
  and must have `root + separator` as its prefix. The folder itself must not be a symlink or reparse point; if it is,
  refuse, because deleting recursively through a link could escape. Delete with `Directory.Delete(path, recursive: true)`.
- **D5 — Afterwards.** On a succeeded completion, the control plane drops the deleted items' `OnDisk` flag on the
  cache rows (`ServerWorkshopItem`) and queues a mod re-discovery (the existing refresh scheduler). The Mods
  section's poll then removes the rows. Audit `Mod.DownloadsDeleted` with the ids and the per-id outcome. A refused
  request (permission, non-leftover id) is audited as refused, like `ServerLifecycle.DeleteAsync`.
- **D6 — Two PRs**, as with #271: **PR-A** (Contracts + Agent + the `pz-lib.sh` path fix) and **PR-B**
  (Domain/Application/Infrastructure/Web, stacked on PR-A; closes #293).

## Slices (TDD)

0. **Spike (optional, local `zwarden-pzserver`):** with the server running, run `ls -l /proc/<java>/fd` and check
   that no `workshop/content` handles are open for a non-configured item. Then delete an item's folder → re-add → restart →
   check whether it downloads again (question 2). If the image isn't cached locally, this moves to the DMZ pass.
1. **Contracts:** `DeleteWorkshopContent` message, `WorkshopContentDeletionResult` on `OperationCompleted`, protocol
   round-trip tests.
2. **Agent `WorkshopContentRemover`:** ownership → read `WorkshopItems=` (shared reader with `ModDiscovery`) →
   per-id guard → delete. Tests: deletes an unreferenced item; refuses a referenced one; rejects a non-numeric id,
   `..`, a separator or an absolute id; refuses a symlinked item folder; refuses an unowned server; absent = success.
   Wire it into `AgentCommandProcessor` (dedupe by OperationId).
3. **`pz-lib.sh` layout fix** + bats + docs (question 1).
4. **Domain/Application:** `OperationKind 25`, `IServerModManager.DeleteDownloadsAsync(user, server, ids)`:
   `Mod.Remove` + server scope, every id must be `Leftover` in the current overview, otherwise `InvalidInput`. Dispatcher
   mapping. Tests in `ServerModManagerTests`.
5. **Completion handling:** clear `OnDisk`, queue re-discovery, audit.
6. **Web:** Delete + Delete all in `LeftoverDownloads` behind `CanDelete` (Mod.Remove), both `BbAlertDialog`s, busy
   state, the success/failure message through the existing section status. bUnit tests, plus a playwright check on
   run-web/aspire.

## Acceptance

- Agent tests as in slice 2 (the issue's list).
- Live (DMZ): delete one leftover and Delete all while the server **runs**. `ls` shows the folders are gone, the rows
  disappear, the server keeps running, and the audit has the entry. Then Reinstall a deleted item + restart, and it
  loads again.

## Out of scope

- Deleting a `RemovedOnRestart` item's files (do it after the restart, when it becomes a leftover).
- Automatic cleanup on Remove (it stays an explicit operator action).
- World/volume deletion for deleted servers (#283).
