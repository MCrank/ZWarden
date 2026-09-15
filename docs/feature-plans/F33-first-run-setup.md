# Feature 33 Mini-Plan — First-Run Setup

**Status:** DONE (2026-09-15). **PR-A** (PR #138, merged): InstallState + gate + first-admin + confirm-TLS +
step indicator + ADR 0036. **PR-B** (this PR, closes #52): the guided-but-skippable enrollment /
discovery-registration / health steps + the shared Skip/Finish nav + the optional "continue" branch off the
TLS step. Full TDD suite green — Domain 226, Infrastructure 346, Web 218, Architecture 23. Load-bearing
decisions were **LOCKED as-recommended with the maintainer (2026-09-15)**. Roadmap issue:
[F33 (#52)](https://github.com/MCrank/ZWarden/issues/52), **Track F — Deployment and release**. The feature
that gives a fresh, self-hosted operator a **guided first-run wizard**: create the first administrator, confirm
the TLS mode, enroll an Agent, discover and register PZ servers, run initial health checks, and record that the
installation is set up — so a bare deployment goes from "container started" to "governed control plane" without
touching environment variables or the database by hand.

**Depends on** (all merged): [F5 (#27)](https://github.com/MCrank/ZWarden/issues/27) — DONE (authorization /
Tenant Owner), [F9 (#31)](https://github.com/MCrank/ZWarden/issues/31) — DONE (Agent enrollment & trust),
[F14 (#35)](https://github.com/MCrank/ZWarden/issues/35) — DONE (server discovery / import / register), and
[F32 (#51)](https://github.com/MCrank/ZWarden/issues/51) — DONE (TLS mode concept). **Blocks
[F34 (#53)](https://github.com/MCrank/ZWarden/issues/53)** (Complete Docker Compose Distribution), which wires
the setup flow into the shipped stack.

**Format:** PRD 60. **TDD is mandatory** (PRD 2.2). **Written against:**
[`scope-and-sequencing.md`](../scope-and-sequencing.md) §6 (F33) and §11 criterion 1 ("deploy the supported
stack with minimal setup", satisfied by F32+F33+F34 together). ADRs it builds on:
[0016](../adr/0016-tenant-isolation-query-filter-and-default-tenant.md) (default tenant seeded at startup),
[0017](../adr/0017-external-identity-provider-seam.md) / [0018](../adr/0018-zwarden-owned-rbac.md) (identity +
ZWarden-owned RBAC — first admin gets Tenant Owner), the F9 enrollment ADRs, and
[0035](../adr/0035-caddy-reference-reverse-proxy-ingress.md) (Caddy owns TLS; F33 **records** the mode, it does
not configure Caddy). A new **ADR 0036** lands with the feature (the first-run setup model: a singleton
install-state record, the two-mandatory-steps completion contract, and the first-run gate).

## Objective

Turn a freshly-started ZWarden.Web instance with **no administrator** into a fully governed control plane
through a browser wizard, then remember that it is done. Concretely, on first run the app **gates** every route
to a `/setup` wizard until setup is complete; the wizard walks the operator through:

1. **Create the first administrator** (pre-auth) — a browser form over the existing `AdminBootstrapper` +
   `AuthorizationBootstrapper` path, then **auto-sign-in** the new admin so the remaining steps run
   authenticated and permission-gated.
2. **Confirm the TLS mode** — the operator declares Public / Private / Existing-reverse-proxy; F33 **records**
   the declared mode on the install-state record and **verifies reachability** with the existing
   `DiagnosticsTlsProbe` / `TlsDiagnostic` (warn-not-block). F33 does **not** configure Caddy (F32/F34).
3. *(skippable)* **Enroll an Agent** — the guided UI over `IEnrollmentService.IssueAsync`, showing the
   one-time secret once (the F9 code explicitly defers this UI to F33).
4. *(skippable)* **Discover & register servers** — list discovered-unregistered containers
   (`IServerInventory.ListAllDiscoveredUnregisteredAsync`) and import/register them.
5. *(skippable)* **Initial health checks** — run `IServerDiagnostics.CheckRconHealthAsync` (and/or the F29
   read-only sweep) against registered servers and surface the verdicts.
6. **Mark setup complete** — persist completion on the singleton install-state record; the gate lifts and
   `/setup` thereafter redirects away.

**Completion contract (D-1):** only steps **1 (first admin)** and **2 (confirm TLS mode)** are **mandatory**;
steps 3–5 are **guided-but-skippable**, because a bare install may have **no Agent connected yet** and cannot
be forced to enroll/discover/health-check synchronously. A headless deployment that seeds its admin via the
existing `ZWarden:Admin:*` env path **auto-completes** setup so it never sees the wizard.

## The load-bearing decisions (LOCKED as-recommended, 2026-09-15)

- **D-1 — Completion = first admin + confirmed TLS mode; enrollment / discovery / registration / health are
  guided-but-skippable. `[LOCKED]`** A fresh install may have no Agent online, so the mandatory set is exactly
  what is always achievable locally. The env-var admin-bootstrap path marks setup complete so headless/existing
  deployments skip the wizard entirely. *Rejected — require agent enrollment or a full walkthrough:* wedges a
  bare install whose Agent has not connected.

- **D-2 — Setup/completion state lives on a new singleton `InstallState` record (non-tenant-owned), which also
  holds the recorded TLS mode. `[LOCKED]`** The gate must evaluate **before** any admin or tenant session
  exists, so the record cannot be tenant-owned; it is a single well-known-id row. It is the home for both the
  `SetupCompletedAt` marker and the declared `TlsMode`. Ships an `IEntityTypeConfiguration<InstallState>` in
  Infrastructure and paired **Sqlite + Postgres** migrations. *Rejected — fields on `Tenant`* (breaks Tenant's
  deliberate minimality and its tenant-owned semantics pre-admin); *a generic key-value settings table* (no
  existing settings store to extend; heavier than one typed record needs to be).

- **D-3 — The wizard is delivered as **static-SSR sequential pages** under `/setup/*`, each its own gated POST,
  with a **lightweight persisted step indicator** for the wizard feel — **not** `BbFormWizard`. `[LOCKED]`**
  `BbFormWizard` is a single interactive circuit that pools one model and defers submit to one `OnComplete`;
  F33's steps run **pre-auth first**, **cross a sign-in redirect** (the auth cookie must be set by an HTTP
  POST, not a SignalR render), and each **persists an immediate, permission-gated side-effect**. Static SSR
  matches every existing form (Account pages, `/servers`) and the #84 "explicit `Name` under SSR" posture.
  *Rejected — BbFormWizard for the post-auth steps* (mixes two patterns; its defer-until-complete model still
  fights per-step immediate actions); *a full BbFormWizard circuit* (runs an interactive circuit on the
  anonymous pre-setup route and cannot span the sign-in cookie boundary).

- **D-4 — Ship in **two PRs**. `[LOCKED]`** **PR-A:** `InstallState` entity + EF config + paired migrations +
  the first-run gate + the create-first-admin step (with auto-sign-in) + confirm-TLS-mode step + the step
  indicator + **ADR 0036**. **PR-B:** the skippable enrollment, discovery/registration, and health-check steps
  + the completion marker + the "setup already complete" redirect polish. *Rejected — single PR:* likely
  near/over the PRD-59 ~100K guardrail and a large review across two auth contexts.

- **D-5 — After the first admin is created, **auto-sign-in** and run the remaining steps authenticated;
  **TLS mode is recorded/confirmed only** (F33 never configures Caddy). `[LOCKED — proceed-as-recommended]`**
  The enrollment / registration seams take a `UserId actor` and enforce permissions, so the wizard must be
  authenticated past step 1; sign-in is a normal HTTP redirect to the account sign-in endpoint. The recorded
  `TlsMode` feeds F34; actual TLS termination stays in Caddy (ADR 0035).

## First-run detection (the gate)

There is no "is there any admin yet?" query today, and no setup flag. F33 introduces both:

- **`InstallState`** singleton (well-known id): `SetupCompletedAt : DateTimeOffset?`, `TlsMode : TlsMode?`,
  plus `IVersioned`. Seeded (empty) alongside the default tenant in the startup bootstrap path
  (`MigrateAndBootstrapDefaultTenantAsync`).
- **First-run = setup not complete.** A small `ISetupState` application seam answers `IsSetupCompleteAsync()`
  and exposes the recorded TLS mode. Completion is set by (a) the wizard's final step, or (b) the env-var
  `AdminBootstrapper` path when it seeds an admin headlessly (so existing deployments never see the wizard).
- **The gate**: middleware / a routing convention that, while setup is incomplete, redirects any non-`/setup`,
  non-static-asset, non-account-endpoint request to the current wizard step, and — once complete — redirects
  `/setup*` away. It must allow the anonymous account sign-in POST (needed for step-1 auto-sign-in).

## TDD plan (mandatory — PRD 2.2)

**PR-A**
1. **`InstallState` + `ISetupState` (Domain/Infrastructure tests):** the singleton is created empty; recording
   completion sets `SetupCompletedAt`; recording a TLS mode persists it; `IsSetupCompleteAsync` reflects both
   the wizard marker and the env-bootstrap auto-complete. Round-trips on **both** providers (the migration is
   exercised by the existing dual-provider integration tests).
2. **First-run gate (Web integration tests via `WebApplicationFactory<Program>`):** with setup incomplete, a
   request to `/` (and `/servers`) **redirects to `/setup...`**; the account sign-in endpoint and static assets
   are **not** gated; with setup complete, `/setup*` redirects away and `/` renders normally.
3. **Create-first-admin step (bUnit + integration):** posting the form with no existing admin creates a
   confirmed admin, grants **Tenant Owner** (asserts via the F5 seeding path), and issues a redirect that leads
   to an authenticated session; posting when an admin already exists is refused / routed onward. Explicit
   `Name="..."` on Bb inputs under static SSR (#84).
4. **Confirm-TLS-mode step (bUnit + integration):** selecting a mode records it on `InstallState`; the
   `DiagnosticsTlsProbe`/`TlsDiagnostic` verdict is surfaced as a **warning, not a block**; completing the two
   mandatory steps marks setup complete.
5. **Step indicator (bUnit):** renders the correct active/completed/pending states from the persisted step.
6. **Bump `ZWarden.Web.Tests` `--minimum-expected-tests`** in **both** the csproj and `ci.yml` guarded pass to
   the exact new count (currently 202) — per the silent-drop-guard rule.

**PR-B**
7. **Enrollment step (bUnit + endpoint/integration):** issuing shows the one-time secret exactly once and lists
   existing enrollments; gated by `Tenant.Enrollment.Manage`; skippable.
8. **Discovery/registration step:** lists discovered-unregistered containers and import/register enqueues the
   provisioning Operation (reuses the `/servers` seams); gated by `Server.Register`/`Server.View`; skippable.
9. **Health-check step:** runs `CheckRconHealthAsync` against a registered server and renders the verdict;
   skippable.
10. **Completion redirect polish:** an authenticated admin hitting `/setup` after completion is redirected to
    the dashboard.
11. **Bump the Web.Tests floor again** for PR-B's additions (both csproj + ci.yml).

## Deliverables

**PR-A**
- `src/ZWarden.Domain/Setup/InstallState.cs` (+ `TlsMode` enum, likely in Domain or Contracts).
- `src/ZWarden.Application/Setup/ISetupState.cs` (read seam: `IsSetupCompleteAsync`, recorded TLS mode) and any
  small setup-orchestration seam.
- `src/ZWarden.Infrastructure/Setup/` — `SetupStateService`, repository, `IEntityTypeConfiguration<InstallState>`,
  `AddZWardenSetup()` DI extension; seed the empty singleton in the bootstrap path; env-bootstrap auto-complete.
- Paired migrations: `src/ZWarden.Migrations.Sqlite/**/*_AddInstallState.cs` and the Postgres twin.
- `src/ZWarden.Web/Hosting/` — the first-run gate (middleware / routing convention) + registration in `Program.cs`.
- `src/ZWarden.Web/Components/Pages/Setup/` — `SetupLayout` (step indicator) + `CreateAdmin.razor` +
  `ConfirmTls.razor` (static SSR, explicit input `Name`s, redirect-after-post).
- `docs/adr/0036-first-run-setup-singleton-install-state-and-gate.md`.
- Test bump (csproj + ci.yml).

**PR-B**
- `src/ZWarden.Web/Components/Pages/Setup/` — `EnrollAgent.razor`, `DiscoverServers.razor`, `HealthChecks.razor`,
  `Complete.razor` (mark-complete + redirect), each skippable, reusing the F9/F14/F29 seams.
- Test bump (csproj + ci.yml).

## Out of scope

- **Configuring Caddy / actual TLS termination** — F32 (config) and F34 (stack). F33 only **records** the mode.
- **The Docker Compose distribution, secrets bootstrap, volume/network wiring** — F34 (#53).
- **Remote / multi-host Agent onboarding UX** — F35 (#54).
- **Hosted-SaaS multi-tenant onboarding** — v1.1.
- **Re-running / editing setup after completion** beyond the redirect-away behavior — later polish if needed.
- **New enrollment / server / diagnostics domain logic** — F33 is composition over F9/F14/F29 seams only.

## PR shape

Two PRs (D-4): **PR-A** the install-state record + gate + two mandatory steps + step indicator + ADR 0036;
**PR-B** the three skippable steps + completion. Each ships its own TDD suite and its own `--minimum-expected-tests`
bump.
