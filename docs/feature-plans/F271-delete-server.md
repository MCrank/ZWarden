# Feature #271 Mini-Plan — Delete a server (container + fleet row), behind a typed-name confirmation

**Status:** PR-A (branch `feat/271-delete-server`) = Agent + Contracts (the `DeleteServer` command and the Agent
handler); PR-B (branch `feat/271-delete-server-web`, closes [#271](https://github.com/MCrank/ZWarden/issues/271))
= Web (permission + grant migration, Operation kind, service, endpoint, completion-time row removal, confirmation
dialog, docs). v1.0.

**Written against:** issue #271; PRD 21 (one mutating Operation per Server), PRD 25/27 (Agent manages only its
own containers; restricted socket proxy);
[ADR 0018](../adr/0018-zwarden-owned-rbac.md) (server-scoped permissions, fail closed),
[ADR 0022](../adr/0022-operation-lifecycle-and-per-server-locking.md) (mutating Operation + per-server lock),
[ADR 0043](../adr/0043-graceful-restart-is-a-best-effort-agent-side-broadcast-before-the-safe-stop.md)
(graceful broadcast before a safe stop),
[ADR 0045](../adr/0045-recreate-removes-a-stopped-container-by-its-canonical-name-only.md) (remove by canonical
name only, never `force`/`v`), F14 D2 (ServerId is the durable key), #229 (Recreate primitive this reuses).

## Objective

An Owner/Administrator deletes a server from Server Detail: (graceful warn → safe stop, if running) →
ownership-guarded remove of `srv-<uuid>` → the Server row leaves the fleet. World data and backups stay on the
host. Nothing runs until a typed-name confirmation, re-checked on the server.

## Maintainer decisions (2026-09-27, all as recommended)

| # | Decision |
|---|---|
| D1 | **Hard delete + tidy.** On a successful completion the Server row and its server-scoped `RoleAssignments` are removed. Operations and Audit rows stay as history (no FKs exist, so nothing cascades); pages that render them must tolerate a missing Server name. Backup / ConfigRevision / BanRecord rows are left (invisible). No soft-delete pattern. |
| D2 | **World data + backups are always kept** (`<DataMountRoot>/<id>`, `<id>.server`, backup root). The dialog shows the path and says recovery is manual — re-import needs a discovered container, so a kept dir is not re-importable in-app. "Also delete world data" → follow-up issue. |
| D3 | **Confirmation = native `<dialog>` + a few lines of JS** wrapping a static-SSR form; type-the-server-name enables Delete; the posted `ConfirmName` is re-checked by the service (ordinal, exact). Pattern recorded in `docs/agents/ui-components.md` for later destructive actions. |
| D4 | **Agent offline:** `DeleteServer` waits/fails like any Operation. Owner-only "remove from ZWarden only" (forget) → follow-up issue. |

## Settled design (defaults taken without a fork)

- **Permission `Server.Delete`** (server-scopable), in the catalogue → Owner (All) + Administrator (All minus
  tenant/agent) automatically; Operator/Moderator/Viewer don't get it. Data-only grant migration
  `GrantServerDeleteToBuiltInRoles` for both providers (same SQL shape as `GrantServerRecreateToBuiltInRoles`).
- **Operation kind `DeleteServer = 24`**, mutating, server-scoped → per-server lock refuses it while busy.
- **Contract** `DeleteServer(GracefulRestartPlan? Plan) : AgentCommand` (additive, ADR 0020).
- **Agent `ServerDeleter`:** resolve owned container → **absent = success** (idempotent; a Server left
  containerless by a failed provision can still be deleted) → if running: `WarnAsync(plan)` then
  `StopAsync` (the F15 safe stop, timeout > save grace) → `RemoveAsync(ServerId)` (ADR 0045: owned, not running,
  by name, no force/v). A stop or remove failure reports `Failed` with the cause and the row stays.
  Redelivery guard (`_handled.TryAdd`) like Recreate. No data dir is touched.
- **No proxy allowlist change**: stop is `POST …/stop`, remove is the existing uuid-scoped `-allowDELETE`. Fix the
  `.localca/compose.fix.yaml` copy only if it's committed (it's untracked local — leave it) and correct the stale
  `docs/research/docker-socket-proxy.md` note.
- **Web service** `IServerLifecycle.DeleteAsync(serverId, confirmName, plan)` through the shared `RunAsync`
  (tenant lookup → `Server.Delete` check → precheck → enqueue → audit). Precheck: `confirmName` ≠ `server.Name`
  → new `ConfirmationMismatch` result, nothing enqueued. Delete also audits its refusals (NotAuthorized and
  ConfirmationMismatch → `Server.Deleted` with outcome Denied), opt-in per verb (`auditRefusals`), so the other
  lifecycle verbs keep their behaviour. Audit `Server.Deleted` at
  enqueue (the existing lifecycle pattern), detail carries the server's name + operation id so it reads after the
  row is gone.
- **Row removal on completion:** `AgentHub.OperationCompleted` → for a *Succeeded* `DeleteServer` operation,
  `IServerRemoval.RemoveAsync(serverId, agentId)` deletes the Server (ownership: the reporting Agent must own it)
  + its server-scoped RoleAssignments, and evicts the discovery / metrics caches for it so it doesn't reappear
  as importable. The Operation is marked done regardless. The completion needs the operation's kind — look it up
  from the Operation row (not trusted from the wire).
- **API** `POST /api/servers/{id}/delete` `{ confirmName, plan? }` → `202 {operationId}`; `400 confirmation_mismatch`
  / `invalid_plan`, `403 not_authorized`, `404 server_not_found`, `409 server_busy` (matches the other lifecycle
  POSTs rather than `DELETE`-with-body).
- **Status**: `ServerLiveStatus` gains `DELETING` (busy) for an in-flight `DeleteServer`. After enqueue the page
  redirects to `/servers`; the fleet poll drops a row whose id is missing from the batch response, and the
  detail page's poll navigating to `/servers` on 404.
- **Capacity**: nothing to do — committed memory is summed from owned containers on the Agent's next report.

## UI (Server Detail, static SSR)

A "Danger zone" card at the end of the Container settings section, shown only with `Server.Delete`: a
Destructive "Delete server…" button (`type=button`, `data-zw-dialog-open`) opens a native `<dialog>` holding a
static `EditForm FormName="server-delete"`:
server name + host, running? ("players are warned, then it's stopped safely" + the countdown preset select —
default first), "Removes the container", "Keeps world data and backups on the host at `<path>`; recovery is
manual", "This can't be undone", a text input "Type **<name>** to confirm", Cancel + Destructive Delete (disabled
until the input matches, JS). Without JS the dialog never opens (fail closed — nothing runs). Dialog JS in
`wwwroot/js/dialog.js` (open/close/Esc, enable-on-match), verified with an Edge fixture.

## Slices

### PR-A — Agent + Contracts
1. Contract `DeleteServer` + message/vocabulary tests.
2. `ServerDeleter` (absent → success; running → warn + stop → remove; failures reported) + tests.
3. `AgentCommandProcessor` case + redelivery guard + tests. Floors bumped.

### PR-B — Web
4. `Server.Delete` permission + built-in role tests + grant migrations (both providers) + drift test.
5. `OperationKind.DeleteServer`, dispatcher map, `ServerLifecycle.DeleteAsync` + confirmation precheck + audit.
6. Completion → `IServerRemoval` (row + role assignments + cache eviction), ownership-guarded.
7. Endpoint + tests; `ServerLiveStatus` DELETING; fleet/detail JS handling of vanished rows.
8. Danger-zone card + `<dialog>` + `dialog.js`; bUnit/page tests (hidden without permission, posted mismatch
   refused); `npm run build:css` if new classes.
9. Docs: getting-started Day-2 "Deleting a server", `ui-components.md` dialog pattern, socket-proxy research
   note. File follow-ups (delete world data; forget server). Floors bumped (csproj + ci.yml).

## Acceptance (from the issue)
Stopped + running delete live on DMZ (`docker ps -a` shows no container, Fleet row gone, wizard free memory
back up), Operator sees no control and a direct POST is refused + audited, `Server.Deleted` survives, proxy
allowlist unchanged, docs updated.
