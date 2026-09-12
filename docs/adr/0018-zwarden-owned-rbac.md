# 18. Authorization is ZWarden-owned RBAC: a permission catalogue, tenant-owned roles, and a fail-closed decision service

**ZWarden is the authoritative authorization store.** F5 delivers a closed **permission catalogue**, a
tenant-owned **Role** aggregate that bundles permissions, tenant-owned **role assignments** (`prm-`) that
bind a user to a role and optionally narrow it to one Server, and a **fail-closed decision service** that
resolves a principal's permissions and is surfaced to ASP.NET Core as permission-named policies and
resource handlers. No external identity provider's RBAC is the store — an IdP is at most an input in a
future release (PRD 63A). Every decision is made server-side; hiding a UI control is never authorization
(PRD 12).

- Status: accepted
- Decided in: [#27 (F5 — Authorization)](https://github.com/MCrank/ZWarden/issues/27); the F5 mini-plan
  (`docs/feature-plans/F5-authorization.md`); absorbs the retired F3C (scope-and-sequencing §3.1.2).
- Bears on: PRD 12 / 12A (RBAC, policy- and permission-based, tenant- and resource-scoped, enforced in
  ZWarden.Web), PRD 63A (ZWarden authoritative for permissions even under an external IdP), ADR
  [0016](./0016-tenant-isolation-query-filter-and-default-tenant.md) (the tenant filter every assignment
  read passes through), ADR [0014](./0014-typed-id-pattern.md) (`RoleId`/`PermissionAssignmentId`), and
  ADR [0006](./0006-identity-hardening-and-deferred-passkeys.md)/F4 (the Identity `ApplicationRole` this
  deliberately does *not* reuse for the RBAC model).

## Context

PRD 12A requires role-based access control combined with fine-grained, policy-based, resource-scoped
permissions, and states the decision must evaluate `authenticated user + tenant membership + role(s) +
permission + target resource + ownership/tenant scope + safety rules`. It also requires custom roles a
Tenant Owner or Administrator may author. Two forces shaped the design:

- **Custom roles must be isolated per tenant.** F4's Identity `ApplicationRole` (`AspNetRoles`) carries a
  *global* unique index on the role name, so two tenants could not both hold an "Ops" role. Reusing it for
  the RBAC model would mean fighting Identity's store (replacing its unique index, making `RoleManager`
  tenant-aware). F4 anticipated this, leaving `ApplicationRole` a coarse identity-claim role and handing
  the permission model to F5.
- **Auth0's RBAC is not the store.** It is not on the Free tier (PRD 7A), and coupling the decision to one
  IdP is exactly what PRD 63A forbids. ZWarden owns permissions; an IdP is at most an input in v1.1.

## Decision

- **Permission catalogue (Domain).** A closed set of `PermissionDefinition` (stable dotted name + a scope
  kind), the single source of truth for the PRD 12A permission names. A test asserts it equals the PRD
  naming list exactly. Scope kind: the per-server families `Server.*`, `Mod.*`, `Player.*`, `Console.*`,
  `Backup.*` are **server-scopable**; host/tenant permissions (`Agent.*`, `Tenant.*`, `User.Manage`,
  `Role.Manage`, `Audit.View`, `Diagnostics.*`) are **tenant-wide**.
- **F5 owns roles.** A tenant-owned `Role` (`rol-`, `ITenantOwned`) bundles permissions
  (`RolePermissionGrant`), **separate from** Identity's `ApplicationRole`. Role names are unique **per
  tenant**. Built-in roles carry a `BuiltInRoleKind` and are seeded per tenant from `BuiltInRoles`; custom
  roles carry none. **Platform Owner is SaaS-only and is not a v1.0 built-in.** The **Moderator** bundle is
  seeded exactly to PRD 12A (asserted include-and-exclude).
- **Assignments (`prm-`, `ITenantOwned`).** `RoleAssignment` binds `(user, role, tenant, optional
  ServerId)`. A null `ServerId` is tenant-wide; a set `ServerId` narrows the role's server-scopable
  permissions to that one Server. Grants go **through roles** — there is no direct user→permission grant.
  Neither `RoleId` nor `ServerId` carries a relational FK (the Server entity is Track D; the resolver fails
  closed on a dangling role), so role deletion removes assignments explicitly.
- **Fail-closed decision service.** `IPermissionChecker` (Application seam, Domain-only) →
  `PermissionChecker` (Infrastructure) resolves principal → tenant-scoped assignments → roles → grants,
  joined **through the tenant-filtered `Role` set** so a cross-tenant role can never leak. Semantics: a
  tenant-wide assignment confers the role's permissions tenant-wide; a server-scoped assignment confers
  only server-scopable permissions and only on that Server; a tenant-wide grant covers every Server; a
  server-scopable permission checked with no Server, an unauthenticated principal, and a user with no grant
  all **deny**.
- **No self-escalation.** `RoleAdministrationService` requires the actor to hold `Role.Manage` tenant-wide,
  and an actor may only *add* to a role a permission the actor itself holds tenant-wide (removals are free).
  Built-in roles may be adjusted (PRD 12A) but not deleted.
- **Deny-only safety seam.** `IAuthorizationSafetyRule` is consulted only after a grant is established and
  can veto but never grant (the PRD 12A "applicable safety rules" term). v1.0 registers no concrete rules;
  high-risk safeguards (confirmation, cooldowns, two-person approval, step-up, time-limited grants) bind to
  the operations that own them later.
- **Enforcement surface (PR 2).** Every catalogue permission name resolves to an ASP.NET Core policy via a
  dynamic policy provider, with a resource handler for server-scoped checks, all backed by
  `IPermissionChecker`. This ADR fixes the shape; the enforcement slice wires it into ZWarden.Web.

## Alternatives considered

- **Reuse Identity's `ApplicationRole` for the RBAC model.** Rejected: its global unique-name index blocks
  per-tenant custom roles, and making `RoleManager` tenant-aware means fighting Identity's store. F5's
  tenant-owned `Role` isolates cleanly; the cost knowingly accepted is two role notions (a coarse Identity
  claim-role and F5's authoritative `Role`).
- **Direct user→permission grants (no roles).** Rejected: PRD 12A wants roles as the manageable bundle;
  per-user permission sprawl has no bundle to manage or audit.
- **A relational FK from assignment to role (and to Server).** Rejected for v1.0: the Server entity does
  not exist yet (Track D), and a dangling role is already handled fail-closed by the resolver; symmetry and
  simpler test setup win. Referential cleanup on role deletion is explicit.
- **An external IdP's RBAC as the store.** Rejected on PRD 63A and the Auth0 Free-tier constraint.

## Consequences

- ZWarden is the single authorization authority; a v1.1 IdP integration feeds inputs, never replaces the
  store. The `§9 rule 6` architecture test (no IdP SDK in Domain/Application) stays green.
- Two role concepts coexist (Identity `ApplicationRole` vs F5 `Role`). This is a knowingly accepted cost;
  the decision service reads **only** F5's model, never Identity role claims, so there is one source of
  authorization truth even though there are two "role" types.
- Adding, renaming, or removing a permission is a deliberate catalogue-and-test change (and, for a new
  capability area, an ADR) — never an ad-hoc string.
- The non-Moderator built-in bundles (Administrator, Operator, Support/Diagnostics) are derived from the
  PRD's one-line descriptions and are a reviewed first cut; Moderator, Viewer, and Tenant Owner are
  unambiguous. Bundles are asserted by test so a seed regression that widens a role is a red build.
- Moderator reconciliation: PRD 12A's illustrative `Server.Health.View`/`Server.Log.View` fold into
  `Server.View`, `Mod.UpdateApproved` maps to `Mod.Update`, and `Server.Delete`/`Mod.*Arbitrary` are not
  v1.0 catalogue permissions.
