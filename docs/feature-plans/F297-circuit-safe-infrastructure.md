# Feature #297 Mini-Plan — Circuit-safe infrastructure: fail-closed tenant, a DbContext per action, auth revalidation

**Status:** three PRs, in order. v1.0, epic [#294](https://github.com/MCrank/ZWarden/issues/294).

- **PR-A** (branch `feat/297-fail-closed-tenant`, PR #303, merged): the fail-closed tenant, named default-tenant grants, the Agent
  tenant claim, the circuit tenant capture, tenant-carrying scopes, and arch tests.
- **PR-B** (branch `feat/297-dbcontext-per-action`): `IDbContextFactory` registration, a scope-per-action runner for interactive components, and the
  32-concurrent-operations test.
- **PR-C** (closes #297): auth revalidation about every minute (user exists, security stamp matches, still holds a
  role), plus a stamp bump when a role assignment is removed.

**Written against:** issue #297;
[ADR 0046](../adr/0046-pages-may-opt-into-interactive-rendering-the-shell-stays-static.md) (Q3/Q6/Q7),
[ADR 0016](../adr/0016-tenant-isolation-query-filter-and-default-tenant.md) (ambient tenant + filter),
[ADR 0018](../adr/0018-zwarden-owned-rbac.md), [ADR 0007](../adr/0007-agent-authentication-enrollment-credential-in-v1-0.md)
(Agent bearer credential), #154 (DbContext concurrency in a circuit), `docs/trust-boundaries.md` §2/§6.

## Objective

Before any page goes interactive (#299), tenant-scoped work must never silently land on the default tenant, and
each action from an interactive page must get its own `DbContext`.

## Facts found (2026-10-01)

- `ClaimsPrincipalTenantContext` returns `Tenant.DefaultId` whenever there's no `HttpContext` or no claim.
- That fallback is load-bearing in more places than circuits:
  - **startup:** `MigrateAndBootstrapDefaultTenantAsync`, `AdminBootstrapper`, `AuthorizationBootstrapper`,
    `SetupBootstrapper`, `DevEnrollmentBootstrapper`;
  - **background:** `OperationReaperService`, `AgentConnectionSweeperService`;
  - **the Agent path:** the Agent principal carries only `zwarden:agent`, so hub calls, the connection-state
    writer and enrollment exchange all depend on the fallback;
  - **anonymous HTTP:** login, setup and `/healthz`.
- 37 classes take a `ZWardenDbContext` in their constructor, and `UserManager`'s store uses the scoped context.
  Server Detail injects about 22 services built on them.
- No code ever changes a user's `SecurityStamp`. Removing a role assignment doesn't touch it.

## Maintainer decisions (2026-10-01, all as recommended)

| # | Decision |
|---|---|
| D1 | **Named grants.** A signed-in principal with a missing or bad tenant claim throws. A circuit with no captured tenant throws. A scope with no `HttpContext` and no assignment throws. The default tenant is reached only through explicit paths: (a) startup and background code open a **system scope** for the install's default tenant; (b) an **anonymous HTTP request** belongs to the install's tenant (F3B replaces this with host-based lookup); (c) the Agent auth handler **stamps the tenant claim** the credential was verified under, and a hub filter assigns it on every Agent hub call. |
| D2 | **A scope per action, not a 37-class rewrite.** `IDbContextFactory<ZWardenDbContext>` is registered scoped (it resolves the scope's tenant), and the scoped context is built from it, so static pages don't change. Interactive components run each service call in a fresh DI scope that carries the circuit's tenant, which covers `UserManager` too. ADR 0046 gets an amendment note. |
| D3 | **Three PRs** as above. |
| D4 | **Revalidate stamp + role.** The user still exists, the stamp matches, and the user holds at least one role assignment. Removing an assignment also bumps the user's stamp. |

## PR-A design

- **`TenantAssignment`** (Infrastructure, scoped): the tenant explicitly given to this DI scope. It can be
  assigned once (re-assigning the same tenant is a no-op, a different one throws). It can also be marked as a
  circuit, which means "never consult `HttpContext`".
- **`ClaimsPrincipalTenantContext`** resolves in this order:
  1. an assigned tenant;
  2. a circuit without one, which throws;
  3. no `HttpContext`, which throws;
  4. an authenticated principal: its `zwarden:tenant` claim must parse, otherwise it throws;
  5. an anonymous request: the install's default tenant (the named anonymous rule).

  `HasCurrentTenant` reports whether that resolves without throwing.
- **`TenantScopes`** (Infrastructure): the only place in `src` that creates DI scopes.
  - `CreateTenantScope(TenantId)` opens a scope with that tenant assigned. `ScopedPermissionChecker` uses it to
    pass the caller's tenant on.
  - `CreateSystemScope()` opens a scope for the default tenant, for startup and background code.
  - It has overloads for `IServiceScopeFactory` and `IServiceProvider`.
- **Agent:**
  - `AgentAuthenticationHandler` adds `zwarden:tenant` with the tenant it verified the credential under (the
    anonymous rule, so the default tenant today).
  - `AgentTenantHubFilter`, registered on `AgentHub` only, assigns that claim into each invocation and
    lifecycle scope, and throws if it's absent.
- **Circuit:** `TenantCircuitHandler` (scoped `CircuitHandler`) marks the scope as a circuit in
  `OnCircuitOpenedAsync`. If the circuit's user carries a valid tenant claim, it assigns it. Circuit handlers
  run before root components render.
- **Arch tests** (source-text scans, like `TenantFilterGuardTests`):
  - `Tenant.DefaultId` appears only in `Tenant`, `SingleTenantContext`, `TenantBootstrapper`,
    `ClaimsPrincipalTenantContext` (the anonymous rule) and `TenantScopes` (the system scope);
  - `.CreateScope(` and `.CreateAsyncScope(` appear only in `TenantScopes`.
- **Tests:**
  - every branch of the resolution order;
  - the Agent claim stamp and the hub filter;
  - the circuit handler assigns, or marks and throws;
  - `CreateTenantScope` carries the tenant;
  - system scopes resolve the default tenant;
  - existing tests that build scopes off a session-tenant host move to `CreateSystemScope()`.

## Out of scope

- Multi-tenant iteration in background services. That's F3B: a system scope is single-tenant by name.
- Making any page interactive (#299).

## PR-B design

- **`ZWardenDbContextFactory`** (scoped `IDbContextFactory<ZWardenDbContext>`) binds the scope's `ITenantContext`
  and the optional secret protector. The scoped `ZWardenDbContext` is now `factory.CreateDbContext()`, so static
  pages and all 37 constructor consumers are unchanged.
- **`ActionScopeRunner`** (scoped, registered by both tenant foundations):
  `RunAsync<TService[, TResult]>((service, ct) => …)` opens `CreateTenantScope(current tenant)`, resolves the
  service there and disposes the scope afterwards. Interactive components (#299) call services through it.
- **Arch test:** nothing under `ZWarden.Web/Components` references `ZWardenDbContext` or `IDbContextFactory`.
- **Tests:**
  - 32 concurrent actions in one circuit-marked scope (alternating `UserManager.FindByEmailAsync` and raw
    context reads) all succeed on 16 distinct contexts;
  - an action from a circuit sees only its tenant's servers, and one from an untenanted circuit throws;
  - the factory and the scoped context bind the same scope tenant.
- ADR 0046 gets a Q7 amendment note.
- Today's `Live*` islands inject only singleton caches, so nothing needed migrating yet.
