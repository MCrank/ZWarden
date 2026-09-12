# Feature 5 Mini-Plan — Authorization

**Status:** draft, for review. Roadmap issue: [F5 (#27)](https://github.com/MCrank/ZWarden/issues/27). Track A,
immediately after F4. **Absorbs F3C** (RBAC and tenant authorization were the same feature twice; §3.1.2 of
[scope-and-sequencing.md](../scope-and-sequencing.md)) — `F3C` is a retired id, so this plan delivers the one
merged permission model, never a tenant-vs-server split PRD 12A does not have.

**Format:** PRD 60. **Written against:** PRD §12 (RBAC, policy-based, permission-based, tenant-scoped,
resource-scoped, enforced in ZWarden.Web — *hiding a UI control is not authorization*), §12A (the permission
model, the built-in role catalogue, the **Moderator** default bundle, permission naming, and the conceptual
decision `user + tenant membership + role(s) + permission + target resource + ownership/tenant scope + safety
rules = Allow/Deny`), §2.1 (no invisible stale security parameters), §2.2 (TDD), §7A (tenant derives from the
session, never the browser), §63A (`Auth0 Organization` and `Tenant` non-interchangeable; ZWarden stays
authoritative for permissions even when an external IdP supplies org context); the trust boundaries
([`trust-boundaries.md`](../trust-boundaries.md) §6 Tenant→Tenant, §9 rules 4 and 6, §10 attack 3); and the
foundations F5 builds on — ADR [0016](../adr/0016-tenant-isolation-query-filter-and-default-tenant.md) (the
tenant filter every assignment read passes through), ADR
[0014](../adr/0014-typed-id-pattern.md)/[0004](../adr/0004-typed-ids-are-stored-as-native-uuid.md)
(`RoleId`/`PermissionAssignmentId` already in the registry — F5 adds **no new prefix**), ADR
[0006](../adr/0006-identity-hardening-and-deferred-passkeys.md) / F4 (the `ApplicationUser`/`ApplicationRole`
Identity entities and the session-derived `ClaimsPrincipalTenantContext` this feature reads its principal
from). It writes one new ADR, **0018 (ZWarden-owned RBAC: permission-name policies + resource handlers)**,
for the load-bearing architecture decision.

## Objective

Deliver the control plane's **authorization** decision: one permission model covering tenant scope and
server scope, enforced in ZWarden.Web's application/business logic regardless of UI visibility (PRD 12 exit
condition). Concretely: a closed **permission catalogue** with stable machine-readable names (PRD 12A); a
**role** layer over F4's `ApplicationRole` that bundles permissions, with the built-in role catalogue and the
**Moderator** default bundle seeded; **custom roles** a Tenant Owner/Administrator may create or adjust;
**tenant-scoped role/permission assignments** (optionally narrowed to a single Server) that are tenant-owned
and pass the ADR 0016 filter on every read; a **permission decision service** that resolves
`principal → assignments → roles → permissions` fail-closed for both tenant-wide and server-scoped checks;
the **ASP.NET Core authorization surface** that exposes those decisions as permission-named policies and
resource-based (server-scoped) handlers; and the **applicable-safety-rule seam** the PRD 12A decision names.
Everything ships fail-closed and is proven test-first (PRD 2.2), against the two-tenant fixture the tenant
boundary is exercised with (trust-boundaries §6).

## Dependencies

- **F4 — Identity and Authentication.** F5 authorizes the identity F4 authenticates. It consumes
  `ApplicationUser` (`usr-`, `ITenantOwned`) and `ApplicationRole` (`rol-`, tenant-global — F4 explicitly
  left "F5 owns the permission model that gives them meaning"), and reads its principal and current tenant
  from F4's `ClaimsPrincipalTenantContext` / `TenantClaimsPrincipalFactory`. Being **signed in is not being
  authorized** — every F5 decision is re-made server-side (PRD 12).
- **F3A — Tenant Foundation** (via ADR 0016). Role/permission **assignments** are `ITenantOwned`: the
  ownership interceptor stamps the current tenant on insert and the tenant filter scopes every read, so a
  membership check can never see another tenant's grants. The two-tenant fixture (trust-boundaries §6) is
  the assignment tests' fixture.
- **F2 — Persistence Foundation.** F5 adds its tables beneath `ZWardenDbContext`, extends
  `ConfigurationAssemblies`, and ships **one migration on each provider** (SQLite + PostgreSQL) — the third
  migration on each after F2's baseline and F4's `AspNet*`.
- **F1 — typed IDs.** `RoleId` (`rol-`) and `PermissionAssignmentId` (`prm-`) already exist in the registry;
  F5 consumes them and **adds no prefix**. `ServerId` (`srv-`) already exists and is F5's server-scope key
  even though the Server *entity* is Track D — F5 authorizes against the id, not a navigation.
- **F0** — the offline tier for unit/model/architecture tests, the networked tier for the per-provider
  migration, warnings-as-errors (ADR 0013).

## Scope

1. **Permission catalogue (Domain).** A closed, reflection-enumerable set of permission definitions with the
   **stable machine-readable names** PRD 12A fixes (`Server.View`, `Server.Start`, `Server.Configuration.Edit`,
   `Mod.ApplyApprovedProfile`, `Player.Ban`, `Console.Execute`, `Backup.Restore`, `Agent.Manage`, `Tenant.Manage`,
   `User.Manage`, `Role.Manage`, `Audit.View`, `Diagnostics.Export`, … the full §12A list). Each definition
   records its **scope kind** — *tenant-wide* vs *server-scopable* — so the decision layer knows whether a
   `ServerId` is meaningful. Names are the wire/DB/policy currency; the catalogue is the single source of truth
   and a test asserts it matches the PRD list exactly (the invisible-stale-set failure PRD 2.1 exists to prevent).
   Pure Domain — no EF, no ASP.NET Core.
2. **Role → permission bundling (Domain concept, Infrastructure persistence).** A **role-permission** mapping
   over F4's `ApplicationRole`: a role names a set of permission definitions. Built-in roles seed their bundle;
   custom roles carry their own. Roles remain keyed by `RoleId`; the mapping table is EF-configured in
   Infrastructure.
3. **Built-in role catalogue + the Moderator default bundle (PRD 12A).** Seed the v1.0-relevant built-in
   roles — **Tenant Owner, Administrator, Operator, Moderator, Viewer, Support/Diagnostics** — each with its
   default permission bundle. **Platform Owner is SaaS-only** and stays out (v1.1, noted). The **Moderator**
   bundle is seeded exactly to PRD 12A: it **includes** `Server.View`, `Server.Health.View`, `Server.Log.View`,
   `Player.View/Kick/Ban/Unban`, `Server.Restart`, `Mod.View`, `Mod.ApplyApprovedProfile`, `Mod.UpdateApproved`,
   and **excludes** `Server.Stop`, `Server.Delete`, `Server.Configuration.Edit`, `Mod.Install*`/`Remove*`,
   `Backup.Restore`, `Console.Execute`, `Agent.Manage`, `User.Manage`, `Role.Manage`, `Tenant.Manage` — asserted
   both ways by test. Seeding is idempotent and runs beside F4's default-tenant/first-admin bootstrap.
4. **Custom roles (PRD 12A).** A Tenant Owner or Administrator may **create a custom role or adjust a bundle**,
   subject to audit later (F6). Custom roles are **tenant-owned** (a tenant authors them); built-in roles are
   catalogue-global and not editable into a security hole (the built-in `Role.Manage`/`Tenant.Manage` exclusions
   on Moderator cannot be granted around by editing Moderator itself without holding `Role.Manage`). The
   create/adjust path is an Application service, permission-gated by `Role.Manage`.
5. **Tenant-scoped role/permission assignments (Infrastructure, `prm-`, `ITenantOwned`).** The entity that
   binds **(user, role, tenant, optional `ServerId`)**, keyed by `PermissionAssignmentId`. An assignment with no
   `ServerId` is tenant-wide; one with a `ServerId` narrows the role's server-scopable permissions to that Server.
   It is `ITenantOwned`, so ADR 0016 stamps and filters it — a **tenant-scoped membership check** is exactly a
   filtered read of this table. EF config + a tenant-filtered repository; no unscoped read exists (trust-boundaries
   §9 rule 4).
6. **Permission decision service (Application seam + Infrastructure resolver).** `IPermissionChecker` (or the
   shape ADR 0018 fixes): *does this principal hold permission P (optionally on Server S) in the current
   tenant?* It resolves principal → tenant-scoped assignments → roles → permissions, honours the tenant-wide vs
   server-scoped distinction (a tenant-wide grant satisfies a server-scoped check; a grant scoped to Server X
   does **not** satisfy a check on Server Y), and **fails closed**: no matching grant ⇒ deny; no authenticated
   tenant ⇒ deny (never `Allow` on a missing scope). Application-level so business logic checks permissions
   without an ASP.NET Core dependency.
7. **ASP.NET Core authorization surface (Infrastructure/Web).** A **`PermissionRequirement`** carrying a
   permission name; a **`PermissionAuthorizationHandler`** backed by `IPermissionChecker`; and a **dynamic
   `IAuthorizationPolicyProvider`** that turns any catalogue permission name into a policy on demand (so
   `[Authorize(Policy = "Server.Start")]` / `AuthorizeView` work without hand-registering hundreds of policies).
   This is the enforcement surface PRD 12's exit condition names — enforced in application/business logic, **not**
   UI visibility.
8. **Resource-based (server-scoped) authorization (Infrastructure/Web).** A resource handler
   `AuthorizationHandler<PermissionRequirement, IServerScoped>` so a check against a specific Server is
   `ServerId`-aware end to end, matching PRD 12A's "target resource + ownership/tenant scope" term.
   Server-scopable permissions evaluated without a resource fail closed rather than silently widening.
   *(As built: business logic gets the same server-scoped check directly from
   `IPermissionChecker.EvaluateAsync(user, permission, serverId)`, so no separate `IResourceAuthorizationService`
   was added — see ADR 0018.)*
9. **Applicable-safety-rule seam (PRD 12A).** The decision "must include … applicable safety rules": an
   `IAuthorizationSafetyRule` evaluated **last**, able only to **deny** an otherwise-allowed decision (a rule
   never grants). v1.0 ships the seam and a no-op default; concrete high-risk safeguards (confirmation prompts,
   cooldowns, maintenance windows, two-person approval, step-up auth, time-limited grants — PRD 12A) bind to
   the operations that own them in later features. Modelled now so the decision shape is complete and testable.
10. **Per-provider migration.** The role-permission and assignment tables as the third migration in
    `ZWarden.Migrations.Sqlite` and `ZWarden.Migrations.Postgres`, with the typed-id column types (native uuid,
    ADR 0004) and the `TenantId` column on the assignment table.
11. **Host wiring + architecture guards.** `Program.cs` gains `AddZWardenAuthorization()` (the checker, the
    handlers, the policy provider, the safety-rule seam) after `AddZWardenAuthentication`, and the built-in-role
    seed folds into the existing `MigrateAndBootstrapDefaultTenant` bootstrap. Guards: §9 rule 6 stays green (no
    IdP RBAC type in Domain/Application — Auth0's RBAC is deliberately not the store, PRD 63A); the assignment
    entity is `ITenantOwned` (a grant with no tenant scope is a red build); the permission catalogue matches the
    PRD list; and no assignment repository exposes an unscoped read (§9 rule 4).

## Non-scope

- **UI-level visibility rules** — out (PRD 12, §12A: hiding a control is never authorization). F5 exposes
  `AuthorizeView`-compatible policies, but *whether a button renders* is a Track E concern; the **decision** is
  always re-made server-side.
- **Anything that assumes an external IdP's RBAC** — out (issue #27; PRD 63A). Auth0 roles/org membership are at
  most *inputs* in v1.1 (F3B); ZWarden stays the authoritative permission store, and no IdP RBAC SDK enters
  Domain/Application (§9 rule 6). Auth0 RBAC isn't even on the Free tier — another reason the store is ours.
- **Enforcement at the privileged Agent command boundary** — F5 authorizes *inside ZWarden.Web*. PRD 12's
  "enforced again at privileged Agent command boundaries" is the Agent plane (F9/F10); F5 provides the permission
  model those consume, it does not build the Agent-side check.
- **The durable audit of authorization decisions and role/assignment changes** — F6 (`aud-`). F5's create/adjust
  and grant/revoke paths are shaped so F6 can bind a sink without touching them; F5 does not build the store.
- **Concrete high-risk safeguards** (two-person approval, cooldowns, maintenance windows, step-up auth,
  time-limited grants) — later, bound to the operations that need them. F5 ships the deny-only seam only.
- **Tenant membership *management*, invitations** — F3D (v1.1). F5 does the tenant-scoped membership *check*
  (a filtered assignment read); it does not build the admin surface for managing members.
- **Server-scoped grants against a real Server entity/navigation** — the Server entity is Track D. F5 authorizes
  against `ServerId` (already in the registry); the FK/navigation lands with the Server feature.

## Domain changes

- **`Permission` catalogue (Domain).** A closed set of permission definitions (name + scope kind), pure and
  reflection-enumerable; the single source of truth for permission names. No new typed-id prefix.
- **Role-permission mapping + role/permission assignment (Infrastructure entities).** Framework-coupled (they
  hang off Identity's `ApplicationRole` and are EF-mapped), so — like F4's Identity entities — they live in
  `ZWarden.Infrastructure`, not Domain. The **assignment** is `ITenantOwned` (`prm-`); it binds
  (user, role, tenant, optional `ServerId`), init-only tenant scope (F3A ownership rule).
- **`IPermissionChecker` / `IResourceAuthorizationService` / `IAuthorizationSafetyRule` (Application seams).**
  Reference only Domain (typed IDs, the permission catalogue) — no ASP.NET Core, no IdP SDK (§9 rule 6). Concrete
  resolver, the ASP.NET Core requirement/handlers/policy provider, and the EF entities are Infrastructure/Web.
- **Glossary (`CONTEXT.md`), terms only:** **Permission** (a named, stable capability; tenant-wide or
  server-scopable), **Role** (a manageable permission bundle; built-in or custom), **Permission assignment**
  (the tenant-owned binding of a user to a role, optionally narrowed to one Server — the `prm-` record), and
  **Authorization decision** (the fail-closed evaluation of principal + tenant + role(s) + permission + target
  resource + safety rules). Keep them distinct from **Authentication event** and **Session** (F4).

## Contract changes

- **`IPermissionChecker`** — the one way business logic asks whether a principal holds a permission (optionally
  on a Server) in the current tenant; fail-closed by contract (deny on no grant / no tenant).
- **The permission catalogue** — a fixed, versioned set of names; adding or renaming a permission is a
  catalogue+test change (and, for a new capability area, an ADR), never an ad-hoc string.
- **ASP.NET Core policies** — every catalogue permission name resolves to a policy via the dynamic provider;
  `[Authorize(Policy = "…")]` and `AuthorizeView(Policy = "…")` are the Web-surface contract.
- **Persistence** — two new tables (role-permission, assignment); the assignment table is tenant-scoped and
  ships on both providers.
- No Agent/SignalR/serialization contract changes (F5 is ZWarden.Web-internal; the Agent-boundary check is F9/F10).

## Security considerations

- **Fail closed, everywhere.** No matching grant ⇒ deny; no authenticated tenant ⇒ deny; a server-scopable
  permission checked with no resource ⇒ deny (never widen); the safety-rule seam can only *subtract* an allow.
  A grant scoped to Server X never satisfies a check on Server Y. Asserted by test, not intended.
- **The browser never selects scope (PRD 7A, trust-boundaries §6/§10 attack 3).** The tenant comes from F4's
  session claim via `ITenantContext`; assignments are read through the ADR 0016 filter; the `ServerId` in a
  resource check is the resource's own id, never a request-supplied tenant/role hint. A request that supplies
  its own tenant or role has no path to widen the decision.
- **ZWarden is the authoritative permission store (PRD 63A, §9 rule 6).** No Auth0/Okta RBAC type in
  Domain/Application; the architecture test that already forbids IdP packages there stays green. A hosted IdP is
  at most an *input* in v1.1, never the decision.
- **Privilege-escalation containment.** Editing the Moderator bundle or authoring a custom role requires
  `Role.Manage`; a role cannot be edited to grant a permission the editor does not themselves hold (no
  bootstrap-yourself-to-owner). Built-in exclusions are asserted so a seed regression can't silently hand
  Moderator `Console.Execute` or `Role.Manage`.
- **Tenant isolation is exercised, not assumed (trust-boundaries §6).** Assignment tests run against the
  two-tenant fixture: tenant B's context resolves none of tenant A's grants; a cross-tenant assignment read is
  impossible through the repository.
- **No secret material in the decision path.** Permissions and role names are not secrets, but the decision
  logs (for F6) carry principal + permission + resource + allow/deny only — never credentials.

## Test plan

Written before the code (PRD 2.2). Offline tier except the per-provider migration (networked). Assignment/
isolation tests use the two-tenant fixture (trust-boundaries §6).

1. **Catalogue matches the PRD** — the permission catalogue's names equal the PRD 12A list exactly (missing or
   extra name fails); each carries a scope kind (tenant-wide vs server-scopable).
2. **Built-in roles seed idempotently** — seeding twice yields one of each built-in role with a stable bundle;
   Platform Owner is absent (SaaS-only).
3. **Moderator default bundle (both directions)** — the seeded Moderator **includes** each PRD 12A "typical"
   permission and **excludes** each "shall not include" permission.
4. **Assignment is tenant-owned & isolated** — an assignment created under tenant A carries A's immutable
   `TenantId`; tenant B's context resolves it in no membership check; changing its `TenantId` throws (F3A
   interceptor); the repository exposes no unscoped read (§9 rule 4).
5. **Tenant-wide decision** — a principal assigned a role granting `Server.View` is allowed `Server.View`
   tenant-wide and denied a permission the role lacks (`Console.Execute`).
6. **Server-scoped decision** — a grant scoped to Server X allows a server-scopable permission on X, **denies**
   it on Y; a tenant-wide grant allows it on both; a server-scopable permission checked with no resource fails
   closed.
7. **Fail-closed principal/tenant** — an unauthenticated principal (no tenant claim) is denied every permission;
   a principal with no assignments is denied.
8. **Custom role create/adjust is permission-gated** — a principal without `Role.Manage` cannot create or edit a
   role; with it, a custom tenant-owned role is created and its bundle takes effect; a role cannot be edited to
   grant a permission the editor lacks.
9. **Dynamic policy provider** — any catalogue permission name resolves to a policy; `[Authorize(Policy=name)]`
   admits a holder and rejects a non-holder; an unknown policy name is not silently allowed.
10. **Resource handler** — the `IServerScoped` resource handler evaluates a specific `ServerId`, agreeing with
    test 6 through the ASP.NET Core pipeline.
11. **Safety-rule seam** — a deny-only rule turns an otherwise-allowed decision into a deny; a rule cannot turn a
    deny into an allow; the default no-op leaves decisions unchanged.
12. **Per-provider migration** — a fresh SQLite database migrated by `MigrationRunner` has the role-permission and
    assignment tables with typed-id column types and the assignment `TenantId` column; the same migration applies
    on PostgreSQL (networked tier).
13. **Host wiring smoke** — the composed host resolves `IPermissionChecker`, the policy provider, and the handlers;
    migrate-then-bootstrap seeds the built-in roles idempotently beside the default tenant/first admin.
14. **Architecture guards** — Domain/Application reference no IdP RBAC package (§9 rule 6 green); the assignment
    entity is `ITenantOwned`; the catalogue-matches-PRD guard (test 1) is wired as an architecture/model test.

## Implementation slices

Sliced so each is independently green and TDD-first. **F5 is the largest feature in Track A; its scope is
expected to break the PRD 60 ~100K guardrail.** Split by *slice* at the marked seam — **PR 1 = definitions and
the decision engine (S1–S5), PR 2 = the enforcement surface (S6–S10)** — never by inventing a tenant/server
boundary PRD 12A does not have (scope-and-sequencing §6, F5 note). Each PR is independently green: PR 1 ends
with a working, tested decision service with no ASP.NET Core surface; PR 2 wires it into the pipeline and the host.

**PR 1 — definitions + decision engine**
- **S1 — Permission catalogue (Domain).** Closed definitions + names + scope kind. *Verify:* test 1.
- **S2 — Role→permission mapping + built-in catalogue + Moderator bundle (Infrastructure).** Mapping table,
  seeded built-in roles, exact Moderator bundle. *Verify:* tests 2, 3.
- **S3 — Assignment entity + tenant-filtered repository (Infrastructure; `prm-`, `ITenantOwned`).** EF config,
  (user, role, tenant, optional `ServerId`), no unscoped read. *Verify:* test 4.
- **S4 — Permission decision service (Application seam + Infrastructure resolver).** `IPermissionChecker`,
  tenant-wide vs server-scoped resolution, fail-closed. *Verify:* tests 5, 6, 7.
- **S5 — Custom roles + safety-rule seam (Application).** `Role.Manage`-gated create/adjust, no-escalation rule,
  `IAuthorizationSafetyRule` deny-only. *Verify:* tests 8, 11. *(Guardrail split point → PR 2.)*

**PR 2 — enforcement surface**
- **S6 — `PermissionRequirement` + handler + dynamic policy provider (Infrastructure/Web).** *Verify:* test 9.
- **S7 — Resource-based server-scoped handler (Infrastructure/Web).** `ServerScopedPermissionHandler` over an
  `IServerScoped` resource; business logic uses `IPermissionChecker.EvaluateAsync` with a `ServerId` directly
  (no separate `IResourceAuthorizationService`, ADR 0018). *Verify:* test 10.
- **S8 — Per-provider migration.** Role-permission + assignment tables on SQLite and Postgres. *Verify:* test 12
  (SQLite offline; Postgres networked).
- **S9 — Host wiring.** `AddZWardenAuthorization`, seed built-in roles in the bootstrap, composition order after
  `AddZWardenAuthentication`. *Verify:* test 13.
- **S10 — Architecture guards + docs.** §9-rule-6 classification, `ITenantOwned` assignment guard,
  catalogue-matches-PRD guard, ADR 0018 / `CONTEXT.md` / `CONTRIBUTING.md`. *Verify:* test 14.

## Diagnostics

- **A denied decision is legible, not silent.** The checker returns a decision that names the missing permission
  (and Server, when server-scoped) at the point of use, so a wiring or grant mistake surfaces as a caught,
  explainable deny — never a silent cross-tenant or cross-server allow, and never echoing a request-supplied hint.
- **Fail-closed is observable.** No tenant claim, no assignment, or a server-scopable permission with no resource
  each produce a distinct, testable deny reason rather than a bare `false`.
- **The catalogue is self-describing.** The permission set is one enumerable source of truth checked against the
  PRD by test, so an operator or the F40 gate can read the enforced permissions without source archaeology, and a
  drift from the PRD list is a red build (the invisible-stale-set failure PRD 2.1 guards against).
- **Seed integrity is asserted.** The built-in bundles (Moderator especially) are asserted include-and-exclude,
  so a seed regression that widens a role is a failing test, not a production privilege.

## Documentation

- **ADR 0018 (new, written with this plan)** — *ZWarden-owned RBAC: permission-name policies + resource
  handlers.* Records: the permission catalogue as the closed source of truth; roles-bundle-permissions over F4's
  `ApplicationRole`; assignments are tenant-owned (`prm-`) and optionally server-scoped; the decision is a
  fail-closed Application service surfaced to ASP.NET Core via a dynamic policy provider + resource handlers; the
  safety-rule seam is deny-only; and **why ZWarden stays the authoritative store rather than an IdP's RBAC**
  (PRD 63A, §9 rule 6). Flags the one real design call for review: **assignments carry role + optional
  `ServerId`** rather than direct user→permission grants.
- **`CONTEXT.md`** — **Permission**, **Role**, **Permission assignment**, **Authorization decision** (terms only).
- **`CONTRIBUTING.md`** — authorization is always re-decided server-side (hiding a control is not authorization);
  permission names come from the catalogue, never ad-hoc strings; assignments are tenant-owned and read through
  the filter; no IdP RBAC type above Infrastructure; fail-closed is the default and is tested.

## Acceptance criteria

1. A closed **permission catalogue** with the PRD 12A names (scope kind per permission) is the single source of
   truth, asserted equal to the PRD list.
2. **Roles bundle permissions** over F4's `ApplicationRole`; the built-in catalogue seeds idempotently and the
   **Moderator** default bundle matches PRD 12A include-and-exclude; **custom roles** can be created/adjusted,
   permission-gated by `Role.Manage` with no self-escalation.
3. **Assignments** (`prm-`) bind (user, role, tenant, optional `ServerId`), are `ITenantOwned`, and are read only
   through the ADR 0016 filter; a tenant-scoped membership check sees no other tenant's grants (two-tenant fixture).
4. The **decision** is fail-closed and honours tenant-wide vs server-scoped: a grant scoped to one Server never
   authorizes another; no grant / no tenant / server-scopable-without-resource all deny.
5. The decision is enforced in ZWarden.Web through **permission-named policies** (dynamic provider) and a
   **resource-based server-scoped handler**, and is available to business logic via `IPermissionChecker` — **UI
   visibility is never the enforcement** (PRD 12 exit condition).
6. The **safety-rule seam** is present and deny-only; the v1.0 default is no-op; concrete safeguards are deferred.
7. **No IdP RBAC dependency** in Domain/Application (§9 rule 6 green); ZWarden is the authoritative store (PRD 63A).
8. `Program.cs` wires `AddZWardenAuthorization` after authentication and seeds built-in roles in the bootstrap;
   the migration ships on **both** providers.
9. The offline tier is green; the networked tier proves the per-provider migration; no obsolete-API or nullable
   warnings (warnings-as-errors, ADR 0013).

## Definition of Done

Per PRD 61, the applicable subset: acceptance criteria met; tests authored first; unit, model, and architecture
tests green in the offline tier and the per-provider migration green in the networked tier; the **PRD 12 exit
condition holds — permissions are enforced in ZWarden.Web regardless of UI visibility** — enforced by tests, not
merely intended; authorization is fail-closed and tenant/server scoping is proven against the two-tenant fixture;
error conditions modelled and diagnosable without leaking request-supplied hints; ADR 0018, `CONTEXT.md`, and
`CONTRIBUTING.md` updated; CI green; no unresolved warnings. Delivered as **two PRs** (S1–S5 decision engine, then
S6–S10 enforcement surface), each independently green. (The durable audit of decisions/role changes is F6; the
Agent-boundary re-check is F9/F10; concrete high-risk safeguards, tenant membership management, external-IdP RBAC,
and UI visibility rules are N/A at F5 — owned by later features, v1.1, and Track E respectively.)
