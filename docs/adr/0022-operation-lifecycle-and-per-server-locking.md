# 22. The Operation lifecycle, its failure and timeout semantics, and the realized per-server lock

An **Operation** moves through a fixed state machine — **`Pending` → `Running` →
(`Succeeded` | `Failed` | `Cancelled`)**, with `Cancelling` as the one transient waypoint — and
that lifecycle, not the agent-reported `OperationOutcome`, is the durable record five downstream
features build on. **Per-server serialization is the Operation row itself**: a partial unique index
admits at most one *mutating* Operation per Server in a non-terminal state, so acquiring the lock
**is** inserting the Operation and releasing it **is** the Operation reaching a terminal state. No
Operation holds a database transaction across agent work (ADR-0005 condition 5); the lock is a
committed row, not a held lock. A **lease** bounds every `Running` Operation and a **reaper**
fails an expired one, which is what frees the lock when an Agent dies mid-work. The SQLite
`CommandTimeout` the operations engine runs under is **30 seconds** (ADR-0005 condition 2, deferred
to this feature).

- Status: accepted
- Decided in: [#33](https://github.com/MCrank/ZWarden/issues/33) (Feature 11), realizing the locking primitive proven in [#9](https://github.com/MCrank/ZWarden/issues/9)
- Bears on: PRD 20 (durability + idempotency), PRD 21 (one conflicting mutating Operation per Server), [ADR-0005](./0005-both-database-providers-ship-in-v1-0.md) (which deferred three items here), [ADR-0004](./0004-typed-ids-are-stored-as-native-uuid.md) (UUIDv7 ids), [ADR-0016](./0016-tenant-isolation-query-filter-and-default-tenant.md) (tenant ownership), [ADR-0019](./0019-audit-is-append-only-tenant-owned-and-binds-the-auth-sink.md) (audit), [ADR-0020](./0020-agent-protocol-versioning-and-catalogue.md) (the command/event contract the engine drives)

## Context

PRD 21 requires that **only one conflicting mutating Operation run against a Server at a time**, and
PRD 20 requires operations to be **durable and idempotent** — survive a Web restart, and not run
twice if a message is redelivered. ADR-0005 already did the hard measurement: it proved a
**provider-portable, application-managed lock** (a row you `INSERT` to claim and an optimistic
`Guid Version` you `UPDATE` to take over) works identically on SQLite and PostgreSQL, and it
explicitly rejected `SELECT … FOR UPDATE`, advisory locks, `xmin`/`rowversion`, and isolation-level
serialization because those diverge across the two providers exactly where PRD 21 needs them not to.

ADR-0005 then deferred three things to "Feature 11": the concrete **`CommandTimeout` value** for
SQLite (condition 2), the **architecture test** forbidding `BeginTransaction(deferred: true)` and
raw-ADO transactions near the engine (condition 3), and the shape of the lifecycle itself. This ADR
settles them, and pins the state machine, lease/reaper rule, idempotency mechanism, and cancellation
contract that F13, F15, F20b, F22 and F24 all inherit.

The engine has no *mutating* command to run yet — the real ones (`RestartServer`, `ApplyConfig`,
`Backup`) land with their owning features. F11 ships one **non-mutating** `Diagnostics.Ping`
command so the dispatch→ingest loop is exercisable end-to-end in production the day the engine
merges. Because ping is non-mutating it does **not** claim the per-server lock; the lock is proven
with mutating *test* Operations and goes live the moment F13's first mutating command exists.

## Decision

### The state machine

An Operation is created `Pending`, is dispatched to become `Running`, and ends in exactly one
terminal state. The transitions are the whole vocabulary; nothing else is legal.

```
             enqueue                dispatch                 agent OperationCompleted(Succeeded)
   (none) ─────────────▶ Pending ─────────────▶ Running ───────────────────────────────▶ Succeeded  (terminal)
                            │                       │        agent OperationCompleted(Failed)
                            │                       ├───────────────────────────────────▶ Failed     (terminal)
                            │                       │        lease expiry (reaper)
                            │                       ├───────────────────────────────────▶ Failed     (terminal)
                            │                       │        cancel requested
                            │                       └──────────────▶ Cancelling ─────────▶ Cancelled  (terminal)
                            │  cancel requested (never dispatched)
                            └──────────────────────────────────────────────────────────▶ Cancelled  (terminal)
```

- **Terminal states are immutable.** No transition leaves `Succeeded`, `Failed` or `Cancelled`.
- **`Cancelling`** is the only transient: a `Running` Operation asked to cancel enters `Cancelling`,
  and reaches `Cancelled` when the Agent acknowledges, or `Failed` if the reaper times it out first.
- The **operation state is authoritative**, distinct from the agent-reported `OperationOutcome`
  (`Succeeded`/`Failed`) on `OperationCompleted`. The engine maps the outcome onto the state;
  `Cancelled` and the whole `Pending`/`Running`/`Cancelling` lifecycle have no agent-side equivalent.

### The per-server lock is the Operation row

There is **no separate lock table.** A partial unique index makes the Operation row its own mutex:

```sql
CREATE UNIQUE INDEX "UX_Operations_ActiveMutating_PerServer"
  ON "Operations" ("ServerId") WHERE "IsMutating" AND "State" IN ('Pending', 'Running');
```

This is the DDL ADR-0005 measured emitting **verbatim-identically** on both providers.

- **An Operation carries a required `AgentId` (its executor) and a nullable `ServerId` (its subject).**
  Every Operation runs on an Agent; it acts on a Server, or on the host Agent itself for host-level
  work (`Diagnostics.Ping` is agent-scoped, `ServerId` null). A **mutating** Operation is always
  server-scoped (enforced at enqueue), so the lock's partial index never has to reason about a null
  `ServerId` — mutating ⟹ `ServerId` present, and non-mutating rows are excluded by the `IsMutating`
  predicate. Until F14 inventory exists there is no `ServerId → AgentId` resolution, so the caller
  supplies the `AgentId` directly; F14 later resolves it from the Server.

- **Acquire = `INSERT`.** Enqueueing a mutating Operation inserts it `Pending`; a second mutating
  Operation for the same Server in `Pending`/`Running` violates the index and throws
  `DbUpdateException`, which the engine translates to a typed **"server busy"** result. The one
  surviving provider divergence (ADR-0005) — `SqliteException [rc=19 ext=1555]` vs
  `PostgresException [23505]` — is inspected in **one** place and nowhere else.
- **Release = reaching a terminal state.** A terminal Operation no longer matches the index
  predicate, so the slot frees with the same write that records the outcome. Nothing is deleted.
- **Read-only Operations never contend** (`IsMutating = false`, so they are outside the index) —
  any number run against the same Server at once. `Diagnostics.Ping` is one of these.
- **Concurrency token.** `IVersioned.Version` (portable `Guid`, `.IsConcurrencyToken()`,
  assign-on-write) guards every state transition; a stale transition throws
  `DbUpdateConcurrencyException` and is retried against fresh state. This is the takeover mechanism
  for the reaper, per ADR-0005.

### Lease and reaper

Every `Running` Operation carries a **`LeaseExpiresAt`**, set when it is dispatched and extended by
each `OperationProgress`. An `OperationReaper` hosted service periodically transitions any `Running`
(or `Cancelling`) Operation past its lease to **`Failed`** with a non-secret reason
(`"lease expired — agent did not report completion"`), via an optimistic `Version` update. This is
what releases the per-server lock when an Agent dies, disconnects, or hangs mid-work — the lock is
never orphaned because its holder is the Operation row and the reaper always fails a stalled holder.

### Idempotency (two layers, PRD 20)

- **Enqueue idempotency (Web).** A caller supplies an **idempotency key**; a unique index on
  `(TenantId, IdempotencyKey)` means a duplicate enqueue **returns the existing Operation** rather
  than creating a second. This makes "start operation X" safe to retry.
- **Execution idempotency (Agent).** The `OperationId` travels on the command `Envelope`; the Agent
  dedupes a redelivered command by `OperationId` so a reconnect/replay does not run the work twice.

### Cancellation

- A **`Pending`** Operation cancels immediately to `Cancelled` (it was never dispatched).
- A **`Running`** Operation enters `Cancelling` and a cancel is signalled to the Agent; it reaches
  `Cancelled` on the Agent's acknowledgement or **`Failed`** if the reaper's lease fires first.
  Cancellation is **cooperative** — a mutating Operation already past the point of no return on the
  host may still complete; the state reflects what actually happened, never a hopeful guess.

### The SQLite conditions this feature owed

- **`CommandTimeout = 30 seconds`** is confirmed as the deliberate operations-engine value (ADR-0005
  condition 2: `0` means *infinite* busy-retry, which must not be inherited). The persistence
  foundation (F0) already applies `ZWardenDbProviderExtensions.CommandTimeoutSeconds = 30` on both
  providers; F11 adopts that as the engine's value and records it here — a stuck write fails at 30 s
  rather than hangs. If the engine ever needs a different bound it changes here, not silently.
- **An architecture test forbids `BeginTransaction(deferred: true)` and raw-ADO transactions**
  anywhere in the operations engine (ADR-0005 condition 3 — the one path to SQLite's unreachable
  `SQLITE_BUSY_SNAPSHOT` trap). This extends the existing `DeferredTransactionGuardTests`.
- **No Operation holds a transaction across agent work** (ADR-0005 condition 5): the engine claims
  the lock row, **commits**, then dispatches and awaits Agent events — never `BEGIN … <agent job> …
  COMMIT`, which on SQLite would block every writer for the duration of a SteamCMD run or a backup.

## Alternatives considered

- **A dedicated `ServerOperationLock` table (PK = ServerId), also portable per ADR-0005.** Rejected
  for v1.0: it needs two writes to acquire and keeps two rows consistent, introducing an orphan-lock
  class of bug (a crash between "lock taken" and "operation created") that the partial-index shape
  cannot have because the lock *is* the operation. A partial index already indexes only in-flight
  rows, so the dedicated table's one advantage — a tiny hot set — is already delivered. It stays
  available as a fallback if a future need arises to hold a server lock with **no** Operation (e.g.
  a maintenance drain); that is better modelled as Server lifecycle state (F14+) and does not tip
  the choice now.
- **Deriving `IsMutating` from a closed `OperationKind` enum only.** Rejected as the sole source:
  with `Diagnostics.Ping` the only kind and it non-mutating, no mutating Operation could be
  constructed to test the lock. `IsMutating` is set at enqueue from the command's declared
  mutating-ness, so tests exercise both and the lock is proven before any mutating command ships.
- **A `TimedOut` terminal state distinct from `Failed`.** Rejected as needless surface: a lease
  expiry is a failure with a specific, non-secret reason string. Downstream features match on
  `Failed`; the reason carries the nuance.
- **Ship no command and test the engine with a test-only fake `AgentCommand`.** Rejected: the
  polymorphic discriminator and the closed-vocabulary guard scan the Contracts assembly, so a
  test-assembly fake cannot prove the real serialization/versioning round-trip without polluting the
  guard. One honest, permanent `Diagnostics.Ping` proves the wire path and is independently useful
  for troubleshooting (CONTEXT.md: monitoring and troubleshooting).

## Consequences

- **The invariant is database-enforced, not discipline-enforced.** "One mutating Operation per
  Server" cannot be violated even by a bug in the engine or a race between two Web instances — the
  index refuses the second insert. This is the property PRD 21 needs.
- **Scale-out is PostgreSQL-only (inherited from ADR-0005 condition 4), and the lock scales with
  it.** Contention is per-`ServerId`; Operations on different Servers never touch the same index
  entry, so throughput grows with Server count — the axis a v1.1 hosted deployment grows along.
  Cross-instance correctness needs no coordination beyond the shared Postgres unique constraint.
- **The Operations table grows unboundedly** (history of every Operation, every tenant). This is a
  retention/partitioning problem, **not** a lock problem — the lock check only ever hits the small
  partial index over in-flight rows. Retention is deferred to when hosted scale demands it.
- **A durability edge remains open (ADR-0005).** Crash durability was measured at
  `synchronous=NORMAL`, which in WAL mode can lose the last commits on OS/power failure; if PRD 20's
  durability claim needs `synchronous=FULL` that changes SQLite throughput. Not decided here;
  flagged for the packaging feature (F34).
- **Cancellation is honest, not absolute.** An operator can ask, but a mutating Operation past its
  point of no return still completes. The UI wording this implies is a downstream (F15+) concern.
- **`Cancelling` and the lease exist before anything long-running does.** `Diagnostics.Ping`
  completes instantly, so F11 never really sits in `Cancelling` or trips a lease in production —
  but the machinery ships now so F13/F15's genuinely long operations inherit a proven lifecycle
  rather than growing one.
