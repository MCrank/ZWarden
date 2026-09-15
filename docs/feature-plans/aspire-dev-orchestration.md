# Aspire Dev Orchestration Mini-Plan — Local AppHost + Integration Testing

**Status:** READY FOR AGENT. Issue: [#123](https://github.com/MCrank/ZWarden/issues/123) (`enhancement`, `ready-for-agent`). **Engineering-foundation** item, not a roadmap F-number — dev/test tooling. No product behaviour changes.

**Format:** PRD 60. **TDD is mandatory** (PRD 2.2). **Written against:** [`Directory.Build.props`](../../Directory.Build.props) / [`Directory.Packages.props`](../../Directory.Packages.props) / [`global.json`](../../global.json) (build policy, CPM, lockfiles, SDK/runner pins — ADR [0013](../adr/0013-feature-0-build-and-ci-policy.md)), ADR [0002](../adr/0002-test-stack-and-ci-tiering.md) (test stack + CI tiering — Aspire's integration boot slots into the Docker-requiring tier), ADR [0008](../adr/0008-docker-socket-access-via-wollomatic-socket-proxy.md) (the wollomatic allowlist — modelled in the dev graph, **never loosened for convenience**), ADR [0021](../adr/0021-serilog-is-the-logging-stack.md) (Serilog stays the logging owner), ADR [0024](../adr/0024-opentelemetry-observability-baseline.md) (our OTel baseline — the AppHost dashboard *is* the dev OTLP endpoint it already targets). One new ADR lands with the work: **0031** (Aspire is dev/test orchestration only and does not govern production — production packaging remains [F34 (#53)](https://github.com/MCrank/ZWarden/issues/53)).

Aspire 13.5 supports the current LTS (**.NET 10**), which the repo builds on. The `aspire` CLI is already installed locally.

## Objective

Add a single-command local orchestration and integration-test harness for the whole distributed graph — **ZWarden.Web + ZWarden.Agent + Postgres + the wollomatic socket-proxy** — via an Aspire **AppHost**, plus `Aspire.Hosting.Testing` so tests boot the real graph and assert against a running Web (screenshots via the Playwright companion), correlated logs/traces in one dashboard, and real Docker container paths. Also adopt Aspire's AI-agent tooling (`aspire agent init` → skills + optional MCP server + Playwright/dotnet-inspect companions).

## Dependencies

All present and merged. The graph being orchestrated already exists: Web host (F4+), Agent runtime (F8), enrollment (F9), SignalR control plane (F10), Docker runtime + wollomatic (F13, ADR 0008), OTel baseline (F16, ADR 0024). No feature work is blocked on this; it is additive tooling. No new runtime dependencies enter `src/` production projects — every Aspire package lands only in the new AppHost and test projects.

## Scope

- A new **`src/ZWarden.AppHost`** project (`Aspire.AppHost.Sdk`) modelling the dev graph: `AddPostgres` → database resource named **`ZWarden`**; `AddProject` for **Web** and **Agent** wired with `WithReference`/`WaitFor`; `AddContainer` for the **wollomatic** proxy with the Agent pointed through it.
- Config/endpoint **injection** replacing hand-set dev config (see Contract changes) — no `Program.cs` rewrite in Web or Agent.
- A **dev enrollment bootstrap** so the graph self-enrolls on boot (D-ENROLL) — no manual first-run enrollment in the inner loop.
- An **integration-test entry point** (`Aspire.Hosting.Testing`, `DistributedApplicationTestingBuilder`) that boots the graph and exposes it to tests, wired into the CI Docker tier.
- **`aspire agent init`** — install the Aspire skills, wire Playwright/dotnet-inspect, optionally the MCP server (D-AGENTTOOLING).
- **ADR 0031**, updates to the `run` / `run-web` skills (→ `aspire run`), a `CONTRIBUTING.md` dev-loop note, and this doc.

## Non-scope

- **Production deployment / packaging.** Aspire `publish`/compose-generation is *not* adopted as the shipping artifact — that is **F34** (#53), with F32 (#51) and F33 (#52). The self-hosted runtime model (enrollment, wollomatic, tenant model) is unchanged.
- **No change** to the Web↔Agent protocol, auth schemes, persistence/migrations, or the container-ownership/allowlist invariants (ADR 0008/F13).
- **No adoption of Aspire ServiceDefaults' opinions** over our stack (D-SVCDEFAULTS) — Serilog and our OTel stay the owners.
- Deploying the Aspire dashboard or MCP server anywhere but the local dev loop.

## The load-bearing decisions

- **D-SCOPE — dev/test only.** Aspire orchestrates the inner loop and integration tests. It does not deploy, package, or run in production. Recorded in **ADR 0031**; a note in the AppHost README states it plainly so it does not drift into the deployment features.
- **D-RISK-TEST — prove `DistributedApplicationTestingBuilder` green under TUnit / Microsoft.Testing.Platform *first*, red-first.** The repo runs **TUnit on MTP** (`global.json` → `test.runner`); Aspire's shipped test templates are xUnit/MSTest/NUnit. The **first commit of Slice 3 is a failing integration test** that boots the AppHost via `DistributedApplicationTestingBuilder.CreateAsync<Projects.ZWarden_AppHost>()` under TUnit/MTP, waits for Web healthy, and asserts a 200 from a Web endpoint. If TUnit/MTP cannot host the builder cleanly (async lifetime/disposal), the **documented fallback** is a single isolated **xUnit** test project scoped to the Aspire integration boot only (kept out of the TUnit suite, its own CI step) — the harness is still delivered; only the runner for this one tier differs. We stop and take the fallback rather than contort the whole test stack.
- **D-SVCDEFAULTS — do not adopt stock ServiceDefaults.** Serilog (0021) and `AddZWardenTelemetry` (0024) remain the owners; adding `AddServiceDefaults()` would double-instrument OTel and fight the two-stage Serilog bootstrap. **Recommended: skip ServiceDefaults entirely** — our OTel already emits OTLP when an endpoint is set, and the AppHost injects that endpoint (`OTEL_EXPORTER_OTLP_ENDPOINT`) so telemetry lights up in the dashboard with zero code change. If dev-only aggregate health endpoints are wanted later, add a *thin owned* helper guarded to Development — not the stock package.
- **D-ENROLL — dev enrollment bootstrap, dev-only.** The Agent needs a trust credential to connect (F9). The AppHost seeds a **well-known dev enrollment credential** as an Aspire parameter and injects it to both Web (pre-authorized) and Agent, so the graph self-enrolls on boot. It is fenced to the dev inner loop and is **never** a production path (ADR 0031 says so; an arch/整 test or a guard asserts the well-known credential is refused outside Development). *Exact seam to reuse from F9 is confirmed in Slice 2 against `AddZWardenEnrollment`.*
- **D-WOLLO — model wollomatic as a container resource; keep the allowlist intact.** `AddContainer("wollomatic", …)` with the socket mount; the Agent's Docker path runs through it exactly as in production. The ADR 0008 allowlist is **not** widened for dev ergonomics — if something needs a verb the allowlist denies, that is a finding, not a dev exception.
- **D-CONN — name the DB resource `ZWarden`.** Aspire then injects `ConnectionStrings__ZWarden`, which is exactly what `GetConnectionString("ZWarden")` reads (Web `Program.cs:46`). Provider is switched with `WithEnvironment("ZWarden__Database__Provider", "postgres")`, exercising the dual-provider path (ADR 0005) and the Postgres migrations at startup.
- **D-AGENTTOOLING — commit the skills, keep MCP local by default.** `aspire agent init` installs the Aspire skills; commit them in-repo under the existing skill layout, reconciled with `docs/agents/` conventions (and CLAUDE.md → "Agent skills" gets a pointer). The optional MCP server config stays **local/uncommitted** unless the maintainer wants it shared — it grants runtime access to a running app, so it is an opt-in per-developer choice.
- **D-BUILDPOLICY — relax build policy only for the AppHost.** Aspire's generated `Projects.*` and AppHost scaffolding can trip `TreatWarningsAsErrors`/analyzers/nullable (ADR 0013). Apply targeted relaxation **scoped to `src/ZWarden.AppHost`** (a `Directory.Build.props` condition or per-project `NoWarn` with a tracking comment) — never widen the solution-wide policy.

## Domain changes

**None.** No entities, value objects, services, state machines, or invariants change. This is dev/test infrastructure; `src/ZWarden.Domain` and every production project are untouched except for the AppHost's outside-in `ProjectReference` (which adds no code to them).

## Contract changes

No **protocol, persistence, or serialization** contracts change — `ProtocolVersion.Current` is untouched, no migration is added. The only new "contracts" are **build + config injection**:

- **New project** `src/ZWarden.AppHost` added to `ZWarden.slnx`; MSBuild SDK `Aspire.AppHost.Sdk` pinned in `global.json` `msbuild-sdks`.
- **New packages** in `Directory.Packages.props` (exact pins, ADR 0002): `Aspire.Hosting.AppHost`, `Aspire.Hosting.PostgreSQL`, `Aspire.Hosting.Testing` (≈13.5.x). New `packages.lock.json` files regenerate for the AppHost + test project ([[packages-lock-sdk-drift]]) and are committed.
- **Env injection the existing hosts already read** (no code change): Web ← `ConnectionStrings__ZWarden`, `ZWarden__Database__Provider=postgres`, `OTEL_EXPORTER_OTLP_ENDPOINT`; Agent ← its hub-URL key (resolved from `AgentOptions` in Slice 2) from `web.GetEndpoint("https")`, the dev enrollment credential, and `OTEL_EXPORTER_OTLP_ENDPOINT`.

## Security considerations

- **Dashboard + MCP server expose logs/traces/runtime commands** — dev-only surfaces. ADR 0031 forbids shipping them; the MCP config is not committed by default (D-AGENTTOOLING).
- **Dev enrollment credential (D-ENROLL) must never authenticate in production.** Well-known + fenced to Development, with a guard/test asserting it is refused otherwise. It is a convenience for the inner loop, not a backdoor.
- **The wollomatic allowlist is not relaxed** (D-WOLLO) — the dev graph exercises the same proxy constraints as production (ADR 0008), so dev cannot mask a denied-verb regression.
- **Secrets/connection strings in dev** come from Aspire parameters/user-secrets, never source-committed. Agent log output remains untrusted input (PRD 38) — unchanged by this work.
- No new production attack surface: no Aspire package enters a `src/` production project's runtime.

## Test plan (TDD)

- **Slice 3, first (D-RISK-TEST, red→green):** integration test boots the AppHost under TUnit/MTP via `DistributedApplicationTestingBuilder`, `WaitForResourceHealthyAsync("web")`, asserts a 200 from a Web endpoint (`CreateHttpClient("web")`). Must go red→green before the rest of Slice 3; on failure, take the documented xUnit fallback.
- **End-to-end graph test:** boot Web + Postgres + Agent + wollomatic; assert the Agent reaches `Connected` against the Web hub (self-enrolled), and a read-only Diagnostics.Ping-style path round-trips — proving injection + enrollment + the Docker/proxy wiring hold together.
- **Guard test (D-ENROLL):** the well-known dev enrollment credential is refused outside Development.
- **Arch test (PRD 15):** no Aspire package is referenced by any `src/` production project (only `ZWarden.AppHost` + the Aspire test project) — keeps the dev/test/prod boundary honest.
- **Build-policy check:** solution builds warnings-as-errors green with the AppHost's *scoped* relaxation only (no solution-wide widening).

## Implementation slices

Grouped into three PRs; each slice is independently verifiable.

**PR-1 — Spike + ADR (branch `feat/aspire-spike`).**
1. Add `src/ZWarden.AppHost` (`Aspire.AppHost.Sdk`); minimal AppHost that boots **Web + Postgres** only; `aspire run` serves Web against Postgres locally (provider switch + migrations verified by hand). Scoped build-policy relaxation (D-BUILDPOLICY). Pin packages/SDK; regenerate + commit lockfiles.
2. Write **ADR 0031** (dev/test-only scope) and finalize this mini-plan's open confirmations.

**PR-2 — Full graph + dev wiring + agent tooling (branch `feat/aspire-apphost`).**
3. Add the **Agent** project resource with `WithReference(web)`/`WaitFor`; resolve and inject the Agent's hub-URL key from `AgentOptions`.
4. Add the **wollomatic** container resource (D-WOLLO); point the Agent's Docker path through it.
5. **Dev enrollment bootstrap** (D-ENROLL) + the out-of-Development refusal guard.
6. Confirm telemetry: OTLP endpoint injected, dashboard shows correlated Web+Agent logs/traces (D-SVCDEFAULTS: no ServiceDefaults).
7. `aspire agent init` (D-AGENTTOOLING): commit skills under the repo layout, reconcile with `docs/agents/`, CLAUDE.md pointer; update the `run`/`run-web` skills to `aspire run`; `CONTRIBUTING.md` dev-loop note.

**PR-3 — Integration-test tier (branch `feat/aspire-testing`) — closes #123.**
8. Risk-gate test first (D-RISK-TEST), then the end-to-end graph test, the D-ENROLL guard test, and the arch test.
9. Wire the boot into CI's **Docker-requiring tier** (ADR 0002), gated so the offline tier skips it; confirm deterministic restore with the committed lockfiles.

## Diagnostics

The Aspire **dashboard** is the primary dev diagnostic — per-resource logs, traces, metrics, health, and an interactive terminal, correlated across Web + Agent. The red→green risk gate (D-RISK-TEST) makes the TUnit/MTP compatibility question a visible pass/fail rather than a late surprise. CI surfaces the integration tier's logs on failure. The optional MCP server lets an agent inspect the running graph directly.

## Documentation

- **ADR 0031** — Aspire is dev/test orchestration only; production stays F34.
- `src/ZWarden.AppHost/README.md` — what the graph is, `aspire run`, the dev-only fence.
- Update the **`run`** and **`run-web`** skills to drive `aspire run`.
- `CONTRIBUTING.md` — the new one-command dev loop.
- **CLAUDE.md → "Agent skills"** — pointer to the committed Aspire skills (D-AGENTTOOLING).
- This mini-plan.

## Acceptance criteria

- `aspire run` boots Web + Postgres + Agent + wollomatic locally; the Agent self-enrolls and reaches `Connected`; the dashboard shows correlated Web+Agent telemetry.
- An integration test boots the graph and asserts a running-Web response **green in CI's Docker tier** (or the documented xUnit fallback, if D-RISK-TEST forces it).
- The dev enrollment credential is refused outside Development (test-proven).
- No Aspire package is referenced by any production `src/` project (arch-test-proven); the wollomatic allowlist is unchanged.
- Solution builds warnings-as-errors green; lockfiles committed; `run`/`run-web` skills, ADR 0031, and docs updated.

## Definition of Done

All three PRs merged; code, tests (TDD), the security guard, ADR 0031, skill/doc updates, and committed lockfiles complete; CI green including the new Docker-tier integration boot; #123 closed. No production runtime, protocol, or persistence change shipped.

## Open confirmations to raise if they bite

- **TUnit/MTP × `DistributedApplicationTestingBuilder` (D-RISK-TEST).** The red-first gate decides it; the xUnit fallback is pre-agreed so the harness ships either way.
- **The Agent's exact hub-URL config key** — read from `AgentOptions`/appsettings in Slice 2 rather than assumed here.
- **The F9 enrollment seam for the dev bootstrap** — confirm against `AddZWardenEnrollment` in Slice 2; if a clean seed seam is missing, a tiny dev-only issuance helper is the fallback (still dev-fenced).
- **Aspire 13.5 exact patch versions** — pinned at implementation via CPM; `aspire agent init`'s emitted layout confirms the skill paths.
