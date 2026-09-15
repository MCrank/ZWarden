# 31. Aspire is dev/test orchestration only and does not govern production

ZWarden adopts **Aspire 13.5 for the local inner loop and integration testing only**. A single
`src/ZWarden.AppHost` project models the dev graph — Postgres + ZWarden.Web + (from PR-2)
ZWarden.Agent + the wollomatic socket-proxy — so `aspire run` boots the whole distributed system with
one command, with connection strings and endpoints **injected** rather than hand-set, and correlated
logs/traces/metrics in one dashboard. Aspire **does not deploy, package, or run in production**:
production packaging stays **F34 (#53)** (with F32 #51, F33 #52), and the self-hosted runtime model —
agent enrollment, the wollomatic allowlist (ADR 0008), the tenant model — is unchanged. **No Aspire
package enters any production `src/` project**; every Aspire dependency lives only in the AppHost and
the Aspire integration-test project, an invariant an architecture test will enforce (PR-3).

- Status: accepted
- Decided in: #123 (engineering-foundation enhancement; mini-plan
  [`docs/feature-plans/aspire-dev-orchestration.md`](../feature-plans/aspire-dev-orchestration.md))
- Bears on: ADR [0002](./0002-test-stack-and-ci-tiering.md) (the Aspire integration boot slots into the
  Docker-requiring tier, not the offline tier); ADR
  [0008](./0008-docker-socket-access-via-wollomatic-socket-proxy.md) (wollomatic is modelled in the dev
  graph and its allowlist is **never** widened for dev ergonomics); ADR
  [0013](./0013-feature-0-build-and-ci-policy.md) (build policy — any relaxation is scoped to the
  AppHost, never widened solution-wide); ADR [0021](./0021-serilog-is-the-logging-stack.md) (Serilog
  stays the logging owner — no stock ServiceDefaults); ADR
  [0024](./0024-opentelemetry-observability-baseline.md) (our OTel baseline — the AppHost dashboard is
  the dev OTLP endpoint it already targets); ADR [0005](./0005-dual-database-provider-strategy.md) (the
  dev graph drives the Postgres side of the dual-provider path); F34 (#53) production packaging.

## Context

ZWarden is a genuinely distributed system — ZWarden.Web (control plane) + ZWarden.Agent (host-resident
worker, outbound SignalR) + Postgres/SQLite + the wollomatic Docker socket-proxy + the PZServer
containers the Agent spawns — but until now there was **no dev-orchestration story**: no compose file,
no one-command boot. Exercising a change end-to-end meant hand-starting Web, hand-starting an Agent,
hand-provisioning a database and connection string, enrolling the Agent, and reading two console logs.
That friction grows with every feature block, and the remaining v1.0 features (F28 console, F29
diagnostics, F30 support package, F32–F34 deployment, F35 multi-host) are exactly the ones that most
need a "spin the whole thing up and watch it" loop.

Aspire 13.5 (rebranded from ".NET Aspire") targets this directly: an AppHost that boots the whole
graph, a dashboard with unified structured logs/traces/metrics across all resources, and a first-class
integration-test harness (`Aspire.Hosting.Testing`) that starts the real graph in-process and hands
tests `HttpClient`s and resource endpoints. It supports the current LTS (.NET 10) the repo builds on,
and the `aspire` CLI (13.5.3) is installed locally.

The decision this ADR records is **not** "use Aspire" in the abstract — it is the **boundary**: Aspire
is a development and test convenience, adopted so it can never quietly become the production deployment
mechanism. Aspire ships a `publish` / compose-generation path; adopting it as ZWarden's shipping
artifact would collide with the self-hosted, agent-enrolled, single-tenant-per-install runtime model
that F32–F34 own. Keeping the boundary explicit here stops the dev tooling from drifting into the
deployment features by convenience.

## Decision

1. **Dev/test scope only.** Aspire orchestrates the inner loop (`aspire run`) and hosts integration
   tests (`Aspire.Hosting.Testing`). It is not a deployment, packaging, or production-runtime tool.
   Production packaging remains F34 (#53). The AppHost README states the fence plainly so it does not
   drift.

2. **Dependency isolation is an invariant.** Aspire packages (`Aspire.Hosting.AppHost`,
   `Aspire.Hosting.PostgreSQL`, later `Aspire.Hosting.Testing`) are pinned in
   `Directory.Packages.props` and referenced **only** by `src/ZWarden.AppHost` and the Aspire
   integration-test project. No production `src/` project takes an Aspire dependency; the Web/Agent
   `Program.cs` files are not rewritten. An architecture test enforces this (PR-3). The AppHost's
   `ProjectReference` to Web is outside-in (source-generator plumbing) and adds no code to Web.

3. **Injection over hand-set config, reusing existing seams — no code change to the hosts.** The DB
   resource is named **`ZWarden`**, so Aspire injects `ConnectionStrings__ZWarden`, which is exactly
   what `GetConnectionString("ZWarden")` reads. The provider is switched with
   `ZWarden__Database__Provider=postgres`, exercising the dual-provider path (ADR 0005) and the
   Postgres startup migrations. Telemetry lights up in the dashboard because the AppHost injects
   `OTEL_EXPORTER_OTLP_ENDPOINT` into hosts whose OTel already emits OTLP when an endpoint is set
   (ADR 0024) — no new instrumentation code.

4. **No stock ServiceDefaults.** Serilog (ADR 0021) and `AddZWardenTelemetry` (ADR 0024) remain the
   owners. `AddServiceDefaults()` would double-instrument OTel and fight the two-stage Serilog
   bootstrap, so it is not adopted; if dev-only aggregate health endpoints are wanted later, a thin
   *owned* helper guarded to Development is the path, not the stock package.

5. **The wollomatic allowlist is not relaxed for dev.** (PR-2) The proxy is modelled as a container
   resource and the Agent's Docker path runs through it exactly as in production (ADR 0008). If dev
   needs a verb the allowlist denies, that is a finding, not a dev exception.

6. **A dev enrollment credential is fenced to Development.** (PR-2) The AppHost seeds a well-known dev
   enrollment credential so the graph self-enrolls on boot, and a guard/test asserts that credential
   is refused outside Development. It is an inner-loop convenience, never a production auth path.

7. **Build-policy relaxations are AppHost-scoped.** (ADR 0013) Aspire's generated `Projects.*` /
   AppHost scaffolding can trip warnings-as-errors, analyzers, or nullable. Any relaxation is scoped to
   `src/ZWarden.AppHost` (a per-project `NoWarn` or targeted condition with a tracking comment), never
   widened solution-wide. PR-1 keeps `AspireUseCliBundle=false` (DCP + dashboard restored from NuGet,
   so a CLI-less build machine still builds deterministically) and suppresses only the resulting
   ASPIRE010 nudge.

8. **CI placement.** (PR-3) The Aspire integration boot runs in the existing Docker-requiring tier
   (ADR 0002), gated so the offline tier skips it. New `packages.lock.json` files are committed so
   restore is deterministic.

## Alternatives considered

- **Adopt Aspire end-to-end, including `publish` for production.** Rejected: it collides with the
  self-hosted, agent-enrolled runtime model that F32–F34 own, and would couple production packaging to
  a tool chosen for the inner loop. The dev/test fence is the whole point of this ADR.
- **A hand-written Docker Compose file for dev.** Rejected for the inner loop: it gives the one-command
  boot but not the correlated dashboard, the strongly-typed project wiring, or the in-process
  integration-test harness (the main prize — asserting against a running Web with real container
  paths). Compose remains the likely *production* distribution shape under F34, which is a separate
  decision.
- **Adopt Aspire's stock ServiceDefaults.** Rejected (decision 4): double-instrumentation and a fight
  with the Serilog two-stage bootstrap, for no gain over our existing OTLP export.
- **Bump the shared `Microsoft.Extensions.Hosting` central pin to satisfy Aspire's floor.** Rejected in
  favour of a `VersionOverride` scoped to the AppHost, so a dev/test-only package does not move a
  production dependency's version. Revisit when the central pin next moves.

## Consequences

- One-command dev boot and an in-process integration harness that exercises real Web + Postgres (+
  Agent + wollomatic in PR-2) with injected config — removing a class of "works because I set three
  env vars by hand" bugs and giving UI/screenshot checks a dependency-complete app to run against.
- A standing invariant to police: "no Aspire in production `src/`." It is arch-test-enforced (PR-3),
  but every future contributor adding an Aspire integration must add it to the AppHost, not a host.
- The dev enrollment credential (PR-2) is real attack surface if it ever authenticates in production;
  it is well-known and fenced, with a guard test, but it is a cost we accept knowingly.
- We now track an Aspire version line (13.5.x) and its transitive floor (it forced the
  `Microsoft.Extensions.Hosting` override). Aspire moves fast; the pins and the override are revisited
  when the CLI or the central Hosting pin moves ([[packages-lock-sdk-drift]]).
- The dashboard and the optional MCP server (PR-2) expose logs/traces/runtime commands — dev-only
  surfaces this ADR forbids shipping.
