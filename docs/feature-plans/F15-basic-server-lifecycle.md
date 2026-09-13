# Feature 15 Mini-Plan — Basic Server Lifecycle

**Status:** IMPLEMENTED (PR pending). Roadmap issue: [F15 (#36)](https://github.com/MCrank/ZWarden/issues/36). Track D — the first feature that *runs* a Server; **depends on F11 (operations engine) and F14 (Server inventory + provisioning), both merged**. Unblocks F16 (health), F20b (config apply).

**As-built correction (flagged for review), an honest consequence of the authorization mechanism — the same root cause as F14 D3:**

- **The lifecycle endpoints (`POST /api/servers/{id}/{start|stop|restart}`) are gated by `RequireAuthorization()` (authenticated), not by the server-scoped `Server.Start/Stop/Restart` *policy* at the edge.** A server-scoped policy checked with no resource **denies outright** (`PermissionChecker`, ADR 0018), so a `RequireAuthorization("Server.Start")` on the endpoint would refuse every caller. The real, fail-closed server-scoped check happens in `ServerLifecycle` with the concrete `ServerId` as the resource (proven at the service tier), and the per-row UI buttons render only where the caller actually holds the permission. This mirrors F14's inventory (D3/D6).

**Format:** PRD 60. **Written against:** [`scope-and-sequencing.md`](../scope-and-sequencing.md) §6 (F15), ADR [0022](../adr/0022-operation-lifecycle-and-per-server-locking.md) (operation lifecycle + per-server lock), ADR [0008](../adr/0008-docker-socket-access-via-wollomatic-socket-proxy.md) (the ten-entry allowlist — the constraint that shapes the stop path), ADR [0018](../adr/0018-zwarden-owned-rbac.md) (server-scoped authorization), ADR [0019](../adr/0019-audit-is-append-only-tenant-owned-and-binds-the-auth-sink.md) (audit), ADR [0003](../adr/0003-blazor-blueprint-ui-library.md) (Blueprint seam + static-SSR inventory), the F12 image contract ([`src/ZWarden.PZServer`](../../src/ZWarden.PZServer) — the entrypoint's SIGTERM→FIFO `save`→`quit` handler), the F13 runtime ([`F13-agent-docker-runtime.md`](F13-agent-docker-runtime.md) — lifecycle methods + ownership enforcement), the F14 hand-off ([`F14-server-registration-and-inventory.md`](F14-server-registration-and-inventory.md) — the authorize→resolve-agent→enqueue pattern), and [`trust-boundaries.md`](../trust-boundaries.md) §3 (observed-never-inferred) / §4 (ownership) / §8 (untrusted Agent output).

## Objective

Give operators **start / stop / restart** for a registered Server, as durable, authorized, audited Operations, with lifecycle progress visible in the UI and a **safe stop that never corrupts the world**. F15 turns F13's container lifecycle *methods* and F14's provisioning wiring into three operator-facing commands.

The load-bearing property is the **stop path**: Project Zomboid must be shut down with the console `save` then `quit` on its stdin FIFO — never a bare SIGTERM/SIGKILL to the JVM, which the PZ developers explicitly discourage and which truncates the world save.

## The stop path — the crux (settled with the maintainer)

Two facts pin down the only correct realization:

1. **The F12 image already implements the blessed shutdown.** `entrypoint.sh` runs under tini as PID 1, opens the FIFO `/pz/runtime/zomboid.control` read-write so stdin never sees EOF, and traps `SIGTERM` → `pz_graceful_stop`: write `save`, sleep `ZW_PZ_STOP_GRACE` (default **30s**), write `quit`, then `wait` for the JVM to exit. **SIGTERM is never forwarded as a bare signal to the JVM.**
2. **The wollomatic ten-entry allowlist (ADR 0008) denies `/containers/{id}/exec` and `/attach`.** The Agent therefore *cannot* write the FIFO directly. It *can* call the allowlisted `POST /containers/{id}/stop` and `/restart`.

**Decision (D-STOP, maintainer-confirmed):** F15's stop is **`docker stop` with an explicit `t` timeout** (`WaitBeforeKillSeconds`). Docker sends SIGTERM to PID 1; the container converts it to the FIFO `save`→`quit`; Docker only SIGKILLs if the process is still alive after `t`. The issue's *"stdin FIFO … not SIGTERM"* is satisfied **inside the container** — "not SIGTERM" means "not a bare SIGTERM to the game process." An in-container control endpoint (the alternative) was rejected: it adds a listener, port and auth surface inside the container and reopens F12/F13 scope, for no gain over the trap that already exists.

**Decision (D-TIMEOUT, maintainer-confirmed):** the stop timeout is a new **`AgentOptions.StopTimeoutSeconds`**, default **120** — comfortably above the 30s in-container grace plus save margin, and raisable by operators with large worlds. Restart uses the same value. **This fixes a latent bug:** `DockerDotNetEngine.StopAsync` currently sends `new ContainerStopParameters()` with *no* timeout → Docker's default 10s < the 30s grace → a SIGKILL mid-save. F15 makes the timeout explicit and safe.

## Decisions (settled from precedent)

- **D1 — three Operation kinds, three commands.** `OperationKind.{StartServer, StopServer, RestartServer}` (stored by name, ADR 0022) and three payload-free `AgentCommand`s `Lifecycle.{StartServer, StopServer, RestartServer}` (ServerId rides the envelope, exactly like `Provisioning.CreateServer`). Each is **mutating and server-scoped**, so each claims the per-server lock. Mirrors the one-command-per-verb shape already in the codebase and the three existing `Server.{Start,Stop,Restart}` permissions.
- **D2 — restart is one `docker restart?t=N` verb, not stop-then-start.** It is allowlisted, atomic from the operator's view, and carries the same safe timeout (its stop half triggers the same FIFO shutdown). Modelling it as two sequenced Operations buys nothing and doubles the lock churn.
- **D3 — no lease heartbeat needed.** The operation lease is 5 minutes; a stop is ≤ ~35s. The Agent runs the verb and reports `OperationCompleted`; it does **not** emit `OperationProgress` to hold the lock. "Lifecycle progress" is the existing `GET /api/operations/{id}` state projection (Pending→Running→Succeeded/Failed + timestamps). A "saving…" progress line is a nice-to-have deferred to F16.
- **D4 — lifecycle Operations are not cancellable in the F15 UI.** They are short-lived, and cancelling mid-stop risks a truncated save. The coordinator's cancellation machinery (ADR 0022) still exists; F15 simply does not surface a cancel button. The reaper still fails a stalled Operation and frees the lock.
- **D5 — the Agent resolves the container by ServerId.** The command carries only the ServerId (envelope). The Agent resolves the owned container via `IContainerRuntime` discovery (the create-template names the container after the ServerId and stamps `io.zwarden.server-id`), then runs the guarded verb. A Server with **no** container (registered, not yet provisioned, or container removed) fails the Operation with an actionable, Agent-authored reason — never a crash.
- **D6 — the UI adds per-row lifecycle actions to the existing static `/servers` page.** Consistent with F14 D6 (static SSR, live `HttpContext` for the tenant), each row gets Start/Stop/Restart form posts (antiforgery via the Lax cookie), each gated by a **per-server** `Server.{Start,Stop,Restart}` check computed per row and **re-checked server-side** in the service. Buttons are disabled as a *hint* from the last-reported state, never as the authorization. A dedicated server-detail page and live progress push are F16/F36.
- **D7 — do not gate transitions on stale observed state.** Run-state is observed and can be stale (trust §3). `docker start` on a running container and `docker stop` on a stopped one are effectively no-ops (304) at the daemon; the Operation reports the real outcome. The last-reported state only *hints* the UI; the Agent is authoritative.

## Scope

1. **Domain — `OperationKind.{StartServer, StopServer, RestartServer}`** appended (by name; never renumbered).
2. **Contracts — three commands** `Lifecycle.StartServer` / `StopServer` / `RestartServer` : `AgentCommand`, sealed records, `ProtocolMessageAttribute` (`lifecycle.start-server`, etc.), payload-free, mutating, server-scoped. Update `ClosedCommandVocabularyTests`.
3. **Web operations dispatch** — map the three kinds → the three commands in `ZWarden.Web/Operations/OperationDispatcher`.
4. **Application — `IServerLifecycle`** (+ result types) : `StartAsync/StopAsync/RestartAsync(UserId, ServerId)` — authorize the matching **server-scoped** permission (`IPermissionChecker.EvaluateAsync(user, perm, server: serverId)`), resolve the Server (tenant-filtered) and its `AgentId`, write an audit event, and `IOperationCoordinator.EnqueueAsync` the mutating server-scoped Operation. Returns the Operation id or a typed failure (`NotAuthorized` / `ServerNotFound` / `ServerBusy` — the last mapped from `ServerBusyException`).
5. **Infrastructure — `ServerLifecycle`** implementing it (mirror `ServerInventory.RegisterAsync`), + `ServerAuditActions.{Started, Stopped, Restarted}`.
6. **Agent — lifecycle command handling.** `AgentCommandProcessor` dispatches the three commands, dedupes by `OperationId`, resolves the container by ServerId, calls the runtime, reports `OperationCompleted` (Failed with a reason when no container or a Docker refusal). `IContainerRuntime` gains ServerId-addressed `Start/Stop/Restart` (resolve owned container, then the existing guarded verb). `IDockerEngine.StopAsync`/`RestartAsync` gain a timeout; `DockerDotNetEngine` passes `WaitBeforeKillSeconds`/`RestartContainerParameters.WaitBeforeKillSeconds` from `AgentOptions.StopTimeoutSeconds`.
7. **Agent config — `AgentOptions.StopTimeoutSeconds`** (default 120; validated > 0).
8. **Web — endpoints** `POST /api/servers/{id}/start|stop|restart` (mirror `MapServerEndpoints`), each `RequireAuthorization` on the matching policy and re-checked in the service; return `202 Accepted` with the operation id, or the typed failure status.
9. **Web — UI** per-row Start/Stop/Restart on `/servers` (static SSR), per-server-gated, disabled-as-hint by last-reported state, redirect-after-post; a short status line pointing at the enqueued Operation. `npm run build:css`, commit `wwwroot/app.css` (ADR 0003 condition 2) if any new literal class is introduced.

## Non-scope

- **Health beyond last-reported state** (F16) — probes, OpenTelemetry, live 1 Hz push, the virtualized `BbDataGrid`, a server-detail page.
- **Update / SteamCMD lifecycle** (F17), **config apply** (F20b), **RCON / players** (F18/F19), **backups** (F24), **bulk operations** (post-1.1).
- **De-provision / delete container** — a registry/destroy concern, not lifecycle.
- **A cancel button** (D4) and **`OperationProgress` "saving…" lines** (D3) — deferred.
- **Widening the allowlist** — F15 stays within the ten entries; no exec/attach.

## Domain / contract changes

- **Domain:** three new `OperationKind` values. No new entity; `Server` already carries `AgentId`, `DockerContainerId`, `LastRunState`.
- **Contracts:** three new commands; envelope + lifecycle unchanged (ADR 0020). Catalogue/vocabulary tests updated.
- **No new permission** — `Server.Start/Stop/Restart` already exist (server-scoped) in the closed catalogue.
- **No migration** — no schema change (Operations table already stores the kind by name).
- **No new ADR** — realized entirely under ADR 0022 (lifecycle/lock), ADR 0008 (allowlist → the stop path), ADR 0018 (authorization). The stop-path realization is recorded here, in the mini-plan, as ADR 0008/0022 intended.

## Security considerations

- **Authorization is server-scoped and fail-closed** (ADR 0018): each verb checks its own `Server.{Start,Stop,Restart}` against the specific ServerId, in the service (the nav/button gate is convenience only). Tenant isolation: the Server is resolved through the tenant filter, so a foreign Server is `ServerNotFound`.
- **Ownership at the Agent** (trust §4): the Agent runs the verb only after F13's ownership guard confirms `io.zwarden.agent-id == self`; Web never targets a container directly.
- **Untrusted Agent output** (trust §8): failure reasons / status lines are data, escaped at render.
- **Safe stop is a correctness requirement, not hardening** (D-TIMEOUT): too-short a timeout corrupts the save. The explicit `StopTimeoutSeconds` > grace is the control.
- **No secrets** on the wire or the record; **antiforgery** on every mutation; **idempotency** by the enqueue key (PRD 20) and by `OperationId` dedupe on the Agent.

## Test plan (TDD — tests first, PRD 2.2; tiers per ADR 0002)

**Offline unit / domain / contracts:**
1. **Command vocabulary** — the three `Lifecycle.*` commands are in the closed vocabulary, mutating, round-trip through `ProtocolJson`; `ClosedCommandVocabularyTests` updated.
2. **Dispatch mapping** — each of the three `OperationKind`s maps to the right command in `OperationDispatcher` (and the provisioning/diagnostic maps are unregressed).
3. **`IServerLifecycle` authorization (fail-closed)** — a server-scoped grant on server X allows Start on X and denies on Y; no grant denies; each verb checks its own permission; a foreign-tenant / missing Server is `ServerNotFound`; enqueues the correct mutating, server-scoped `OperationKind` against the Server's `AgentId`; audits.
4. **Server-busy** — a second lifecycle verb while one is in flight surfaces `ServerBusy` (from `ServerBusyException`, the per-server lock, ADR 0022).
5. **Agent handler** — each `Lifecycle.*` command resolves the container by ServerId and calls the matching runtime verb; dedupe by `OperationId`; a Server with no owned container → Failed with an actionable reason; a `ForeignContainerException` / denied verb → Failed, not a crash; unknown-command behaviour unregressed.
6. **Safe stop timeout** — the runtime/engine issues stop and restart with `WaitBeforeKillSeconds == StopTimeoutSeconds`; `AgentOptions` validates it > 0 and defaults 120.

**Web tier (`WebApplicationFactory<Program>` + bUnit):**
7. **Endpoints** — `POST /api/servers/{id}/start|stop|restart` require the matching policy, are antiforgery-protected, return 202 + operation id on success, and the typed statuses on failure; an invalid id → 400.
8. **Inventory page** — per-row Start/Stop/Restart render only for a caller with the per-server permission; a POST enqueues the Operation and redirects; buttons reflect last-reported state as a hint.

**Architecture tier:**
9. **Reference direction** unregressed (Web maps kind→command; Agent references the Docker client; Domain references neither). No new allowlist verb reachable (the engine still has no exec/attach/kill path — F13's guard test stands).

## Implementation slices (each independently verifiable, one context)

- **S1 — Domain + Contracts + dispatch.** Three `OperationKind`s, three `Lifecycle.*` commands, the dispatcher map. *Verify:* T1, T2, T9.
- **S2 — Application + Infrastructure service.** `IServerLifecycle`/`ServerLifecycle`, result types, audit actions, DI. *Verify:* T3, T4.
- **S3 — Agent handler + safe stop.** `AgentCommandProcessor` cases, ServerId-addressed runtime lifecycle, `IDockerEngine`/`DockerDotNetEngine` timeout, `AgentOptions.StopTimeoutSeconds`. *Verify:* T5, T6.
- **S4 — Web endpoints + UI.** `/api/servers/{id}/{verb}`, per-row actions on `/servers`, `app.css` if needed. *Verify:* T7, T8.

## Acceptance criteria

1. `Lifecycle.{StartServer,StopServer,RestartServer}` are in the closed vocabulary (mutating, server-scoped) and dispatch end-to-end (Web → Agent → `OperationCompleted`) with F11's dedupe; unknown-command behaviour unregressed.
2. Starting / stopping / restarting a Server enqueues the matching mutating, server-scoped Operation (per-server lock, ADR 0022), authorized by the matching `Server.*` permission fail-closed, audited, antiforgery-protected.
3. **Stop is safe:** the Agent issues `docker stop` with `WaitBeforeKillSeconds = StopTimeoutSeconds` (default 120 > the 30s in-container grace), so the container's FIFO `save`→`quit` completes before any SIGKILL; the current no-timeout default is removed. Restart carries the same timeout.
4. A Server with no owned container, a foreign container, or a denied verb fails the Operation with an actionable, escaped, Agent-authored reason — never a crash.
5. The `/servers` dashboard offers per-row Start/Stop/Restart, per-server-authorized and re-checked server-side, with the Operation state visible via the existing operations endpoint; CI's `app.css` diff is green.

## Definition of Done

Per PRD 61, the applicable subset: acceptance criteria met; tests authored first; offline + architecture tiers green in CI and the Web tier authored; server-scoped authorization carries auth-grade tests; **safe-stop timeout proven by test**; no secrets; diagnostics exist (no-container / foreign / denied-verb / server-busy); the stop path is correct with the socket unproxied and within the ten-entry allowlist (no exec/attach); documentation updated (this plan; the Agent README's stop-path note; PZServer README cross-reference); threat considerations reviewed against trust-boundaries §3/§4/§8 and ADR 0008/0018/0022. (No migration, no new domain entity, no new permission.)
