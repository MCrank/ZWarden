# Feature 3A Mini-Plan — Tenant Foundation

**Status:** ready for implementation. Roadmap issue: [F3A (#25)](https://github.com/MCrank/ZWarden/issues/25). Track A, immediately after F3.

**Format:** PRD 60. **Written against:** PRD §7A (Multi-Tenancy and SaaS Domain Model), §8 (typed IDs),
§12 (authorization is re-made server-side, hiding a control is not authorization), the trust boundaries
([`trust-boundaries.md`](../trust-boundaries.md) §6 Tenant→Tenant and §9 rule 4 "the tenant filter"),
ADR [0004](../adr/0004-typed-ids-are-stored-as-native-uuid.md)/[0014](../adr/0014-typed-id-pattern.md)
(typed IDs — `TenantId` already exists), ADR [0005](../adr/0005-both-database-providers-ship-in-v1-0.md)
(both providers, the `IVersioned` token, migrations), and ADR
[0016](../adr/0016-tenant-isolation-query-filter-and-default-tenant.md) (the isolation mechanism and the
fixed default tenant, written with this plan). It also discharges three forward-references the earlier
features left for F3A by name: `MigrationRunner`/`TestModel` ("the first production migration is F3A's")
and `ReferenceDirectionTests` (§9 rule 4 "the tenant filter … arrive[s] with the feature that adds the
types").

## Objective

Deliver the tenancy every later control-plane feature is scoped by: a **`Tenant`** entity (the first
production table, and thus the first real migration on both providers); an **`ITenantOwned`** ownership
rule that stamps an **immutable** tenant scope onto every tenant-owned record; a **tenant filter** that
is *always* evaluated — an EF Core global query filter keyed on an ambient **`ITenantContext`** that the
browser can never influence (PRD 7A) — so that a tenant-owned read returns another tenant's rows *by
construction never, not by remembering a `WHERE`*; **tenant-aware repositories** that expose no unscoped
read; and the **single-tenant self-hosted bootstrap** so F9 (Agent enrollment, which binds to a tenant)
does not wait on the v1.1 tenant-administration feature. The whole thing is proven against a **two-tenant
fixture** in v1.0, because a boundary that is never exercised is not a boundary (trust-boundaries §6).

## Dependencies

- **F3** — the security foundation. F3A stores no secrets itself, but it is the feature that first turns
  the DbContext into a *persisting* host (migrations on startup, the bootstrap), so it lands on F3's
  fail-closed posture and DI conventions (`AddSecurityFoundation` is the shape `AddTenantFoundation`
  copies).
- **F2** — the persistence foundation: `ZWardenDbContext`, the typed-id and `IVersioned` model
  conventions (F3A adds a third convention — the tenant filter — beside them), `UseZWardenProvider`, the
  `VersionStampingInterceptor`, and `MigrationRunner` (whose `MigrateAsync` finally has migrations to run).
- **F1** — typed IDs. `TenantId` (`ten-`) already exists in the registry (PRD 7A); F3A consumes it and
  adds no new prefix.
- **F0** — the offline tier for the unit and model tests, the networked tier for the Postgres migration
  and the cross-provider isolation test, and warnings-as-errors (ADR 0013).
- ADR 0016 — the isolation mechanism (global query filter over an ambient context + a fail-closed
  ownership interceptor) and the fixed, well-known **default tenant** id.

## Scope

1. **`Tenant` entity + its table (PRD 7A).** A `Tenant : IVersioned` with a `TenantId Id`, a display
   `Name`, and the concurrency `Version`. It is deliberately **not** `ITenantOwned` — a Tenant is not
   owned by a tenant, it *is* one — so the tenant filter never applies to it, and the narrow set of reads
   against the Tenant table (the bootstrap, and F3D's later administration) are the sanctioned unscoped
   reads. `TenantConfiguration : IEntityTypeConfiguration<Tenant>` maps the `Tenants` table.
2. **`ITenantOwned` ownership rule (PRD 7A "immutable tenant scope").** A Domain marker interface
   carrying `TenantId TenantId { get; }`. Implementers expose it as init-only; the scope is assigned once
   and never changed. This is the "tenant-scoped identifier / ownership rule" deliverable in the entity
   model rather than in prose.
3. **The tenant filter (trust-boundaries §6, §9 rule 4; ADR 0016).** A third `ZWardenDbContext` model
   convention: every `ITenantOwned` entity gets an EF Core global query filter
   `e => e.TenantId == currentTenantId`, where `currentTenantId` is read from the context's ambient
   **`ITenantContext`** at query time. **Fail closed:** if the model contains an `ITenantOwned` entity and
   no `ITenantContext` was supplied, model creation throws — there is no "filter absent ⇒ everything
   visible" path. The filter is evaluated identically in a single-tenant install.
4. **`ITenantContext` (PRD 7A — derived from the session, never the browser).** An Application
   abstraction exposing `CurrentTenantId` (and `HasCurrentTenant`); reading `CurrentTenantId` with no
   ambient tenant **throws** (fail closed, never a silent empty id). F3A ships exactly one implementation,
   the self-hosted `SingleTenantContext` that returns the default tenant; F3B/F4 add the session-derived
   implementation behind the same interface. No implementation takes a tenant id from a request parameter.
5. **The ownership interceptor (PRD 7A "immutable scope", trust-boundaries §6 "nothing crosses").** A
   `TenantScopeInterceptor : SaveChangesInterceptor` beside the version stamper: on an **inserted**
   `ITenantOwned` entity it stamps the ambient tenant when unset and **rejects** a foreign tenant id when
   set; on a **modified** one it rejects any change to `TenantId` (immutability) and any write whose
   `TenantId` is not the ambient tenant (no cross-tenant write). Rejection is a clear, secret-free
   exception — writes fail closed.
6. **Tenant-aware repositories (deliverable "tenant-aware repositories"; §9 rule 4 "no repository exposes
   an unscoped read").** An Application `ITenantScopedRepository<TEntity>` and an Infrastructure base whose
   query surface is only the *filtered* `DbSet<TEntity>` — it never calls `IgnoreQueryFilters()`. The
   pattern (not a generic UoW framework) is the deliverable; it is proven against the test-only
   tenant-owned entity, exactly as F2 proved its conventions against `Widget`.
7. **Single-tenant self-hosted bootstrap (absorbed from F3D so F9 does not wait on v1.1).** A fixed,
   well-known default `TenantId` (ADR 0016) and a `TenantBootstrapper` that idempotently inserts the
   default `Tenant` row after migration. `SingleTenantContext` resolves every request to it. This is the
   sanctioned unscoped read of the Tenant table.
8. **The first production migration, per provider (F2's forward-reference; user-confirmed scope).** Two
   provider-specific migrations assemblies — `ZWarden.Migrations.Sqlite` and `ZWarden.Migrations.Postgres`
   — each with a design-time context factory and the initial `Tenants` migration; `UseZWardenProvider`
   selects the matching `MigrationsAssembly`. `MigrationRunner.MigrateAsync` now has real migrations; the
   two-assembly split is forced by ADR 0005 (divergent DDL, provider-specific model snapshots).
9. **Isolation tests against a two-tenant fixture (deliverable "tenant isolation tests";
   trust-boundaries §6 "cross-tenant integration tests run in v1.0").** Tenant A's context cannot see
   tenant B's rows; inserts are auto-stamped; a cross-tenant insert or update throws; the filter holds on
   a real SQLite database (offline) and on PostgreSQL (networked).
10. **The §9 rule-4 architecture guard.** A model-level assertion that *every* `ITenantOwned` entity in
    the built model carries a query filter (a new tenant-owned type with no filter is a red build), plus a
    source guard that `IgnoreQueryFilters` appears nowhere outside a single sanctioned tenant-admin seam.

## Non-scope

- **Tenant membership, invitations, settings, and the administration UI** — F3D (v1.1). F3A creates the
  *default* tenant programmatically; it does not let anyone create, name, or manage tenants.
- **The Auth0 Organization ↔ Tenant mapping and any external-org reference column** — F3B (v1.1) owns
  "organization-to-tenant mapping" and "external subject mapping". F3A's `Tenant` carries no
  external-identity field: an unused, untested column now would just be a migration to alter later. The
  glossary keeps `Auth0 Organization` and `Tenant` non-interchangeable (PRD 63A) regardless.
- **Authentication and the session-derived `ITenantContext`** — F4. F3A ships the interface and the
  single-tenant implementation; the "derive the tenant from the authenticated session" implementation
  needs Identity, which is F4.
- **Authorization / RBAC** — F5 (PRD 12A). The tenant filter is a *scope* boundary, not a permission
  check; "may this user act in this tenant" is F5. F3A denies cross-tenant *access* by default; it does
  not decide intra-tenant permissions.
- **Row-level security in the database engine.** Isolation is enforced in application code (the filter +
  interceptor), consistent with ADR 0005 (one provider-agnostic model); Postgres RLS is not used in v1.0.
- **Host UI wiring beyond making the host persist.** F3A wires `AddTenantFoundation` + the
  bootstrap/migrate startup path (SQLite default needs no external service), but ships no tenant-facing
  pages.

## Domain changes

- **`Tenant`** (entity) and **`ITenantOwned`** (ownership marker) live in `ZWarden.Domain` beside
  `IVersioned` — pure types with no infrastructure dependency (the reference-direction guard keeps Domain
  dependency-free). **`ITenantContext`** and **`ITenantScopedRepository<T>`** live in
  `ZWarden.Application` (ambient current-tenant and the read seam are use-case concerns, referencing only
  Domain), mirroring where F3's abstractions sat. The concrete filter convention, interceptor,
  repository base, `SingleTenantContext`, bootstrapper, and migrations live in `ZWarden.Infrastructure`
  and the two migrations assemblies — the same Domain/Application/Infrastructure split F1–F3 used.
- **Glossary (`CONTEXT.md`):** add **Tenant-owned** (a record carrying an immutable tenant scope, filtered
  on every read), **Tenant filter** (the always-evaluated scope check derived from the session, never the
  browser), and **Default tenant** (the single well-known tenant of a self-hosted install). `Tenant` and
  `Auth0 Organization` are already defined; terms only, no implementation detail.

## Contract changes

- **`ITenantContext`** — the one way any code learns the current tenant; the value comes from the
  session, never a request parameter; reading it with no ambient tenant throws.
- **`ITenantOwned`** — the one way an entity declares it is tenant-scoped; declaring it opts the type into
  the filter *and* the ownership interceptor automatically.
- **The isolation contract** — a tenant-owned read returns only the ambient tenant's rows; a cross-tenant
  insert/update throws; the `TenantId` of a persisted tenant-owned row is immutable.
- **`MigrationsAssembly` per provider** — `UseZWardenProvider(Sqlite|Postgres, …)` now binds the matching
  migrations assembly; a deployment's provider choice selects its migration history.

## Security considerations

- **The browser never selects a tenant (PRD 7A / trust-boundaries §2, §6).** `ITenantContext` has no
  request-parameter path; the guard and code review keep it that way. Hiding a UI control is not
  authorization (PRD 12), and scope is not authorization either — both are re-made server-side.
- **Fail closed, three ways:** a model with tenant-owned entities but no tenant context does not build; an
  `ITenantContext` with no ambient tenant throws rather than returning `TenantId.Empty` (which the filter
  would read as "match unset rows"); a cross-tenant or scope-changing write throws rather than persisting.
- **The filter is always evaluated (trust-boundaries §6)** — including in a one-tenant install — so the
  hosted deployment inherits a boundary that has been exercised since v1.0, not one switched on later.
- **`IgnoreQueryFilters` is contained.** It is the documented escape hatch that would silently defeat the
  filter; the architecture guard forbids it outside one sanctioned tenant-admin seam, so an unscoped read
  cannot be introduced by a one-line call that reviewers miss.
- **Raw SQL bypasses query filters** — noted as a standing rule (F3A adds none); any future raw-SQL read of
  a tenant-owned table must scope by hand, and belongs behind the repository, not in a component.
- **Error messages carry no tenant data** (trust-boundaries §2) — a rejected cross-tenant write names
  *that* the scope was wrong, never another tenant's id or row contents.

## Test plan

Written before the code (PRD 2.2). Offline tier except the Postgres migration/isolation checks (networked).

1. **Ownership stamp** — inserting an `ITenantOwned` entity with an unset `TenantId` under tenant A stamps
   it with A; reading back shows A.
2. **Filtered read** — with rows owned by A and by B in one database, tenant A's context reads only A's
   rows (`.Count()`, `.Find`, and a `Where` all agree); B's context reads only B's.
3. **Cross-tenant insert rejected** — inserting an entity whose `TenantId` is B while the ambient tenant is
   A throws, and nothing is written.
4. **Immutable scope** — loading an A-owned entity and attempting to change its `TenantId` (to B or to
   empty) throws on save; the stored value is unchanged.
5. **Cross-tenant update rejected** — an A-owned row cannot be updated while the ambient tenant is B (it is
   invisible to B's context, and a forced update throws).
6. **Fail-closed context build** — constructing a context that maps an `ITenantOwned` entity without an
   `ITenantContext` throws at model creation; the message names the missing context, not a row.
7. **Fail-closed ambient** — `SingleTenantContext.CurrentTenantId` returns the default tenant; a
   no-tenant context throws on `CurrentTenantId` and reports `HasCurrentTenant == false`.
8. **Bootstrap idempotence** — running the bootstrapper on an empty database inserts exactly one default
   `Tenant`; running it again inserts none; the default id is the fixed constant.
9. **Repository exposes no unscoped read** — the tenant-scoped repository returns only ambient-tenant rows;
   there is no member that returns across tenants.
10. **Migration creates the table** — a fresh SQLite database, migrated by `MigrationRunner`, has a
    `Tenants` table with the expected columns; the default tenant seeds; the same migration applies on
    PostgreSQL (networked tier) against the existing Postgres fixture.
11. **Architecture guard (§9 rule 4)** — every `ITenantOwned` type in the built model has a query filter
    (a filterless tenant-owned type fails the build); `IgnoreQueryFilters` appears only in the sanctioned
    seam.

Tests 1–9 and 11 run against a **test-only** tenant-owned entity added to `ZWarden.TestSupport` beside
`Widget`, and against a **two-tenant fixture**, so the convention is proven without waiting on a
production tenant-owned entity (Server/Agent/User arrive in F14/F7/F4).

## Implementation slices

- **S1 — Ownership types + glossary (Domain).** `Tenant`, `ITenantOwned`; the `CONTEXT.md` terms; the
  test-only tenant-owned entity + its configuration in TestSupport. *Verify:* the types compile and the
  entity maps; tests 1's fixture stands up.
- **S2 — The tenant filter + ownership interceptor (Infrastructure).** The third model convention
  (global query filter, fail-closed when no context), `TenantScopeInterceptor`, wired into
  `UseZWardenProvider`. *Verify:* tests 1–6.
- **S3 — `ITenantContext` + single-tenant bootstrap (Application/Infrastructure).** `ITenantContext`,
  `SingleTenantContext`, the fixed default id (ADR 0016), `TenantBootstrapper`, `AddTenantFoundation`.
  *Verify:* tests 7, 8.
- **S4 — Tenant-aware repository (Application/Infrastructure).** `ITenantScopedRepository<T>` + the
  filtered base. *Verify:* test 9.
- **S5 — Isolation tests against the two-tenant fixture.** The offline SQLite two-tenant suite.
  *Verify:* tests 2, 3, 5 end-to-end on a real database.
- **S6 — Architecture guard.** The model-level "every tenant-owned entity has a filter" assertion and the
  `IgnoreQueryFilters` source guard. *Verify:* test 11 (red on an injected filterless entity).
- **S7 — Per-provider migrations + host wiring.** `ZWarden.Migrations.Sqlite` and
  `ZWarden.Migrations.Postgres` (design-time factories + initial `Tenants` migration), `MigrationsAssembly`
  selection, and the migrate-then-bootstrap startup path wired in `Program.cs` behind config (SQLite
  default). *Verify:* test 10 (SQLite offline; Postgres on the networked tier).

## Diagnostics

- **A rejected cross-tenant or scope-changing write** is a single, catchable exception type naming the
  failure category (foreign-tenant insert vs. scope mutation vs. no-ambient-tenant) and **never** another
  tenant's id or row contents — actionable for developers, safe for logs.
- **A fail-closed model build** names the missing `ITenantContext` and the offending entity type, so the
  wiring mistake is obvious at startup rather than as silent cross-tenant reads later.
- **The default tenant is self-describing** — its id is a documented constant, so an operator inspecting a
  self-hosted database can recognise the bootstrap row without a lookup.
- **The isolation guarantee is observable in tests** — a regression that drops a filter or lets a
  cross-tenant write through is a failing build (test 11 / the two-tenant suite), not a data leak found in
  production.

## Documentation

- `CONTRIBUTING.md` — the rule that every tenant-owned entity implements `ITenantOwned` (never a
  hand-written `WHERE TenantId`), that reads go through a tenant-scoped repository, that `IgnoreQueryFilters`
  and raw SQL over tenant-owned tables are off-limits outside the sanctioned seam, and how the default
  tenant/`ITenantContext` resolve in self-hosted mode.
- `CONTEXT.md` — **Tenant-owned**, **Tenant filter**, **Default tenant** (terms only).
- **ADR 0016** records the load-bearing, hard-to-reverse decisions (the query-filter-over-ambient-context
  mechanism with its fail-closed rules and `IgnoreQueryFilters` containment; the fixed default-tenant id).
  This plan records the type placement, the per-provider migrations structure, and the test topology.

## Acceptance criteria

1. A `Tenant` entity persists via the first production migration on **both** providers; the default tenant
   is bootstrapped idempotently under a fixed, well-known id.
2. `ITenantOwned` entities carry an **immutable** tenant scope, auto-stamped from the ambient
   `ITenantContext` on insert.
3. Every tenant-owned read is scoped by an **always-evaluated** query filter; tenant A's context cannot
   read, update, or insert into tenant B — proven against a **two-tenant fixture** on SQLite (offline) and
   PostgreSQL (networked).
4. `ITenantContext` derives the tenant from the session (single-tenant implementation for self-hosted),
   never from a request parameter, and fails closed with no ambient tenant.
5. Tenant-aware repositories expose no unscoped read; `IgnoreQueryFilters` is contained to one sanctioned
   seam and the architecture guard enforces both that and the presence of a filter on every tenant-owned
   entity.
6. The model builds fail closed when a tenant-owned entity has no tenant context; cross-tenant and
   scope-changing writes throw without leaking tenant data.
7. The offline tier is green; the networked tier proves the Postgres migration and isolation; no obsolete
   API or nullable warnings (warnings-as-errors, ADR 0013).

## Definition of Done

Per PRD 61, the applicable subset: acceptance criteria met; tests authored first; unit, model, and
architecture tests green in the offline tier and the cross-provider isolation/migration checks green in
the networked tier; the PRD 7A exit condition holds — **every tenant-owned operation is scoped and
cross-tenant access is denied by default**, enforced by the two-tenant fixture and the §9-rule-4 guard,
not merely intended; error conditions modelled and diagnosable without leaking tenant data; `CONTEXT.md`,
`CONTRIBUTING.md`, and ADR 0016 updated; CI green; no unresolved warnings. (Membership, invitations,
settings, the Auth0-organization mapping, authentication, and RBAC are N/A at F3A — it is the scope
foundation those features build on, proven against its own isolation tests and consumed by F3B/F4/F5+.)
