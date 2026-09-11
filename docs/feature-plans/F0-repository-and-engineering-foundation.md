# Feature 0 Mini-Plan — Repository and Engineering Foundation

**Status:** ready for implementation. Resolves [Write the Feature 0 mini-plan](https://github.com/MCrank/ZWarden/issues/14); this is the terminal artifact of the [wayfinding map](https://github.com/MCrank/ZWarden/issues/1). Roadmap issue: [F0 (#20)](https://github.com/MCrank/ZWarden/issues/20).

**Format:** PRD 60. **Authority it is written against:** the locked technology baseline (ADRs [0001](../adr/0001-three-components-and-retired-vocabulary.md)–[0013](../adr/0013-feature-0-build-and-ci-policy.md)), the six named trust boundaries ([`trust-boundaries.md`](../trust-boundaries.md)), and the agreed sequence ([`scope-and-sequencing.md`](../scope-and-sequencing.md) §6, F0).

---

## Objective

Stand up the repository, solution, build, analyzer, dependency and CI foundation on which every later feature is built — such that **a clean build-and-test pipeline runs green from an empty baseline**, and the two silent-failure hazards a .NET/Blazor test harness carries (a suite that quietly stops testing; a Tailwind layer that quietly stops regenerating) are guarded against in CI rather than trusted to discipline. No domain code is written; the deliverable is the *engineering substrate* and its guardrails.

## Dependencies

**None** (F0 is the root of the DAG). It consumes only settled decisions:

- ADR 0001 — three components, retired vocabulary (drives the `src/` names).
- ADR 0002 — test stack, exact pins, CI tiering by assembly, the `--minimum-expected-tests` requirement.
- ADR 0003 — Blazor Blueprint 3.16.0, the Node/Tailwind step, the wrapper seam, the stale-`app.css` guard.
- ADR 0004/0005/0006 — forward parameters recorded here, applied later (see *Decisions carried forward*).
- ADR 0013 — the build-and-CI policy decided in [#14](https://github.com/MCrank/ZWarden/issues/14)'s grilling (warnings-as-errors posture, NuGet-advisory two-tier handling, analyzer mode, dependency locking).
- `trust-boundaries.md` §9 — the architecture-test assertions.
- PRD 14 (layout, superseded on the solution filename and the `tests/` shape as noted), PRD 15 (dependency rules), PRD 55/56 (supply chain, warnings-as-errors), PRD 2.2 (TDD mandatory).

## Scope

1. **Repository baseline** — `.gitignore` (VS/.NET/Node), `.gitattributes` (LF normalization; `*.slnx`, `*.razor`, `*.css` text), no secrets committed, `README` pointing at the docs.
2. **Solution** — `ZWarden.slnx` (**Q1**; PRD 14's `ZWarden.sln` literal is superseded — SDK 10 emits `.slnx` and `restore ZWarden.sln` fails `MSB1009`).
3. **`src/` skeleton, full breadth (Q8)** — all nine projects from PRD 14 (`ZWarden.Domain`, `.Application`, `.Infrastructure`, `.Contracts`, `.Web`, `.Agent`, `.Rcon`, `.Diagnostics`, `.PZServer`), empty of domain code, wired with reference directions that satisfy PRD 15 and `trust-boundaries.md` §9.
4. **SDK pin & language** — `global.json` pinned to **SDK 10.0.302, `rollForward: latestFeature`**, CI provisions **10.0.401**, runtime **10.0.12** (ADR 0002). `global.json` also carries `"test": { "runner": "Microsoft.Testing.Platform" }` — without it `dotnet test` is hard-blocked on SDK 10. Production projects leave `LangVersion` unset (`net10.0` → C# 14); test projects pin `<LangVersion>14.0</LangVersion>` for determinism (ADR 0002 item 6).
5. **Compiler / analyzer configuration** — `Directory.Build.props`: `Nullable=enable`, `ImplicitUsings=enable`, `TreatWarningsAsErrors=true`, `EnableNETAnalyzers=true`, `EnforceCodeStyleInBuild=true`, `AnalysisMode=Recommended` (ADR 0013; `All` is deferred — it is a 68-warning cliff on template code needing its own severity policy). NU1015/NU1510 hazards avoided (no versionless `PackageReference`; `Microsoft.Extensions.Diagnostics.HealthChecks` and other shared-framework packages never referenced directly).
6. **`.editorconfig`** — house style, plus the **CA1707 + CA1515 carve-out for `tests/**`** (Q2) so `Method_does_thing` test naming and `internal` test classes are legal there and nowhere else.
7. **Dependency management** — central package management (`Directory.Packages.props`) with `CentralPackageTransitivePinningEnabled=true`; `RestorePackagesWithLockFile=true` + CI `restore --locked-mode` (satisfies PRD 55 dependency-locking); `nuget.config` with `<clear/>` before nuget.org; `DOTNET_CLI_TELEMETRY_OPTOUT=1` as repo policy.
8. **NuGet-advisory policy (Q3, ADR 0013)** — audit codes `NU1901`–`NU1905` demoted via `WarningsNotAsErrors` so a newly-published advisory shows as a **warning** on PR/`main` (never silent, never blocking unrelated work); a **scheduled** CI job re-promotes them to errors, fails loudly, and opens a tracking issue; any open advisory is a **hard blocker at the F40 gate**. Third-party `[Obsolete]`/analyzer breakage handled by *targeted* per-diagnostic `NoWarn` with a tracking comment, never a global mute.
9. **Test harness (ADR 0002), tiered by assembly:**
   - **Tier 1 (offline unit):** TUnit **1.66.27** + TUnit.Mocks **1.66.27**; MTP runner; `--minimum-expected-tests` floors mandatory.
   - **Reflection-mode bUnit assembly** (`ZWarden.Web.Tests`): `bunit` **2.10.3**, `Microsoft.NET.Sdk.Razor`, explicit `<FrameworkReference Include="Microsoft.AspNetCore.App" />`, `<EnableTUnitSourceGeneration>false</EnableTUnitSourceGeneration>` **and** `[assembly: TUnit.Core.ReflectionMode]`, its own `--minimum-expected-tests` floor.
   - **Tier 2 (integration):** `ZWarden.IntegrationTests` with Testcontainers / Testcontainers.PostgreSql **4.15.0**, Npgsql **10.0.3**.
   - **E2E stub:** `ZWarden.EndToEndTests` assembly shell only (Playwright wiring deferred — never exercised in the spike).
10. **Parallelism policy (Q4)** — unit assemblies fully parallel; every container-backed test gated by a shared `ParallelLimiter` capped at **1** in F0; CI passes `--maximum-parallel-tests` sized to runner cores.
11. **Testcontainers lifetime (Q5)** — one shared PostgreSQL container **per test session** (assembly-scoped), started once and disposed at session end; per-test isolation via fresh database/schema or transaction rollback, never a fresh container.
12. **ZWarden.Web + UI pipeline (ADR 0003, Q8)** — Blazor Web App hosting `BlazorBlueprint.Components` / `.Primitives` **3.16.0** (exact pins); the Node/`npm`/Tailwind build step; the **wrapper-seam rule** (ZWarden components over theme tokens, library primitives underneath) demonstrated by one owned component (`StatusBadge`, per the Signal style guide, ADR-superseded placeholder in the roadmap) plus one trivial exercised page that uses a Tailwind utility so the guard has something real to protect.
13. **CI pipeline** — GitHub Actions running: `restore --locked-mode`; the offline tier inside a network namespace (`sudo unshare -n`) with egress probes asserted to fail; the integration tier; the e2e stub; the **silent-drop guard** job; the **stale-`app.css` guard** job; and the **scheduled advisory** job. Step summaries surfaced.
14. **ADR framework** — `docs/adr/README.md` index (currently absent per the map) listing 0001–0013, plus a copy-me ADR template.
15. **Contribution / development documentation** — `CONTRIBUTING.md`: the TDD workflow (PRD 2.2), test naming, running each CI tier locally (including the offline namespace), the Tailwind step and why forgetting it is silent, central package management, and how to add an ADR.

## Non-scope

- **Any domain code.** No entities, no typed IDs (F1), no persistence/EF model or migrations (F2), no Identity (F4). Empty skeletons only.
- **Container scanning, SBOM, artifact signing, build provenance** — post-1.0 (PRD 54/55); SBOM/signing enter at the F40 gate.
- **`AnalysisMode=All`** — deferred behind its own severity-policy slice (68-warning template cliff).
- **Playwright/WebApplicationFactory wiring** — assembly shell only; never exercised in the spike.
- **Applying** the PBKDF2 count (F4) or the SQLite `CommandTimeout` (F2) — the *numbers* are decided here (below), the *code* lands with those features.
- **The three architecture-test assertions that need types not yet in existence** (§9 rules 3, 4, 7 — free-form-command contracts, repository tenant filter, the RCON-password type). The arch-test project exists in F0; those assertions are added by the features that introduce the types.
- **No PZ-derived artefact** is committed (ADR 0009) — a repo-level rule, though the fixtures it concerns are F12's.

## Domain changes

**None.** F0 introduces no entities, value objects, services, state machines, or invariants. The one *engineering* invariant it establishes and enforces is the reference-direction rule set (below), asserted as tests, not modelled as domain types. `CONTEXT.md` is unchanged by this feature.

## Contract changes

**None** in the API/Agent/SignalR/persistence/serialization sense — no contract types exist yet (`ZWarden.Contracts` is an empty skeleton). F0 fixes only the *project-reference contract* between assemblies:

- `ZWarden.Domain` references no infrastructure framework (EF Core, ASP.NET Core, Docker, SignalR, SteamCMD, filesystem impls) — PRD 15 / §9 rule 2.
- `ZWarden.Agent` references neither EF Core, `ZWarden.Infrastructure`, nor any DB provider — §9 rule 1.
- `ZWarden.Web` references no Docker client library — §9 rule 5.
- Auth0-specific types appear in neither `ZWarden.Domain` nor `ZWarden.Application` — §9 rule 6.

## Security considerations

F0 writes no security-sensitive logic, but it sets the security *posture* every later feature inherits, and it must not undermine it:

- **Supply chain (PRD 55).** Dependency locking (`restore --locked-mode`) + central transitive pinning make the dependency graph reproducible and reviewable; the advisory policy (§8) keeps a live advisory *visible* without letting it silently ship or silently block. The F40 gate is the hard stop.
- **No secrets in the repo.** `.gitignore` covers user-secrets, `*.env`, cert/key material; CI uses no committed credentials; the scheduled advisory job needs only read scope + issue-create.
- **Boundary enforcement as build failure.** The architecture tests turn `trust-boundaries.md` §9 into red builds — the reference-direction rules are the earliest, cheapest enforcement of the load-bearing `Web → Agent` and database-reachability boundaries (§3, §7).
- **Decisions carried forward (recorded now, applied later):**
  - **PBKDF2-HMAC-SHA512 iteration count = 220,000** (Q6), chosen **2026-09-11** against OWASP's then-current figure for that exact PRF, reviewed at the F40 gate. Applied in F4 (`PasswordHasherOptions.IterationCount`) via the new `Rfc2898DeriveBytes.Pbkdf2` API (`SYSLIB0060`); recorded where the config lives per ADR 0006.
  - **`SqliteCommand.CommandTimeout` = 30 seconds** (Q7) — explicit, never 0 (0 = infinite busy-retry, ADR 0005). A documented shared default that F2 consumes; not re-litigated in F11.
- **Telemetry** opted out repo-wide (`DOTNET_CLI_TELEMETRY_OPTOUT=1`).

## Test plan

Tests are written **before** the config/scaffolding they verify (PRD 2.2). The harness is partly self-testing — several "tests" here are CI jobs that assert a guard *fires*.

1. **Tier-1 smoke** — a trivial TUnit test and a trivial TUnit.Mocks test in `ZWarden.Domain.Tests`; proves the runner, MTP wiring, and the mock seam. Written red first.
2. **Silent-drop guard** — a `.razor` component test in the reflection-mode `ZWarden.Web.Tests`; the CI guard job strips `[assembly: TUnit.Core.ReflectionMode]` and asserts the run **fails** on `--minimum-expected-tests` (MTP exit **9**). This is the single highest-value line in the pipeline (ADR 0002).
3. **Offline-tier isolation** — the offline job asserts, inside `unshare -n`, that four egress probes fail while the suite still runs green; restore is the only network-permitted step.
4. **Integration smoke** — one DB-connectivity test against the session-shared Postgres container; asserts the container starts exactly once and the `ParallelLimiter` cap holds.
5. **Stale-`app.css` guard** — regenerate Tailwind and `git diff --exit-code` the committed `app.css`; the guard job mutates a utility class without regenerating and asserts the job goes **red** (ADR 0003 condition 2).
6. **Architecture tests** — assert the four §9 reference-direction rules that are checkable now; each is proven by temporarily adding the forbidden reference and observing a red build, then reverting.
7. **Warnings-as-errors** — a build with an intentional `[Obsolete]` call / underscore-named production method fails; the same names in `tests/**` do not (CA1707 carve-out).
8. **Advisory policy** — the scheduled-job path is exercised (dry-run) to prove promotion-to-error + issue-creation fire, and that the PR/`main` path leaves the same advisory as a non-blocking warning.

## Implementation slices

Each is independently verifiable and comfortably inside one agent context (PRD 59 guardrail).

- **S0 — Solution & policy spine.** Repo hygiene, `ZWarden.slnx`, `global.json`, `Directory.Build.props`, `Directory.Packages.props`, `nuget.config`, `.editorconfig`. *Verify:* `restore --locked-mode` clean and `build` green with warnings-as-errors on an empty solution.
- **S1 — `src/` skeleton.** All nine projects, empty, reference directions wired. *Verify:* solution builds; references match PRD 15 (asserted for real in S6).
- **S2 — Tier-1 harness.** TUnit + TUnit.Mocks in `ZWarden.Domain.Tests`, MTP runner, min-expected floor. *Verify:* offline tier green in a network namespace.
- **S3 — Reflection-mode assembly + silent-drop guard.** `ZWarden.Web.Tests` in reflection mode with a `.razor` test; the strip-and-assert-9 guard job. *Verify:* guard red without the attribute, green with it.
- **S4 — Integration tier.** `ZWarden.IntegrationTests`, session-shared Postgres container, `ParallelLimiter` cap, connectivity test. *Verify:* tier-2 green; one container; cap enforced.
- **S5 — Web + Blueprint + Tailwind + stale-css guard.** `ZWarden.Web` with Blueprint 3.16.0, the Node/Tailwind step, the `StatusBadge` wrapper-seam demo, one exercised page, the css-diff guard. *Verify:* app builds; `app.css` regenerates deterministically; guard red when stale.
- **S6 — Architecture tests.** `ZWarden.ArchitectureTests` asserting §9 rules 1, 2, 5, 6. *Verify:* green on the skeleton; red when a forbidden reference is added.
- **S7 — CI assembly.** The workflow wiring every tier + both guards + the scheduled advisory job + `--locked-mode` + step summaries. *Verify:* full pipeline green on a PR; scheduled path dry-run proven.
- **S8 — ADR framework + contribution docs.** `docs/adr/README.md` index + template; `CONTRIBUTING.md`. *Verify:* docs present, index lists 0001–0013, template usable.

Suggested order: S0 → S1 → {S2, S5} → {S3, S4, S6} → S7 → S8. S2 and S5 are independent once the skeleton exists; S7 depends on all test slices.

## Diagnostics

How a failure is *detected and understood*, by design:

- **Silent test-drop** → `--minimum-expected-tests` violation, MTP exit **9**, distinct from a test failure (exit **2**); TRX distinguishes `Failed` from `NotExecuted`.
- **Silent CSS staleness** → the `git diff --exit-code` guard turns an invisible layout bug into a red job.
- **Advisory / obsolete drift** → warnings on every build, promoted-and-loud on the scheduled job with an auto-filed issue.
- **Dependency drift** → `restore --locked-mode` fails on any un-committed lockfile change.
- **Boundary violation** → the architecture tests name the offending reference and fail the build.
- **Run visibility** → TUnit's GitHub step summary (read in the same step that writes `$GITHUB_STEP_SUMMARY`).

## Documentation

- `CONTRIBUTING.md` (scope item 15).
- `docs/adr/README.md` index + ADR template.
- `README` updated to point at `CONTEXT.md`, `docs/adr/`, `docs/scope-and-sequencing.md`, `docs/trust-boundaries.md`, and this plan.
- **New ADR 0013** — the F0 build-and-CI policy (warnings-as-errors posture, NuGet-advisory two-tier handling, `AnalysisMode=Recommended` + tests carve-out, dependency locking), authored alongside this plan.
- The two carried parameters (PBKDF2 220,000 @ 2026-09-11; `CommandTimeout` 30s) recorded in this plan and to be echoed where they are applied (F4, F2).
- `CONTEXT.md` — unchanged (no new domain vocabulary).

## Acceptance criteria

1. `dotnet restore --locked-mode` and `dotnet build` of `ZWarden.slnx` are green with `TreatWarningsAsErrors=true`.
2. The offline tier runs green *inside a network namespace* with egress probes failing; restore is its only network step.
3. The integration tier runs green against a single session-shared Postgres container with the parallel cap enforced.
4. The silent-drop guard is red without the reflection-mode attribute and green with it.
5. The stale-`app.css` guard is red when `app.css` is stale and green when regenerated.
6. The four in-scope architecture assertions pass on the skeleton and fail when a forbidden reference is introduced.
7. Production code cannot use underscore method names or obsolete APIs (build error); `tests/**` can use underscores.
8. A live NuGet advisory is a non-blocking warning on PR/`main` and a red, issue-filing failure on the scheduled job.
9. `docs/adr/README.md` lists 0001–0013; `CONTRIBUTING.md` documents the TDD workflow, the CI tiers, the Tailwind step, and the ADR process.

## Definition of Done

Per PRD 61, the applicable subset: acceptance criteria met; tests authored as executable specifications; unit + integration + (stub) e2e projects present and green; **architecture rules pass**; **no secrets logged or committed**; diagnostic behaviour (the two guards + exit-code semantics) exists; error/failure conditions modelled in CI (advisory, drift, stale css, silent drop); **SQLite and PostgreSQL test paths exist** at the harness level (Postgres via Testcontainers; SQLite exercised when F2 lands); documentation and ADR 0013 updated; **CI is green**; **no unresolved warnings** on PR/`main`; threat considerations reviewed against `trust-boundaries.md` §9. (Migrations, authorization, and audit are N/A at F0 — there is no domain to migrate, authorize, or audit yet.)
