# Issue #383 Mini-Plan — an operation on an offline host is refused, and nothing stays Pending forever

**Status:** one PR (branch `fix/383-offline-host-ops`). Found in #377's DMZ live pass; #377's branch is stacked on it.

**Written against:** issue #383; F11 (`OperationCoordinator`, `OperationDispatcher`, `OperationReaper`); ADR 0022
(per-server lock, leases); F10 (`IAgentConnectionRegistry`); #364 (`RegisterAsync` refusals).

## Objective

A server-changing action on a server whose host isn't connected is refused at once with a clear reason. No
Operation is created and the server stays usable. An Operation that still ends up Pending (the host dropped between the check and the
send) is failed after a short dispatch window, so its lock is freed. This also clears any that are already stuck.

## Facts found (2026-10-10)

- `OperationDispatcher.TryDispatchAsync` leaves an Operation Pending when the Agent has no connection ("no reconnect
  pump yet — the enqueue is the trigger"). Nothing dispatches it later.
- `OperationRepository.FindExpiredLeasesAsync` only selects Running/Cancelling, so a Pending Operation is never
  reaped. `Operation.Fail` is already legal from Pending (its doc anticipates "the reaper may fail a Pending Operation
  whose enqueue window elapsed").
- A Pending mutating Operation holds the per-server lock. The header shows its transitional label and refuses
  every other action.
- Every mutating, server-scoped enqueue is operator-initiated:
  - backup, restore;
  - config apply (form, raw, and mod list changes through `ConfigApplyEnqueuer`);
  - Workshop delete and mod-update restart (`ServerModManager`);
  - lifecycle (`ServerLifecycle`);
  - provision (`ServerInventory.RegisterAsync`).
- `RegisterAsync` inserts the Server row **before** it enqueues, so it needs its own check up front.
- `ServerConfigurationFailure.AgentOffline` already exists, with a UI message.
- Web tests enqueue against hosts with no connection; the existing pattern is
  `IAgentConnectionRegistry.Register(agentId, conn, () => {})`.

## Decisions

- **D1 — the coordinator refuses.** `OperationCoordinator` takes `IAgentConnectionRegistry`. A **mutating,
  server-scoped** request whose Agent isn't connected throws a new `HostOfflineException(serverId)` (Application,
  next to `ServerBusyException`). The check runs after the idempotency lookup and before the insert, so nothing is
  written and no lock is taken. Non-mutating Operations (probes, discovery, player lists) are unchanged: they're
  background or read-only and don't hold the lock.
- **D2 — each service maps it.** Each service catches `HostOfflineException` next to `ServerBusyException`, audits the
  failure as "host offline", and returns:
  - `BackupRequestFailure.HostOffline`
  - `RestoreRequestFailure.HostOffline`
  - `ServerConfigurationFailure.AgentOffline` (already exists)
  - `ModManagementFailure.HostOffline`
  - `ServerLifecycleFailure.HostOffline`

  Every UI and endpoint mapping says "That server's host is offline. Try again when it reconnects."
- **D3 — provisioning checks first.** `RegisterAsync` checks the connection before creating the Server and returns
  `ServerRegisterFailure.HostOffline`. The Deploy sheet already disables offline hosts, so this is the server-side
  guarantee.
- **D4 — the reaper fails stale Pending.** `OperationEngineOptions.PendingDispatchWindow` (default 2 min).
  `FindStalePendingAsync(now)` selects Pending Operations enqueued before `now - window`. The reaper fails them with
  "the host was offline, so this operation never started" and audits each like a lease expiry. It runs on the
  existing sweep, so a stuck Operation clears within about 30 s of deploying.
- **No reconnect pump.** Running a restore hours later, when a laptop wakes, would be a surprise. Offline means refuse.

## Tests (TDD)

1. **Coordinator:**
   - mutating + server-scoped + offline → `HostOfflineException`, nothing inserted, no audit;
   - online → enqueued;
   - non-mutating offline → still enqueued (Pending);
   - the idempotent re-enqueue of an existing key still returns it.
2. **Reaper:**
   - Pending past the window → Failed with the offline reason, and audited;
   - Pending inside the window → untouched;
   - Running past its lease → still the lease reason.
3. **Services:** backup, restore, config apply, mod manager and lifecycle each return their offline failure and
   enqueue nothing; `RegisterAsync` refuses offline and creates no Server.
4. **Web:** the offline message for backup/restore (bUnit) and lifecycle (ServerDetail and the endpoint). The harness
   marks the seeded host connected, so existing tests keep their meaning.
5. Floors.

## Live check (DMZ)

1. With Biggie stuck: deploy, and within about 30 s the Restore fails with the offline reason and Biggie's actions
   work again.
2. Sleep the laptop, then click Restore: you get the offline message immediately and nothing is queued.
3. Wake it, then restore: it works.
