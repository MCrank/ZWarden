# 25. SteamCMD updates are driven through the container, not by exec

A SteamCMD **update** (install/update/validate — one Valve verb) is a mutating, server-scoped Operation
(ADR 0022) the Agent drives **without `exec`**, because the socket allowlist denies it (ADR 0008). The Agent
**drops a control-file into the writable data volume carrying the OperationId, restarts the container, and
observes `docker logs`** — the entrypoint runs `app_update 380870 … validate` past the install marker,
brackets its output with a `steamcmd update session <id> begin … end (success|failure)` banner, and the
Agent parses that log into `OperationProgress` and a terminal outcome. Success/failure is **read from stdout**,
never an exit code (ADR 0009). To make this runnable and persistent on a read-only-rootfs container, the
install lives on a **persistent `/pz/server` bind mount** and SteamCMD's self-updating client + the FIFO live
on an **ephemeral `exec` tmpfs at `/pz/runtime`**, with the baked SteamCMD relocated to `/opt/steamcmd` and
copied in on boot. A **failed update never bricks a working server** — it boots on the prior install and the
Operation reports the failure.

- Status: accepted
- Decided in: [#38](https://github.com/MCrank/ZWarden/issues/38) (Feature 17), building on [#33](https://github.com/MCrank/ZWarden/issues/33) (F11 operations) and [#21](https://github.com/MCrank/ZWarden/issues/21)/[#34](https://github.com/MCrank/ZWarden/issues/34) (F12/F13 container + runtime)
- Bears on: PRD 30 (server update), [ADR-0008](./0008-docker-socket-access-via-wollomatic-socket-proxy.md) (allowlist — no new verb), [ADR-0009](./0009-steamcmd-at-runtime-is-mandatory.md) (stdout parsing, undocumented exit codes), [ADR-0022](./0022-operation-lifecycle-and-per-server-locking.md) (operation lifecycle + per-server lock + lease/reaper), [ADR-0020](./0020-agent-protocol-versioning-and-catalogue.md) (additive contract change), [ADR-0023](./0023-server-health-model-and-observed-delivery.md) (observed-never-inferred)

## Context

F12 installs Project Zomboid on first run only; F17 must make **updating** it a first-class, durable,
progress-reporting Operation. Three measured facts shape the design:

- **`exec` and `attach` are denied** (ADR 0008 — granting `/containers` would silently grant
  `/containers/{id}/exec`). The Agent therefore cannot run SteamCMD inside a live container; execution has to
  stay in the container's own entrypoint.
- **SteamCMD's exit codes are undocumented** (ADR 0009 — Valve's own tracker issue, open since 2016). Observed
  values include `0`, and `7`/`42` for a self-update-restart. The outcome must be parsed from stdout.
- **The F13 create template runs a read-only root filesystem** with only `/pz/data` writable. SteamCMD installs
  into `/pz/server`, self-updates its own client into its directory, and needs the `/pz/runtime` FIFO — none of
  which could persist, or even run, under that template. So the whole update path was latently unrunnable on an
  Agent-created container until this feature.

## Decision

- **Trigger.** The Agent writes `/pz/data/.zwarden-update-requested` (a control-file in the already-writable
  data bind, host-side — it owns `DataMountRoot`) containing the **OperationId**, then issues the allowlisted
  **restart** (its safe stop timeout runs the blessed FIFO `save`→`quit`). On reboot the entrypoint sees the
  control-file, runs `app_update 380870 [-beta $ZW_PZ_BETA] validate` **past** the install marker, and always
  deletes the control-file afterwards (no restart loop).
- **Observation.** The entrypoint brackets the run with `steamcmd update session <OperationId> begin` … `end
  (success|failure)`. The Agent polls `GET /containers/{id}/logs` (a **read** verb already in the allowlist —
  **no allowlist change**), parses the session window (`SteamCmdLogParser`), emits `OperationProgress` from
  SteamCMD's `Update state (0x…) …, progress: NN.NN` lines (each report **extends the operation lease**), and
  takes the terminal outcome from the entrypoint's authoritative end banner.
- **Storage.** `/pz/server` (install + `steamapps/appmanifest_380870.acf`) is a **persistent host bind mount**,
  a host **sibling** of the data dir so the ~6.72 GiB install is not counted by F16's world-data disk meter.
  `/pz/runtime` is an **ephemeral `exec` tmpfs** (SteamCMD's self-updating client + the FIFO — nothing under it
  must survive a recreate; Workshop persistence is F21). SteamCMD is baked at **`/opt/steamcmd`** (outside any
  `/pz` mount, so a mount cannot shadow it) and copied into the tmpfs on boot. The root filesystem stays
  read-only; these are host **bind/tmpfs mounts on the create body**, not `docker volume create` (unallowlisted).
- **Version.** On success the Agent reads the installed `buildid` from `appmanifest_380870.acf` host-side (the
  install bind is host-visible) and reports it on `OperationCompleted.Update` (`UpdateResult`, additive per
  ADR 0020); ZWarden.Web persists it on the `Server`.
- **Failure is non-fatal to a running server.** Unlike F12's first-*install* fail-closed exit, a failed
  *update* leaves the existing install intact, boots the server on it, and the Operation is `Failed` while the
  Server is `Running` on the prior build — reported honestly, never a hopeful guess.
- **One combined kind.** `OperationKind.UpdateServer` covers install/update/validate (one SteamCMD verb); a
  repair is this same Operation run again. No separate Install/Validate kinds.

## Alternatives considered

- **`docker exec steamcmd` into the running container.** The obvious shape, and rejected not on taste but on
  ADR 0008: the allowlist denies `exec`/`attach`, and widening it would reopen the single most dangerous verb
  (a shell inside a container ZWarden runs). The control-file + restart is strictly less privileged.
- **A one-shot updater sidecar container.** The Agent may `create`/`start` but not `remove` (no delete verb), so
  sidecars would accumulate with no cleanup path, and it doubles the per-server container count. Rejected.
- **Relocate the whole install onto `/pz/data`.** Simplest (one existing mount), but co-locates the 6.72 GiB
  install with the save world — undoing F12's deliberate separation (the devs keep them apart precisely so an
  update cannot corrupt saves) and inflating the world-data disk meter. Rejected; the maintainer chose separate
  storage.
- **Persist `/pz/runtime` too.** Would give a head start on F21 Workshop persistence and skip the per-boot
  SteamCMD re-bootstrap, but it persists mostly regenerable client bits and adds a second volume per server to
  back up. Deferred to F21, which can swap the tmpfs for a bind on the Workshop subtree.
- **Branch on SteamCMD exit codes.** Rejected by ADR 0009 — they are undocumented and observed to vary by
  platform. Stdout is the contract.

## Consequences

- **The update path finally runs on an Agent-created container.** This feature, not F14/F15, is where the
  read-only-rootfs template gains the writable install + runtime storage a real PZ server needs; a container
  created before F17 has no install bind and must be re-provisioned to gain one (acceptable pre-1.0).
- **A running server is unavailable for the length of an update** (the restart takes it down, the update runs
  on boot). This is inherent to driving the update through the entrypoint without `exec`; the operator sees it
  as a stop→update→start with live progress.
- **F17 is the first real user of the F11 progress pipeline.** Everything downstream of `OperationProgress`
  existed and was unexercised; a bug there surfaces here first.
- **A stalled SteamCMD is bounded twice** — the Agent's own wall-clock timeout, and the operation lease/reaper
  (ADR 0022) if progress stops arriving. The lease is the authority that frees the per-server lock.
- **The build id is observed, not authoritative for "is an update available?"** F17 records what is installed;
  comparing it against Steam's latest to *offer* an update is a later feature.
- **The image now bakes SteamCMD at `/opt/steamcmd` and copies it in on boot** — a small cold-start cost each
  boot, accepted so the read-only rootfs and the ephemeral runtime tmpfs can both hold.
