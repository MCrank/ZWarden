# Feature 34 Mini-Plan — Complete Docker Compose Distribution

**Status:** IN PROGRESS (2026-09-15) — load-bearing decisions **LOCKED as-recommended with the maintainer
(2026-09-15)**: D-2 SQLite base + Postgres override; D-3 `.env` + bootstrap script; D-4 wizard-driven
enrollment; D-7 two PRs. Roadmap
issue: [F34 (#53)](https://github.com/MCrank/ZWarden/issues/53), **Track F — Deployment and release**. The
feature that assembles every shipped component into **one reference `docker compose` distribution** a
self-hosted operator can pull and run: Caddy ingress (F32) in front of ZWarden.Web, the ZWarden.Agent host
worker, the wollomatic Docker socket-proxy the Agent's Docker path runs through (ADR 0008), and a database in
**either SQLite mode or PostgreSQL mode** — with a secrets bootstrap, health-ordered startup, and
upgrade documentation. This is the deployment §11 criterion 1 ("deploy the supported stack with minimal
setup") delivered as the F32+F33+F34 triad's final piece.

**Depends on [F33 (#52)](https://github.com/MCrank/ZWarden/issues/52) — DONE** (First-Run Setup — the guided
admin/TLS/enroll wizard the operator runs *after* the stack is up) **and Track D complete** — wired as native
dependency edges to the Track D leaves **F19 (#41)**, **F22 (#44)**, **F25 (#46)**, **F27 (#47)**, all DONE
(every other Track D feature is a transitive prerequisite of one of those leaves). **Blocks [F35 (#54)](https://github.com/MCrank/ZWarden/issues/54)** (Remote
Agent / Multi-Host — a remote Agent installs against the same control plane this stack stands up).

**Format:** PRD 60. **TDD is mandatory** (PRD 2.2). **Written against:**
[`scope-and-sequencing.md`](../scope-and-sequencing.md) §6 (F34) and §11 criterion 1; the shipped F32 ingress
artifact ([`deploy/caddy/`](../../deploy/caddy) + [`docs/deployment/https-reference.md`](../deployment/https-reference.md));
the F12 PZ container ([`src/ZWarden.PZServer/Dockerfile`](../../src/ZWarden.PZServer/Dockerfile)); and the
dev/test orchestration that already wires the same graph in Aspire
([`src/ZWarden.AppHost/AppHost.cs`](../../src/ZWarden.AppHost/AppHost.cs)) — F34 is the **production twin** of
that graph, minus Aspire (ADR 0031). ADRs it builds on: **0031** (Aspire is dev/test only — F34 owns
production packaging), **0035** (Caddy is the reference ingress; F34 wires its Caddyfile into the stack),
**0008** (the Agent reaches Docker only through wollomatic), **0015** (the fail-closed AES key ring from
`ZW_SECRET_KEYS`), **0005** (dual provider, chosen by `ZWarden:Database:Provider`), **0036** (First-Run Setup
is how the operator finishes bootstrap after the stack is up). A new **ADR 0037** lands with the feature (the
reference Compose distribution: component topology, the two DB modes, the secrets/enrollment bootstrap
contract, and the health-dependency ordering).

## Objective

Turn "here are twelve projects and a Caddyfile" into **`git clone`, `./bootstrap-secrets.sh`, `docker compose
up -d`, open the browser, finish the First-Run wizard**. F34 ships the packaging and its documentation as a
standalone deployable artifact under `deploy/compose/`. Concretely:

- **Two application images** — new multi-stage Dockerfiles for **ZWarden.Web** and **ZWarden.Agent** (neither
  exists yet; only PZServer has one). Chiselled/distroless .NET 10 runtime, non-root, pinned base by digest.
- **The reference stack** — `compose.yaml` wiring **Caddy → Web → Agent → wollomatic**, reusing the F32
  Caddyfile verbatim (`{$ZWARDEN_DOMAIN}`/`{$ZWARDEN_ACME_EMAIL}`/upstream parameters) and the F13 allowlist
  verbatim on wollomatic.
- **SQLite mode and PostgreSQL mode** — SQLite is the base (single-node, one persisted volume, zero extra
  services); PostgreSQL mode adds the `postgres` service and flips the Web service's provider + connection
  string.
- **Secrets bootstrap** — a script that generates the AES key ring (`ZW_SECRET_KEYS`/`ZW_SECRET_ACTIVE_KEY_ID`,
  fail-closed per ADR 0015) and the Postgres password into a git-ignored `.env` from `.env.example`.
- **Health-ordered startup** — every service carries a healthcheck; `depends_on: condition: service_healthy`
  sequences `postgres → web`, `wollomatic → agent`, `web → agent`. Web gains a minimal liveness endpoint for
  its own healthcheck (it has none today).
- **Upgrade documentation** — pull-new-images / recreate / migration-on-boot / backup-first guidance under
  `docs/deployment/`.

F34 **does not** deploy or govern production remotely (that is out of v1.0), **does not** add anything to the
Aspire graph (ADR 0031), **does not** run PZ game servers as compose services (the Agent creates those
dynamically on the host daemon via wollomatic — F13), and **does not** own the hosted-SaaS deployment (v1.1).

## The PZ-container clarification (load-bearing context, not a decision)

"The three components" in the PRD are **Web, Agent, and the PZ runtime**. Only Web and Agent are long-lived
`compose` services. **PZ server containers are never compose services** — the Agent spawns them at runtime from
the F12 image (via the closed create template, F13), on the same Docker daemon wollomatic fronts. The compose
distribution therefore stands up the *control plane and its host worker*; the game servers appear as
Agent-managed containers once an operator registers/starts one. This is documented, not built.

## The load-bearing decisions (LOCKED with the maintainer, 2026-09-15)

- **D-1 — F34 ships `deploy/compose/` = two new app Dockerfiles + `compose.yaml` + Postgres overlay + `.env`
  bootstrap + deployment/upgrade docs; it reuses F32's Caddyfile and F12's PZServer image unchanged.
  `[LOCKED]`** *Rejected — reuse `docker compose` against the projects directly (à la Aspire):* Aspire is
  the dev graph (ADR 0031); production needs self-contained, pinned, non-root images and a hand-authored
  compose, not a dev orchestrator's generated one.

- **D-2 — SQLite is the base `compose.yaml`; PostgreSQL is an override file
  (`docker compose -f compose.yaml -f compose.postgres.yaml up -d`). `[LOCKED]`** The override adds the
  `postgres` service (+ its volume, healthcheck, `depends_on`) and overrides only Web's
  `ZWarden__Database__Provider=postgres` and `ConnectionStrings__ZWarden`. *Rejected — compose `profiles`:*
  a profile can add the postgres service but cannot conditionally swap Web's provider env, so it still needs a
  second env source — the override file is the honest mechanism and the one the docs can show as one command.
  *Rejected — two full separate compose files:* they drift; the base+overlay keeps one source of truth.

- **D-3 — Secrets live in a git-ignored `.env` generated by `bootstrap-secrets.sh` (+ a `.ps1` twin for
  Windows hosts); Docker `secrets` files are documented as the hardening upgrade, not the v1.0 default.
  `[LOCKED]`** The script generates a 32-byte base64 key ring and a strong Postgres password, never
  overwriting an existing `.env`. *Rejected — Docker secrets from day one:* heavier (swarm/compose-secrets
  semantics, file mounts, app changes to read `*_FILE`), and for a single-node self-hosted install the `.env`
  is the idiomatic, documented baseline; we note the `docker inspect` exposure and the upgrade path.

- **D-4 — Agent enrollment is operator-driven through the F33 First-Run wizard, NOT auto-seeded.
  `[LOCKED]`** Unlike the Aspire dev graph (which pre-seeds `zwe_dev-…` so it self-enrolls), the prod stack
  brings up Caddy+Web first; the operator runs the wizard (admin → TLS → **enroll**), the wizard issues a
  one-time enrollment secret, the operator drops it into `.env` as `Agent__EnrollmentSecret`, and the Agent
  service self-enrols once on (re)start, then connects with its issued per-Agent credential (F9). *Rejected —
  bake a shared enrollment secret into `.env` at bootstrap:* that resurrects the dev-only shortcut the
  D-ENROLL guard forbids outside Development and undermines F9's single-use, short-lived enrollment rule
  (PRD 63A). The docs make the "wizard first, then start the Agent" order explicit.

- **D-5 — Health-ordered startup via per-service healthchecks + `depends_on: condition: service_healthy`;
  Web gains a minimal unauthenticated liveness endpoint (`/healthz`) purely for its container healthcheck.
  `[LOCKED]`** Ordering: `postgres`(pg_isready) → `web`(/healthz) ; `wollomatic`(_ping) → `agent` ;
  `web` → `agent`. Caddy has no hard dependency (it retries the upstream). *Rejected — reuse F16's health
  model for the container probe:* F16 is PZ-server health, authenticated and domain-shaped; a container
  liveness probe must be unauthenticated, dependency-free, and cheap — a separate concern.

- **D-6 — TDD: an always-on **tier-1** structural test parses the compose YAML and asserts the invariants
  (services present, non-root, healthchecks + `depends_on` conditions wired, both DB modes' provider/conn
  wiring, and the **wollomatic allowlist matches the F13 drift set verbatim**), plus a **tier-2** networked
  smoke test that `docker compose up`s the SQLite stack and asserts `/healthz` green through Caddy.
  `[LOCKED]`** Mirrors `CaddyReferenceDeploymentTests` (tier-2, `[Category("Networked")]`) and the F13
  allowlist drift test. The tier-1 test needs no Docker so it guards trunk on every push.

- **D-7 — Ship in two PRs to respect the ~100K guardrail. `[LOCKED]`**
  - **PR-A — images + base stack (SQLite):** Web+Agent Dockerfiles, `/healthz`, `compose.yaml`,
    `.env.example` + `bootstrap-secrets.{sh,ps1}`, tier-1 structural test, ADR 0037.
  - **PR-B — Postgres mode + docs + smoke:** `compose.postgres.yaml`, the deployment/upgrade guide under
    `docs/deployment/`, tier-2 networked smoke test, `.gitignore`/README wiring.
  *Rejected — one PR:* two new Dockerfiles + dual-mode compose + bootstrap + docs + two test tiers is over
  the guardrail; the image/stack slice is independently reviewable and testable.

- **D-8 — The co-located Agent reaches the control plane over `wss://${ZWARDEN_DOMAIN}` through Caddy (a
  Caddy network alias makes the domain resolve inside the stack), NOT directly to Web. `[LOCKED]`** Forced by
  F8's validator (`ControlPlaneUri` must be `https`/`wss`, fail-closed) and consistent with ADR 0007's
  outbound-WSS model. Public mode works out of the box (trusted Let's Encrypt cert); **Private (`tls internal`)
  mode requires the operator to trust Caddy's root CA in the Agent container** — exactly the step F32's
  `Caddyfile.internal` and https-reference guide already call out; the F34 deployment guide (PR-B) gives the
  concrete mount. *Rejected — relax the validator to allow `http://web:8080` on the internal network:* weakens
  F8's fail-closed scheme rule for a dev-shaped shortcut; the alias+wss path keeps prod honest.

## Networks & hardening (implementation shape, not a new decision)

Two networks: **`frontend`** (caddy ↔ web, agent → caddy) and **`docker-proxy`** (`internal: true`, fixed
subnet; agent → wollomatic only). Only **caddy** publishes ports (80/443); Web, the Agent, wollomatic, and any
Postgres are never host-published. wollomatic sits on the internal-only network with `allowfrom` scoped to that
subnet (a prod tightening over the dev graph's `0.0.0.0/0`), carrying the **same GET/HEAD/POST/bindmount
allowlist the F13 drift tests enforce, verbatim**. Every image runs **non-root**.

## Out of scope / explicitly deferred

- Remote/multi-host Agent installation (**F35 #54**), production remote governance (not v1.0).
- Docker `secrets`/vault integration (documented upgrade, not default — D-3).
- SBOM, image signing, provenance, container scanning (**F40**, PRD 54/55).
- Any Aspire change (ADR 0031). Hosted-SaaS deployment (v1.1).

## Progress

- **PR-A (images + base SQLite stack)** — Web+Agent multi-stage Dockerfiles (non-root, Debian runtime),
  `deploy/compose/compose.yaml` (Caddy/Web/Agent/wollomatic, two networks, health-ordered), `.env.example` +
  `.gitignore` + `bootstrap-secrets.{sh,ps1}`, Web `/healthz` (allow-listed past the first-run gate), the
  tier-1 `ComposeDistributionTests` guard (15 tests) + the `/healthz` Web test, ADR 0037. `docker compose
  config` renders; both Dockerfiles pass `docker build --check`. Floors bumped: ArchitectureTests 23→38,
  Web.Tests 218→219 (csproj + ci.yml guard).
- **PR-B (Postgres mode + docs + smoke)** — `compose.postgres.yaml` overlay, the `docs/deployment/` guide
  (SQLite/Postgres, secrets, upgrade, Private-mode Agent CA trust), the tier-2 networked boot smoke test.

## Verification

- `dotnet test` tier-1 green (structural compose invariants) on every push; tier-2 networked smoke green in
  the opt-in tier.
- Manual acceptance: fresh clone → bootstrap → `docker compose up -d` → First-Run wizard completes → register
  & start a PZ server via the Agent → repeat with the Postgres overlay.
