# 36. First-run setup is a singleton install-state record behind a redirect gate

**A fresh ZWarden.Web install with no administrator is unusable until it is set up, and it must not be
reachable before then.** F33 records setup progress on a single, non-tenant-owned **`InstallState`** row
(`ist-`, well-known id) holding `SetupCompletedAt` and the declared `TlsMode`. A **first-run gate**
middleware redirects every browser navigation to the `/setup` wizard until setup is complete; the wizard is a
sequence of **static-SSR pages**, not an interactive component. Setup is **complete once the first
administrator is created and a TLS mode is confirmed** — Agent enrollment, server discovery/registration, and
health checks are guided-but-skippable. ZWarden **records** the TLS mode; it does not terminate TLS (ADR 0035).

- Status: accepted
- Decided in: #52 (F33 — First-Run Setup); mini-plan `docs/feature-plans/F33-first-run-setup.md`
- Bears on: PRD §11 criterion 1 (deploy with minimal setup, satisfied by F32+F33+F34); ADR 0016 (default
  tenant / sanctioned unscoped reads), ADR 0018 (ZWarden-owned RBAC — first admin gets Tenant Owner), ADR
  0035 (Caddy owns TLS termination). Composes the F5/F9/F14/F29 seams without adding domain logic to them.

## Context

Before F33 the only way to get a usable install was to set `ZWarden:Admin:Email` / `ZWarden:Admin:Password`
in configuration and restart; there was no browser path to a first administrator, no persisted notion of
"is this install set up", and no "is there any administrator yet" query anywhere. A fresh install therefore
presented a login page an operator could not get past. The dependencies F33 needs already exist as seams —
first-admin seeding (`AdminBootstrapper` + `AuthorizationBootstrapper`), enrollment (`IEnrollmentService`),
discovery/registration (`IServerInventory`), health (`IServerDiagnostics`), and TLS facts
(`DiagnosticsTlsProbe`) — so F33 is composition plus the missing gate, first-run state, and wizard UI.

Two forces shaped the decision. First, the setup flow **crosses an authentication boundary**: step 1 runs
with no user in existence (anonymous), and the moment it creates the first administrator it must sign that
account in — which requires writing an auth cookie on a real HTTP response, not inside a SignalR circuit.
Second, a bare install may have **no Agent connected**, so any step that depends on an Agent cannot be a
precondition for finishing setup without risking a wedged install.

## Decision

- **`InstallState` is a singleton, non-tenant-owned entity** (`ist-<uuid>`, fixed `DefaultId`), like `Tenant`:
  the first-run gate evaluates it before any tenant session exists, so it carries no tenant filter and its
  reads/writes are sanctioned unscoped operations (ADR 0016). It stores `SetupCompletedAt` (nullable; presence
  is the gate signal, monotonic in v1.0) and `TlsMode` (nullable; stored by name). It ships an EF
  configuration in Infrastructure and **paired Sqlite + Postgres migrations**. An empty row is seeded at
  startup next to the default tenant.

- **The completion contract is: first administrator + confirmed TLS mode.** Those two steps are mandatory;
  enrollment, discovery/registration, and health checks are guided-but-skippable (they land in F33 PR-B). A
  headless deploy that seeds its administrator from `ZWarden:Admin:*` **auto-completes** setup so it never
  sees the wizard.

- **The gate is a middleware placed before authentication.** While setup is incomplete it redirects any
  request that is not the wizard (`/setup*`), a framework/static asset (`/_*`, any path with a file
  extension), the Agent hub (`/agent*`), or the error page to `/setup`. Running before `UseAuthentication`
  means an un-set-up install routes even `[Authorize]` pages to `/setup` rather than `/login`. Completion is
  memoized in a process-wide `SetupCompletionSignal`, so after setup the gate costs one volatile read per
  request — no database round-trip.

- **The wizard is static-SSR pages, not `BbFormWizard`.** `/setup` creates the first administrator (guarded to
  the genuinely-first: it refuses when any user already exists) and signs them in on the request; `/setup/tls`
  runs authenticated, records the declared mode, and marks setup complete. A static `SetupStepIndicator`
  supplies the wizard "feel". `ISetupState` is the read/write seam over the record.

## Alternatives considered

- **`BbFormWizard` (one interactive circuit, one pooled model, submit-once).** Rejected: it needs a circuit on
  the anonymous pre-setup route, defers all side effects to a single `OnComplete`, and cannot span the
  anonymous→signed-in sign-in redirect. F33's steps are immediate, individually persisted, permission-gated
  server actions across an auth boundary — the opposite shape. Static-SSR pages match every existing form.

- **Setup state as fields on `Tenant`, or a generic key-value settings table.** Rejected: `Tenant` is
  deliberately minimal and is itself the tenant, so tenant-owned semantics are wrong for a pre-admin gate
  signal; a key-value store is heavier than one typed record needs and there is no existing settings table to
  extend.

- **Require Agent enrollment / a full walkthrough to finish setup.** Rejected: a bare install may have no Agent
  online, so making enrollment or a registered server a precondition can wedge setup. The mandatory set is
  exactly what is always achievable locally.

- **Gate after authentication.** Rejected: `[Authorize]` endpoints would challenge to `/login` before the gate
  ran, so a first-run operator would bounce to a login page instead of the wizard.

## Consequences

- There is now a persisted installation-level fact (`InstallState`) and a process-wide completion signal. The
  gate is on the hot path of every request; its cost after setup is a single volatile read, accepted as
  negligible. Every future entity that adds a migration continues to ship both provider migrations.

- **Completion is one-way in v1.0.** There is no "reset setup" path; re-running setup after completion only
  redirects away. Changing the recorded TLS mode later is not yet a surfaced flow (a future settings feature).

- The recorded `TlsMode` is **advisory** — ZWarden does not act on it to configure Caddy or terminate TLS
  (ADR 0035); it exists for the operator's confirmation, the F34 distribution, and diagnostics. Reachability
  is surfaced as a warning during setup, never a block, so an operator on plain HTTP can still finish.

- The first-administrator page is anonymous by necessity and is protected by a single invariant: it creates an
  administrator **only when no user exists**. That invariant, not authentication, is what prevents an
  anonymous second-admin creation during the setup window.
