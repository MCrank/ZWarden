# Feature #229 Mini-Plan — Operator-chosen host ports + Recreate container (preserve data)

**Status:** PR-A (#264, branch `feat/229-recreate-host-ports`) and PR-B (branch `feat/229-recreate-web`, closes
[#229](https://github.com/MCrank/ZWarden/issues/229)). PR-A = Agent + Contracts + proxy/ADR (the Recreate
primitive and port choice, Agent-side); PR-B = Web (permission, Operation kind, endpoints, UI). v1.0 — and the
Recreate primitive is what #230 (memory limit) and #258 (branch change) build on.

**Written against:** issue #229; PRD 2.2 (TDD), PRD 21 (one mutating Operation per Server), PRD 25/27 (Agent
manages only its own containers; restricted socket proxy), PRD 28 (port stride);
[ADR 0008](../adr/0008-docker-socket-access-via-wollomatic-socket-proxy.md) (allowlist — **explicitly denies
`DELETE /containers/{id}`**, amended here), [ADR 0020](../adr/0020-agent-protocol-versioning-and-catalogue.md)
(additive contracts), [ADR 0022](../adr/0022-operation-lifecycle-and-per-server-locking.md) (mutating
Operation + per-server lock + lease), [ADR 0043](../adr/0043-graceful-restart-is-a-best-effort-agent-side-broadcast-before-the-safe-stop.md)
(graceful broadcast before a safe stop), F14 D2 (ServerId, not container id, is the durable key).

## Objective

1. **Choose at provisioning:** an optional host game port on the new-server form (the pair is `p`/`p+1`);
   default = next free stride.
2. **Change later with Recreate:** (graceful broadcast → safe stop, if running) → ownership-guarded remove →
   create from the same closed template with the new host ports → start (if it was running). `/pz/data` and
   `/pz/server` binds derive from the ServerId, so world, config and the installed PZ build survive with no
   re-download. Container-internal ports stay 16261/16262 — the config file never changes (#228).

## Maintainer decisions (2026-09-25, all as recommended)

| # | Decision |
|---|---|
| D1 | **Proxy DELETE is scoped to UUID-shaped container names only**: `-allowDELETE=(/v1\.[0-9]+)?/containers/<uuid regex>`. Canonical containers are named by ServerId, so the Agent always removes **by name**. A foreign container addressed by name (`postgres`) or by 64-hex id still gets 403 — the proxy keeps most of its bug-containment. Agent-side: owned + stopped required, never `force`, never `v=true`. New **ADR 0045** amends ADR 0008. |
| D2 | **New server-scopable permission `Server.Recreate`**, bundled to Owner + Administrator (not Operator, not Moderator). Gates Recreate (ports now, memory #230, branch #258 later). Choosing a port *at provisioning* stays under `Server.Register`. |
| D3 | **Any host game port 1024–65534**; pair = `p`, `p+1`. The allocator becomes interval/overlap-aware; "next free" is still the lowest stride not overlapping any in-use pair. |
| D4 | **Recreate preserves prior run state**: running → graceful warn + safe stop … start; stopped → remove + create, left stopped (a clash with a non-Docker host process then surfaces at the next Start — accepted). |

## Settled design (defaults taken without a fork)

- **No in-process "host bind check".** The Agent runs in its own network namespace, so binding a probe
  socket proves nothing about the host. The checks are: (a) **pre-flight** against every container's
  published host ports on the daemon (`GET /containers/json` already returns them — not just owned ones;
  excluding the server's own container on Recreate); (b) **Docker's start** as the authority
  (`port is already allocated` / `address already in use`), classified by `DockerFailureInterpreter` as a new
  `PortInUse` failure with an actionable message. Web also pre-checks against sibling Servers' recorded ports
  on the same Agent for a fast form error; the Agent stays authoritative.
- **Fix: start failure no longer silently hangs the Operation.** Today a start `DockerApiException` in
  `ProvisionAsync` is only logged and the lease reaper fails it. Provision/Recreate now report `Failed` with the
  cause, and a container that failed to start is removed again so a retry (Recreate, which repairs an absent
  container) or a rollback isn't a name clash.
- **Recreate is also repair.** An absent container is tolerated (skip stop/remove, just create) so a Server left
  containerless by a failed provision or a failed rollback can be recovered with another Recreate.
- **Recreate refuses a mount mismatch (fail-closed).** Before removing, inspect the container; if its
  `/pz/data` / `/pz/server` bind sources don't equal the ServerId-derived sources, refuse — an imported
  container with other mounts would otherwise lose its world.
- **Rollback.** If create/start with the new ports fails, remove the new container, re-create with the
  previous spec (old ports), restore the prior run state, and report `Failed` with the cause. If rollback also
  fails, report that explicitly (Server is containerless → repair via Recreate).
- **Template drift is intentional.** Recreate builds from the *current* closed template, image digest and
  `AgentOptions` memory/heap — so it also picks up an upgraded PZServer image. Documented, not prevented.
- **Graceful broadcast reuses ADR 0043** (`IServerRestartCoordinator.WarnAsync`, operator-overridable/skippable
  schedule payload like Restart).
- **Result** reuses `ProvisionResult(GamePort, QueryPort, ContainerId)`; Web records it via
  `Server.RecordContainer` (also fills ports for imported servers).

## Slices

### PR-A — Agent + Contracts + proxy (ADR 0045)

1. **Port rules + overlap-aware allocator** (Agent `PortStrideAllocator` / Domain `HostPortRules` shared with
   Web): validate 1024–65534, pair overlap, `IsFree(requested, inUse)`, `AllocateNext` skips any stride that
   overlaps an in-use pair (incl. off-stride operator ports).
2. **Remove verb**: `IDockerEngine.RemoveAsync(name)` (DELETE, no force, no v) + `IContainerRuntime.RemoveAsync(ServerId)`
   (owned + not running, removes by ServerId name). Arch test: no `Force`/`RemoveVolumes` true.
3. **Allowlist**: `-allowDELETE` in all four copies (compose, remote-agent compose, AppHost, .localca fix) + drift
   tests (integration: owned uuid-named DELETE passes the proxy; hex-id / non-uuid name DELETE → 403);
   `ComposeDistributionTests`/`RemoteAgentDistributionTests`. ADR 0045 + ADR 0008 amendment note.
4. **Contracts**: `CreateServer` gains optional `GamePort` (additive); new `RecreateServer(int? GamePort, graceful
   schedule)` `provisioning.recreate-server`; message tests + closed-vocabulary tests.
5. **Provision with requested port + failure reporting**: pre-flight against daemon-wide bindings, `PortInUse`
   classification, start failure → `Failed`, cleanup on port clash.
6. **Recreate runner** (`ServerRecreateRunner`): resolve (absent tolerated) → mount check → warn + safe stop if
   running → remove → ensure dirs → create → start if was running → rollback on failure; progress keeps lease.

### PR-B — Web

1. **Domain**: `Server.Recreate` permission + role bundles; `OperationKind.RecreateServer`; audit actions. Built-in
   roles persist their grants (and are editable), so a one-time data migration grants `Server.Recreate` to existing
   Owner/Administrator roles (tested on the SQLite upgrade path).
2. **Infrastructure**: `IServerRecreate.RecreateAsync(serverId, gamePort?, graceful)` (authorize, sibling-port
   pre-check, enqueue mutating, audit, ServerBusy); `RegisterAsync` accepts optional game port + payload.
3. **Web plumbing**: dispatcher mapping (Create payload + Recreate), `AgentHub` records Recreate result,
   `POST /servers/{id}/recreate`, `RegisterServerRequest` optional `GamePort`, live-status label `RECREATING`.
4. **UI** (Bb* components): optional "Game port" on the register form (inventory + setup), "Container" card on
   Overview with a "Change ports" form (port + graceful presets) → Recreate, gated by `Server.Recreate`.
5. **Docs**: ports/firewall + recreate in the deployment guide.

## Out of scope

Memory limit change (#230), branch change (#258) — both reuse the primitive. Changing container-internal ports.
Deleting a Server (`Server.Delete` stays out of v1.0).
