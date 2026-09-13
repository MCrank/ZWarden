# Feature 17 Mini-Plan — SteamCMD Lifecycle

**Status:** ready for implementation. Roadmap issue: [F17 (#38)](https://github.com/MCrank/ZWarden/issues/38). Track D — the feature that turns a provisioned Server's Project Zomboid install from a *one-shot first-run* thing (F12) into a **managed, updatable** thing. **Depends on F16 (health & observability), merged.** Unblocks [F21 (#43)](https://github.com/MCrank/ZWarden/issues/43) — Workshop & mod discovery.

**Format:** PRD 60. **TDD is mandatory** (PRD 2.2). **Written against:** [`scope-and-sequencing.md`](../scope-and-sequencing.md) §6 (F17), ADR [0009](../adr/0009-steamcmd-at-runtime-is-mandatory.md) (SteamCMD-at-runtime, stdout-parsing, undocumented exit codes), ADR [0008](../adr/0008-docker-socket-access-via-wollomatic-socket-proxy.md) (the allowlist — **`exec`/`attach` are denied**, the design pivot below), ADR [0022](../adr/0022-operation-lifecycle-and-per-server-locking.md) (operation lifecycle + per-server lock + lease/reaper — the progress path F17 is the **first** to exercise), ADR [0020](../adr/0020-agent-protocol-versioning-and-catalogue.md) (additive-vs-breaking), ADR [0019](../adr/0019-audit-is-append-only-tenant-owned-and-binds-the-auth-sink.md) (audit), ADR [0018](../adr/0018-zwarden-owned-rbac.md) (fail-closed server-scoped authorization), the PZ runtime research ([`research/project-zomboid-runtime.md`](../research/project-zomboid-runtime.md) §4–5, verified against **Build 42.20.4**), and the F12/F13/F15/F16 plans. New ADR **0025** lands with the feature (below). The four load-bearing decisions were settled with the maintainer before writing.

## Objective

Make **updating** a Server's PZ install a first-class, durable, authorized, audited, **progress-reporting** Operation. SteamCMD `app_update 380870 validate` (install, update and validate are the *same* verb — `pz-lib.sh:38-52`) becomes a mutating, server-scoped Operation whose success/failure is decided by **parsing stdout** (ADR 0009 — Valve's exit codes are undocumented), whose live percentage the operator watches through the **already-built but never-used** progress pipeline, and which reads back the **installed build id** on completion. This is also the feature that makes the ~6.72 GiB install **persist** across container recreations so "update" is incremental, not a full reinstall.

## The four load-bearing decisions (settled with the maintainer before writing)

- **D-STORAGE — the install must become writable and persistent; via host bind mounts, not Docker volumes.** SteamCMD installs into `/pz/server` (+ the Steam root under `/pz/runtime`), but F13's closed create template (`PzContainerFactory`) mounts **only** `/pz/data` and sets `ReadonlyRootfs = true` (invariant 8) — so today a runtime `app_update` can neither persist nor run. Decision: **add two persistent host bind mounts** — `/pz/server` (the 380870 install) and the Steam root under `/pz/runtime` — to the create template, **keeping the read-only rootfs** (only the three named mounts are writable). This preserves F12's deliberate install-vs-world separation ("settings and the world would not be corrupted in case of an update" — research §5) and matches ADR 0009's Steam-root Workshop layout. Bind mounts, **not** `docker volume create` — `POST /volumes/create` is not allowlisted (ADR 0008); the Agent already owns `DataMountRoot` and provisions host paths there. **Consequence:** this reopens the closed §5.3 create template — new spec fields, two new structural invariants, and the create-template / ownership / wollomatic-drift tests all extend. Servers must be **(re)provisioned** to gain the mounts (acceptable pre-1.0; a container created before F17 has no install volume). **No allowlist change** — the create *body* is not socket-governed, and no new verb is used.
- **D-TRIGGER — no `exec`, so the Agent drives the in-container entrypoint by a control-file + restart, and observes via `logs`.** `exec`/`attach` are denied (ADR 0008), so the Agent cannot run SteamCMD itself; execution stays in the container entrypoint (F12). To force an update on an already-installed container (whose entrypoint currently *skips* SteamCMD when the `.zwarden-installed` marker is present — `entrypoint.sh:39-41`), the Agent: (1) writes a control-file `/pz/data/.zwarden-update-requested` **containing the OperationId** into the already-writable data bind mount (host-side — the Agent owns `DataMountRoot/<serverId>`); (2) issues the allowlisted **restart** verb (its safe stop timeout runs the FIFO `save`→`quit`, then the container reboots); (3) the F12 entrypoint sees the control-file, prints a `steamcmd update session <OperationId> begin` banner, runs `app_update 380870 [-beta] validate` **regardless of the install marker**, prints `… end`, deletes the control-file, then launches the server; (4) the Agent parses the SteamCMD lines from `GET /containers/{id}/logs` **bounded by the session banner** to emit progress and decide the terminal outcome. **Update failure does not brick a working server** (unlike F12's first-*install* fail-closed exit): the entrypoint records the failure, clears the control-file to avoid a restart loop, and **launches the server on the existing (old) install** — the Operation is `Failed`, the Server is `Running` on the prior build, honestly reported.
- **D-SURFACE — one combined `UpdateServer` Operation; repair is a re-run.** Install/update/validate are one SteamCMD verb, so F17 ships **one** `OperationKind.UpdateServer` (mutating, server-scoped), one command, one permission. A validate/repair is the same Operation run again (`validate` is always in the verb). Smallest catalogue/dispatch/permission surface; no distinct Install/Validate kinds.
- **D-PROGRESS + D-VERSION — F17 is the first real progress emitter, and version comes from the manifest host-side.** The whole progress path exists and **no Operation uses it** (`OperationProgress` → `AgentHub.OperationProgress` → `OperationStore.ApplyProgressAsync` → `/api/operations/{id}` exposes `percentComplete`/`statusLine`). F17 adds the missing **emitter**: the Agent polls `logs` on an interval, parses SteamCMD's `Update state (0x…) …, progress: NN.NN (bytes / total)` lines into `OperationProgress` (each ingest also **extends the lease**, keeping a multi-minute install alive under the reaper). Poll, not `follow`, to sidestep the streaming-logs allowlist question (an open confirmation below). **Version detection** reads `steamapps/appmanifest_380870.acf`'s `buildid` **host-side** from the now-persistent Steam-root bind mount (the F16 host-side-disk pattern), reported on completion and persisted on the Server.

## Scope, by PR

### PR-A — Image update path (`ZWarden.PZServer`, container-side; branch `feat/f17-image-update-path`)
1. **Entrypoint (`entrypoint.sh` / `pz-lib.sh`):** an update-control-file check that runs `app_update … validate` **despite** the `.zwarden-installed` marker; a `steamcmd update session <id> begin/end` banner (id read from the control-file) bounding the log window; control-file cleared on both success and failure; **update-failure fallback** — log, clear, launch the server on the existing install (distinct from first-install fail-closed `exit 1`). Ensure SteamCMD progress lines reach container stdout so `docker logs` sees them (already tee'd — `pz-lib.sh:85`).
2. **Layout:** `/pz/server` and the Steam root are now persistent mounts, not ephemeral rootfs paths; update the README `/pz` map, the by-hand `docker run` (three mounts now), and env docs.
3. **`bats` tests (offline tier, stub `steamcmd`):** control-file triggers update past the marker; session banner brackets the SteamCMD output; control-file cleared on success **and** on failure; update-failure launches the server on the old install (no `exit 1`); first-run install unchanged (regression). Bump nothing else.

### PR-B — Create-template persistence + Agent orchestration + contracts (branch `feat/f17-agent-update-orchestration`)
1. **Create template (F13 reopened):** `PzContainerSpec` gains `ServerMountSource` + `SteamRootMountSource` (absolute host paths); `PzContainerFactory.Build` adds two `Type="bind"`, `ReadOnly=false` mounts at `/pz/server` and the Steam root, **rootfs stays read-only**; `Validate` requires them absolute. Two new §5.3 invariants documented in the class; extend the create-template, ownership, and **wollomatic allowlist drift** tests. `ProvisionAsync` (`AgentCommandProcessor`) computes the two sources under `_options.DataMountRoot/<serverId>/{server,runtime}`.
2. **Contracts (all additive — ADR 0020, `ProtocolVersion.Current` stays 1):** `UpdateServer : AgentCommand` `[ProtocolMessage("lifecycle.update-server")]` (auto-registered by the assembly scan; copy `StartServer`); an optional `UpdateResult(string? InstalledBuildId, …)` added to `OperationCompleted` (nullable ⇒ additive, mirrors `ProvisionResult`); assert additivity + closed-vocabulary in `ClosedCommandVocabularyTests`/`ProtocolCompatibilityTests`.
3. **Agent:** `OperationKind.UpdateServer = 6` (append; stored by name); `AgentCommandProcessor` `case UpdateServer` — dedupe by OperationId, write the control-file, restart, **poll `logs` and emit `OperationProgress`** via a new injected `IOperationProgressReporter` seam (keeps the processor transport-free; the real impl is `SignalRControlPlaneConnection.SendOperationProgressAsync` over `AgentHubProtocol.OperationProgress`, a sibling of the existing `SendServerStateChangedAsync`), parse the terminal `Success!`/`Error!` line → `OperationCompleted` with `UpdateResult`; a per-invocation **timeout** and no-output stall handled (the lease reaper is the ultimate net). A `SteamCmdLogParser` (pure, unit-tested): progress lines → percent+status, terminal lines → outcome, session-banner bounding.
4. **Web ingest + persistence:** `Server.InstalledBuildId` (`string?`) + `InstalledBuildReportedAt` + `RecordObservedBuild(...)` (Domain); EF mapping + `AddServerInstalledBuild` migration (**both providers**); the `OperationCompleted` ingest path persists `UpdateResult` through the reconciler.
5. **ADR 0025** — SteamCMD update orchestration under a no-`exec` allowlist: the control-file + restart + log-parse loop, the install-persistence bind mounts (reopening §5.3), and the stdout-parsed progress/outcome/version contract.

### PR-C — Enqueue seam + permission + endpoint/UI (closes #38; branch `feat/f17-update-enqueue-and-ui`)
1. **Application service `IServerUpdate`/`ServerUpdate`** — a near-verbatim copy of `ServerLifecycle`: resolve Server through the tenant filter → authorize the new server-scoped permission → `EnqueueAsync(new EnqueueOperationRequest(server.AgentId, OperationKind.UpdateServer, IsMutating: true, Guid.NewGuid().ToString("N"), ServerId: serverId), …)` → audit → `catch (ServerBusyException)` ⇒ `ServerBusy`.
2. **Web dispatch:** a `case OperationKind.UpdateServer` in `OperationDispatcher.CommandFor` → `UpdateServer` command (covered by `OperationDispatcherMapTests`).
3. **Authorization:** `Permissions.ServerUpdate` (server-scopable; `Server.*` pattern) added to the closed catalogue + `All`; `BuiltInRoles` grants; extend `PermissionCatalogueTests`.
4. **Audit:** automatic `Operation.*` via the engine; add `ServerAuditActions.Updated` for the intent record (mirrors `Started`).
5. **Endpoint/UI:** an enqueue endpoint (copy `OperationEndpoints`) + an **Update** action on the server-detail view showing live `percentComplete`/`statusLine` off the existing operation surface; the installed build id surfaced beside health. Per-PR chore: if any Web.Tests count changes, **bump the floor in BOTH the csproj and `ci.yml` `tier1-silent-drop-guard`** ([[web-tests-discovery-floor-bump]]); `npm run build:css` + commit `wwwroot/app.css` if styles change.

## Non-scope

- **Workshop / mod content** (F21) — `workshop_download_item 108600 …` and `WorkshopItems=` are F21; F17 updates the base server only.
- **`-Dsoftreset`** — broken as of 42.20.4 (ADR 0009); never used.
- **Branch switching as a managed operation** — `ZW_PZ_BETA` (`public`/`legacy41`/`42.19`) is set at provision (F14); F17 updates within the configured branch. A managed branch-change is later.
- **Scheduled / automatic updates**, update *notifications*, and "update available?" polling of Steam — F17 is operator-initiated; a build-id-drift check is a later feature.
- **Streaming (`follow`) log ingestion / live logs** (F27) — F17 polls `logs` for the update window only.
- **Windows / native (non-container) install** — Linux container only in v1.0.
- **Rollback to a prior build** — SteamCMD has no clean pinned-rollback within a branch; out of scope.

## Domain / contract / persistence changes

- **Domain:** `OperationKind.UpdateServer` (append); `Server.InstalledBuildId`/`InstalledBuildReportedAt` + `RecordObservedBuild`.
- **Contracts (additive, no version bump):** `UpdateServer` command; optional `UpdateResult` on `OperationCompleted`. Assert `ProtocolVersion.Current == 1` holds.
- **Persistence:** one `AddServerInstalledBuild` migration per provider. The Operations table itself is unchanged (`UpdateServer` is a new enum value in the existing string column — no operations migration).
- **Authorization:** `Permissions.ServerUpdate` (closed catalogue).

## Test plan (TDD, per PR)

- **PR-A (`bats`, offline, stub `steamcmd`):** control-file triggers update past the marker; banner brackets output; control-file cleared on success and failure; update-failure launches on the old install (no `exit 1`); first-run install regression green.
- **PR-B:** `SteamCmdLogParser` (every state/progress/terminal/banner-bounding case, incl. `Error! … state 0x…` ⇒ Failed whatever the code, and a first-install ~6.72 GiB denominator vs a small incremental one); `PzContainerFactory` two-new-mounts + RO-rootfs-intact + absolute-path validation + §5.3 invariant tests; wollomatic allowlist drift test (unchanged allowlist asserted); `AgentCommandProcessor` update case with a fake `IOperationProgressReporter` (progress emitted, dedupe on redelivery, timeout→Failed); `Server` build-mutator tests; reconciler persists `UpdateResult`; Contracts serialization + additivity.
- **PR-C:** `ServerUpdate` (not-found/foreign ⇒ ServerNotFound, unauthorized, ServerBusy, success audits); `OperationDispatcherMapTests` covers the new kind; `PermissionCatalogueTests`; enqueue-endpoint + server-detail live-progress render tests (bUnit, loose JSInterop).

## Diagnostics

- **Why an update failed is legible, not an opaque exit code:** the `Error! App '380870' state is 0x…` line is surfaced as the Operation `FailureReason` (ADR 0009), and the full SteamCMD stdout is in `docker logs` for the update session.
- **Progress is observable live** — the first Operation to light up the `percentComplete`/`statusLine` surface end-to-end.
- **A stalled SteamCMD** (no log output) stops extending the lease and the `OperationReaper` fails it with `"lease expired …"` — the per-server lock frees without operator action.
- **Installed build id** after a successful update is reported and persisted, so the fleet can answer "what build is this Server on?".

## Documentation

- `src/ZWarden.PZServer/README.md` — the update-control-file contract, the three-mount layout, the by-hand update run.
- **ADR 0025** as above; note in ADR 0008's consequences that F17 adds bind mounts to the create body (no new *verb*, no allowlist change) and relies on `logs` for update observation.

## Acceptance criteria

1. A `UpdateServer` Operation, authorized by `Permissions.ServerUpdate` and serialized by the per-server lock, runs `app_update 380870 validate` **in the container** (no `exec`), driven by the control-file + restart, observed via `logs`.
2. Success/failure is decided by **stdout parsing**, never the exit code; the failing SteamCMD line is the Operation's reason.
3. The Operation emits **live progress** (`percentComplete`/`statusLine`), extending its lease; a stall is reaped.
4. The install **persists** across container recreation (separate host bind mounts; read-only rootfs intact; §5.3 invariants + drift test still green).
5. A failed update **does not brick** a working Server — it runs on the prior build, Operation `Failed`, Server `Running`, honestly reported.
6. The installed **build id** is detected host-side from the manifest, reported and persisted.
7. All protocol/persistence changes are **additive** (`ProtocolVersion.Current == 1`); migrations apply on both providers.

## Definition of Done

Per PRD 61: acceptance criteria met; tests authored first (`bats` offline + .NET unit/integration); **no secrets** (anonymous SteamCMD, no Steam account — ADR 0009); fail-closed authorization + tenant isolation; audit on enqueue/terminal; diagnostics (stdout-parsed outcome, live progress, reaper safety net, version report) exist; failure modes modelled (update error, stall, foreign/absent Server, server-busy); migrations on both providers; ADR 0025 written; docs updated; CI green (offline `bats` + tier-1 + the opt-in real-install tier exercising a genuine update); no committed PZ artefact (ADR 0009); trust-boundaries §5/§8 reviewed (all SteamCMD/PZ output is untrusted).
