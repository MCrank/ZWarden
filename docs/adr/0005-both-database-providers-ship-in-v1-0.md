# 5. Both database providers ship in v1.0, under five SQLite operating conditions

**SQLite and PostgreSQL are both supported v1.0 deployment modes.** The escape hatch written
into `docs/scope-and-sequencing.md` §6 (demote SQLite to development use if it cannot meet PRD
21's per-server locking) is **not triggered** — it was tested and SQLite passed. The support
comes with five conditions, one of which is a scope constraint rather than tuning: **only one
process may write, so any ZWarden.Web scale-out is a PostgreSQL-only deployment.**

- Status: accepted
- Decided in: [#9](https://github.com/MCrank/ZWarden/issues/9), the dual-provider spike; scope position from [#11](https://github.com/MCrank/ZWarden/issues/11)
- Versions exercised: EF Core / `.Sqlite` / `.Design` **10.0.12**, `Microsoft.Data.Sqlite` **10.0.12**, `Npgsql.EntityFrameworkCore.PostgreSQL` **10.0.3**, `Npgsql` **10.0.3**, `Testcontainers.PostgreSql` **4.15.0**, `dotnet-ef` **10.0.12**, `postgres:18.1-alpine`

## Context

PRD 9 wants one model that runs on both providers; PRD 21 wants only one conflicting mutating
Operation per Server at a time. Those two pull against each other, because the primitives that
make per-server locking easy on PostgreSQL — `SELECT … FOR UPDATE`, advisory locks,
`rowversion` — **do not exist on SQLite at all**. Supporting both was therefore a real risk,
not a packaging preference, and the sequencing spec deliberately left a door open to drop SQLite.

The work is also not deferrable: Feature 34 ships both modes and v1.1 needs PostgreSQL
regardless, so "support one provider now" would only hide the cost, not avoid it.

A throwaway spike settled it: **76 tests, 0 failures**, from one abstract `CorePersistenceSuite`
plus one abstract `PerServerLockingSuite` containing **no provider name and no provider branch**,
run four ways (SQLite × native, SQLite × text, PostgreSQL × native, PostgreSQL × text).

## The locking primitive, and the trap that does not fire

**The lock is a row whose primary key is the ServerId.** Acquire is an `INSERT`, release is a
`DELETE`, expiry takeover is an optimistic `UPDATE … WHERE Version = @original`. No row locks, no
`FOR UPDATE`, no advisory locks, no provider-specific SQL — which is the point, because none of
those three exist on SQLite. The backing partial unique index emits **verbatim identically** on
both providers:

```sql
CREATE UNIQUE INDEX "UX_Operations_ActiveMutating_PerServer"
  ON "Operations" ("ServerId") WHERE "IsMutating" AND "State" IN ('Pending', 'Running');
```

At 16 simultaneous contenders, both providers give `winners=1 losers=15` on every mechanism
tested, admit all 16 concurrent read-only Operations, and grant 16/16 locks on 16 different
Servers.

**The known SQLite killer is unreachable through EF Core.** A *deferred* transaction that reads
then writes returns `SQLITE_BUSY_SNAPSHOT`, for which SQLite deliberately does not invoke the
busy handler — so `busy_timeout` cannot rescue it. Probed directly:

```
SqliteConnection.BeginTransaction()               => BEGIN IMMEDIATE
SqliteConnection.BeginTransaction(deferred: true) => BEGIN DEFERRED
EF Core Database.BeginTransaction()               => BEGIN IMMEDIATE
```

EF takes the write lock at `BEGIN`. The failure mode is only reachable by dropping past EF to
raw ADO and asking for `deferred: true`, where `busy_timeout=5000` demonstrably does not help at
all — it turns a 4.5 s run into a 120 s one without fixing anything. **`lostUpdates=0` in every
configuration tested, including the broken ones:** even the wrong shape failed loudly.

Throughput is not the constraint. SQLite sustained 4,628 lock cycles/s at 16 workers with zero
errors, and readers were unaffected while 8 writers hammered the same database (p50 0.14 ms, p99
1.58 ms). Against a workload of human-initiated restarts, updates and backups — operations per
*minute* — that is orders of magnitude from the ceiling. (The cross-provider numbers are not
comparable: a local file against Docker-Desktop-on-WSL2 carries a network round trip a real
PostgreSQL deployment would not. Only the within-provider ordering should be trusted.)

## Alternatives considered

- **PostgreSQL only, SQLite demoted to development use.** The escape hatch. Rejected because
  the measurement removed its justification, and because it would make the simplest self-hosted
  deployment (PRD 2.4, criterion 1) require a database server.
- **Isolation levels instead of an application-managed lock.** Rejected: PostgreSQL's
  repeatable-read raises a serialization error where SQLite blocks, so the behaviour diverges
  precisely where PRD 21 needs it not to. An application-managed token behaves identically on
  both; the isolation route does not.
- **`IsRowVersion()` for the concurrency token.** Unusable as a shared mechanism: SQLite has no
  database-generated token at all, and on Npgsql `IsRowVersion()` means `xmin` and requires a
  `uint` property rather than SQL Server's `byte[]`. The portable mechanism is `Guid Version` +
  `.IsConcurrencyToken()` + assign-on-write, which maps to TEXT on SQLite and native `uuid` on
  PostgreSQL — identical behaviour, different DDL.

## The five conditions, which belong here rather than in folklore

1. **WAL, `busy_timeout` and `foreign_keys` must be set explicitly on every SQLite connection.**
   None is a default. Measured cost of getting it wrong, 32 concurrent writers: **147 ms with
   WAL + `busy_timeout=5000`, 1,492 ms with WAL alone, 10,582 ms with neither.**
2. **`SqliteCommand.CommandTimeout = 0` means *infinite* busy retry, not "no retry".**
   Microsoft.Data.Sqlite runs its own retry loop bounded by `CommandTimeout`; zero removes the
   bound, so a stuck write hangs forever. The operations engine must set a real value. (This ADR
   does not fix the number; Feature 11 does, and should record it.)
3. **Never call `BeginTransaction(deferred: true)`**, or reach past EF Core to raw ADO
   transactions, anywhere near the operations engine. This is worth an architecture test
   (PRD 15), because it is the one way to reach the unreachable trap.
4. **One writing process.** WAL coordinates through shared memory on a single host, and
   ZWarden.Web is a single process today, so this holds. **Any future scale-out of ZWarden.Web
   is therefore a PostgreSQL-only deployment.** That is a product constraint, written down now
   rather than discovered during a v1.1 capacity conversation.
5. **The lock is a row, not a held transaction.** No Operation may hold a database transaction
   open across agent work — a SteamCMD update, a backup — because on SQLite that blocks every
   writer for the duration. Feature 11 claims the lock row, commits, then works.

## Consequences

- **One genuine provider divergence survives, in one place.** Both providers throw
  `DbUpdateException` on an insert conflict, but the inner exception differs:
  `SqliteException [rc=19 ext=1555]` (`SQLITE_CONSTRAINT_PRIMARYKEY`) versus
  `PostgresException [23505]`. Anything inspecting the inner exception needs a provider switch —
  one switch, in one place. Shared test suites can assert `DbUpdateException` and nothing more
  specific.
- **Container lifetime for Testcontainers is hand-rolled.** No TUnit integration package exists
  (only `Testcontainers.Xunit`/`.XunitV3`). The spike's static harness works; PostgreSQL also
  needed `-c max_connections=500` and a capped `MaxPoolSize` before a parallel suite creating a
  database per test would run. Feature 0/2 owes a decision on the shape of this.
- **Every provider difference that silently passes on one side is a test-suite hazard**, and the
  dual-provider suite exists to catch them: SQLite's `LIKE` is case-insensitive for ASCII while
  PostgreSQL's is case-sensitive; text `ORDER BY` is BINARY on SQLite and locale-dependent on
  PostgreSQL; type facets are enforced on PostgreSQL and ignored on SQLite; and after any error
  inside a PostgreSQL transaction every later command fails with `25P02` until rollback, where
  SQLite has no equivalent.
- **Things we know we have not measured, ranked by how much they could still hurt:** crash
  durability was measured at `synchronous=NORMAL`, which in WAL mode can lose the last commits
  on OS or power failure — this bears directly on PRD 20's durability claim and may force
  `synchronous=FULL`, which would change the throughput numbers; **SQLite on a bind-mounted or
  network volume was never tested**, and SQLite's locking is documented as unsafe over NFS/SMB,
  so a specific Feature 34 packaging choice could still invalidate the verdict; and only the
  *initial* migration was applied, so SQLite's table-rebuild `ALTER TABLE` limits are the
  likeliest source of future divergence.
