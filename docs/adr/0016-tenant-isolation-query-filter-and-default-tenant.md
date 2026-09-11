# 16. Tenant isolation: an always-on EF global query filter over an ambient tenant context, with a fixed self-hosted default tenant

ZWarden enforces tenant isolation **in application code**, not in the database engine. Every
`ITenantOwned` entity gets an **EF Core global query filter** `e => e.TenantId == currentTenantId`,
applied by a model convention, where `currentTenantId` is read at query time from an ambient
**`ITenantContext`** whose value comes from the authenticated session and **never** from a request
parameter (PRD 7A). A `SaveChanges` interceptor stamps the ambient tenant onto inserted rows and
**rejects** any insert or update that would cross or mutate a tenant scope. The mechanism is **fail
closed**: a model that maps a tenant-owned entity without a tenant context does not build, and an
`ITenantContext` with no ambient tenant throws rather than yielding `TenantId.Empty`. The filter is
**always evaluated, including in a one-tenant install**, and `IgnoreQueryFilters` is contained to a
single sanctioned tenant-admin seam. The **self-hosted default tenant has a fixed, well-known
`TenantId`** (a compile-time constant), seeded idempotently at startup.

- Status: accepted
- Decided in: [#25](https://github.com/MCrank/ZWarden/issues/25) (the F3A mini-plan)
- Bears on: PRD 7A (multi-tenancy domain model, "the browser never selects a tenant id"), PRD 12
  (authorization re-made server-side), `trust-boundaries.md` §6 (Tenant→Tenant, "the filter is always
  evaluated" and the v1.0 two-tenant fixture) and §9 rule 4, ADR 0005 (both providers, one
  provider-agnostic model), and every later tenant-owned feature (F4 users, F7 Agents, F9 enrollment,
  F14 Servers, F30 diagnostics)

## Context

PRD 7A requires that "every tenant-owned record carry an immutable tenant scope" and that "the browser
never be trusted to select an arbitrary tenant ID" — context derives from the authenticated session.
`trust-boundaries.md` §6 sharpens this into two commitments that shape the mechanism: **the filter is
evaluated even in a self-hosted install with exactly one tenant**, and **cross-tenant integration tests
run in v1.0 against a two-tenant fixture** — a boundary never exercised is not a boundary. §9 rule 4
turns it into a build-time assertion: "every tenant-owned query passes through the tenant filter; no
repository exposes an unscoped read."

Two forces make this a decision rather than a default:

- **Where isolation is enforced.** ADR 0005 ships one provider-agnostic EF model on SQLite *and*
  PostgreSQL. Postgres row-level security would be a second, provider-specific enforcement path that
  SQLite cannot mirror, splitting the guarantee across two mechanisms. Hand-written `WHERE TenantId = …`
  in each query, by contrast, is a guarantee that depends on every author remembering it — exactly the
  "by discipline, not by construction" posture F3 rejected for secrets.
- **What "self-hosted single tenant" means concretely.** PRD 7A says self-hosted installs "use the same
  tenant-aware domain model even when the default installation contains only one tenant … to avoid a
  later multi-tenancy rewrite." That default tenant needs an identity that is stable across restarts and
  migrations and cheap to reference from a bootstrap that runs before any user exists.

EF Core global query filters are the platform-native answer to the first, but they carry documented
footguns that must be pinned down rather than inherited: a filter that reads a *nullable/unset* tenant id
silently matches unset rows; `IgnoreQueryFilters()` silently disables the whole guarantee; raw SQL
bypasses filters entirely; and the model (with its filter lambda) is cached, so the current tenant must
be captured per-context-instance, not baked into the cached model.

## Decision

**Enforce isolation in application code with a global query filter over an ambient context.** A third
`ZWardenDbContext` model convention (beside the typed-id and `IVersioned` conventions) adds, to every
entity implementing the Domain marker `ITenantOwned` (`TenantId TenantId { get; }`, init-only —
immutable), the filter `e => e.TenantId == ctx.CurrentTenantId`, evaluating the ambient `ITenantContext`
held by the context instance at query time. No Postgres RLS; no hand-written per-query predicate.

**Derive the tenant from the session, never the request.** `ITenantContext` (Application) exposes
`CurrentTenantId`/`HasCurrentTenant` and has no method that accepts a tenant id from a caller. F3A ships
one implementation, `SingleTenantContext`, returning the default tenant; F3B/F4 add the session-derived
implementation behind the same interface.

**Fail closed, deliberately, at three points.** (1) If the model maps an `ITenantOwned` entity and no
`ITenantContext` was supplied, model creation **throws** — there is no filter-absent path that returns
every tenant's rows. (2) `CurrentTenantId` with no ambient tenant **throws** rather than returning
`TenantId.Empty`, because an empty id would make the filter match unset rows. (3) A `SaveChanges`
interceptor **rejects** an inserted tenant-owned row whose `TenantId` is set to a foreign tenant, and any
update that changes `TenantId` or writes a row not owned by the ambient tenant — stamping the ambient
tenant when the insert leaves it unset.

**Always evaluate the filter, and contain the escape hatch.** The filter runs in a one-tenant install
too. `IgnoreQueryFilters()` is permitted only in a single sanctioned tenant-administration seam (the
bootstrap's read of the un-scoped `Tenant` table, and F3D later); an architecture guard fails the build
on its use anywhere else and asserts that every `ITenantOwned` type in the built model actually carries a
filter. Raw SQL over tenant-owned tables is off-limits outside the repository (F3A adds none).

**The `Tenant` entity is not tenant-owned.** A Tenant is not owned by a tenant; it *is* one, so it carries
no filter, and reads against the `Tenants` table are the narrow, sanctioned unscoped reads.

**A fixed, well-known default tenant.** The self-hosted default tenant's `TenantId` is a compile-time
constant (a fixed UUIDv7-shaped value), and a bootstrapper inserts that row idempotently after migration.
Not generate-once-and-store: a constant is deterministic across restarts, environments, and test runs;
lets the bootstrap and `SingleTenantContext` reference it without a prior read; and is safe to publish
because a tenant id is not a secret and the filter is always evaluated regardless.

## Alternatives considered

- **PostgreSQL row-level security.** Rejected for v1.0: it is provider-specific, so SQLite (the default
  self-hosted mode, ADR 0005) would need a second enforcement path, splitting one guarantee across two
  mechanisms and defeating the single-model commitment. Kept in reserve as defence-in-depth for a future
  hosted-only hardening feature, behind the same `ITenantOwned` boundary.
- **Hand-written `WHERE TenantId = @tenant` in each query / repository method.** Rejected: correctness
  depends on every author (and every future one) remembering the predicate on every read, and §9 rule 4
  explicitly wants "no repository exposes an unscoped read" as a structural property, not a convention.
  The global filter makes the scoped read the *default* and the unscoped read the flagged exception.
- **A nullable ambient tenant with a filter that no-ops when unset.** Rejected: this is fail-open — an
  unset context would silently return unfiltered (or unset-row) data. The chosen design throws instead, so
  a missing tenant context is a loud failure, never a quiet leak.
- **Generate the default tenant id once at first startup and persist it.** Rejected: it adds a
  read-before-write bootstrap ordering problem, makes the id differ per environment (so tests and
  fixtures cannot name it), and buys unpredictability that has no value — the id is not a secret and the
  filter is enforced independently of it.
- **A single shared migrations assembly for both providers.** Rejected: EF migration snapshots and DDL
  are provider-specific, so one assembly cannot host both histories cleanly; F3A ships two
  provider-specific migrations assemblies selected by `UseZWardenProvider`. (Recorded here because it is a
  direct consequence of this ADR's single-model-two-providers stance; the structure detail lives in the
  F3A mini-plan.)

## Consequences

- **Isolation is a property of the model, not of caller discipline.** Declaring `ITenantOwned` opts a type
  into both the filter and the ownership interceptor; a new tenant-owned entity is scoped the moment it is
  mapped, and the architecture guard fails the build if it somehow is not. This is the "by construction"
  that earns the ADR.
- **`IgnoreQueryFilters`, raw SQL, and `DbContext.Find` across filters remain the known ways to defeat the
  filter.** They are contained by the guard and by routing reads through repositories, but the containment
  is a rule a reviewer must uphold on any raw-SQL feature — named here so it is not rediscovered as a leak.
- **The current tenant is captured per context instance.** Because the model (and its filter lambda) is
  cached, the context is constructed with its `ITenantContext` and the filter closes over that instance;
  a mis-scoped context instance would mis-scope its reads — so `ITenantContext` is request-scoped and
  wired once, in `AddTenantFoundation`.
- **The default tenant id is a published constant.** Anyone can read it; this is acceptable because it is
  an identifier, not a credential, and cross-tenant access is denied by the filter and interceptor whether
  or not an attacker knows an id. A hosted deployment simply never uses the single-tenant context.
- **The `Tenant` table's unscoped reads are a deliberate, narrow exemption.** The bootstrap and F3D's
  administration read it without the filter; that exemption is the one place `IgnoreQueryFilters` is
  allowed, and it is guarded so it cannot quietly widen.
- **Every later tenant-owned feature inherits scoping for free but owes the two-tenant test.** F4/F7/F9/
  F14/F30 add `ITenantOwned` types and get the filter automatically; trust-boundaries §6 still expects
  each to exercise the boundary, and the v1.0 two-tenant fixture F3A builds is the shared harness for that.
