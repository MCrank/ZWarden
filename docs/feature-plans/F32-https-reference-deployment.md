# Feature 32 Mini-Plan — HTTPS Reference Deployment

**Status:** DONE (2026-09-15) — shipped in PR #137 (merged); issue #51 closed. Load-bearing decisions were
**LOCKED as-recommended with the maintainer (2026-09-15)**. Roadmap
issue: [F32 (#51)](https://github.com/MCrank/ZWarden/issues/51), **Track F — Deployment and release**. The
feature that puts a **Caddy 2.11.4 reverse-proxy ingress** in front of ZWarden.Web so a self-hosted operator
gets automatic HTTPS, an HTTP→HTTPS redirect, ACME/Let's Encrypt certificates, and transparent WebSocket
forwarding for the SignalR agent control plane — plus the **public and private deployment documentation** for
the three TLS modes. Caddy is the **sole front door**: `ZWarden.Web shall not need direct Internet exposure`
(PRD §45).

**Depends on [F4 (#26)](https://github.com/MCrank/ZWarden/issues/26) — DONE** (identity/auth) and
**[F10 (#32)](https://github.com/MCrank/ZWarden/issues/32) — DONE** (SignalR agent hub — the WebSocket path Caddy
must forward). Both merged. **Blocks [F33 (#52)](https://github.com/MCrank/ZWarden/issues/52)** (First-Run
Setup consumes F32's "TLS mode" concept) per the issue's native dependency edge, and feeds
[F34 (#53)](https://github.com/MCrank/ZWarden/issues/53) (Complete Docker Compose Distribution wires this Caddy
config into the shipped stack).

**Format:** PRD 60. **TDD is mandatory** (PRD 2.2). **Written against:**
[`scope-and-sequencing.md`](../scope-and-sequencing.md) §6 (F32) and §11 criterion 1 ("deploy the supported
stack with minimal setup", satisfied by F32+F33+F34 together); PRD **§45** (HTTPS / reverse proxy
responsibilities — Caddy, HTTPS termination, ACME/Let's Encrypt, HTTP-01, automatic renewal, HTTP→HTTPS
redirect, WebSocket forwarding, Web not internet-exposed) and **§46** (the three reference TLS modes:
**Public**, **Private**, **Existing reverse proxy**; DNS-01 is a future advanced mode);
[`docs/research/deployment-security-standards.md`](../research/deployment-security-standards.md) **§1** (the
verified Caddy 2.11.4 facts this plan relies on). ADRs it builds on:
[0031](../adr/0031-aspire-is-dev-test-orchestration-only.md) (Aspire is dev/test only — **Caddy does not enter
the Aspire graph**; production packaging stays F34) and
[0007](../adr/0007-agent-authentication.md) (v1.0 agent auth is outbound WSS via enrollment credential, not
mTLS — so Caddy terminates TLS and the agent connects over `wss://`). A new **ADR 0035** lands with the feature
(Caddy is the reference reverse-proxy ingress; the three-mode config contract; the `trusted_proxies` posture).

## Objective

Give a self-hosted operator a **drop-in Caddy configuration** that turns a bare ZWarden.Web instance into a
publicly reachable HTTPS control plane with zero manual certificate handling, and document the three deployment
shapes the PRD requires. F32 ships **the ingress layer and its documentation as a standalone artifact** — it is
the *ingredient* F34 later assembles into the full Compose distribution. Concretely:

- **HTTPS termination + ACME** — Caddy obtains and renews a Let's Encrypt certificate via **HTTP-01** (ports
  80/443 reachable), storing it under Caddy's data volume; renewal is automatic at 2/3 lifetime.
- **HTTP→HTTPS redirect** — Caddy's automatic **308 Permanent Redirect** from `:80` to `:443`.
- **WebSocket forwarding** — `reverse_proxy` to ZWarden.Web passes the SignalR agent hub (`/agent/hub`) and the
  live-log/console streams through transparently. Caddy v2 needs **no special directive** for WebSockets (it
  performs the HTTP upgrade natively — research §1.5); F32 asserts this rather than assuming it.
- **Three TLS modes (PRD §46)** documented and, where they are config, provided:
  - **Public** — public DNS hostname + Let's Encrypt (HTTP-01). The default Caddyfile.
  - **Private** — `tls internal` (Caddy's built-in CA) for an internal/air-gapped install with a locally
    trusted cert.
  - **Existing reverse proxy** — the operator brings their own ingress and cert management; ZWarden.Web is
    documented as accepting plain HTTP from that trusted upstream, and the `trusted_proxies` guidance.

F32 **does not ship the Compose stack** (that is F34), **does not add Caddy to the Aspire dev graph** (ADR
0031), and **has nothing to do with the hosted SaaS deployment** (that is v1.1 F33A). No C# domain/entity, no
migration — this is a reverse-proxy configuration artifact plus documentation plus a thin behavioral test.

## The load-bearing decisions (LOCKED as-recommended, 2026-09-15)

- **D-1 — F32 ships the Caddy ingress artifact + deployment docs only; not the Compose stack, not the Aspire
  graph, not SaaS. `[LOCKED]`** The deliverable is `deploy/caddy/` (Caddyfile + mode snippets) plus
  `docs/deployment/` (public + private guides). Caddy is deliberately **not** added to `src/ZWarden.AppHost`
  (ADR 0031 keeps Aspire on the ASP.NET dev-https endpoint and fences prod packaging into F34); the full
  Compose wiring — volumes, networks, both DB modes, secrets bootstrap — is **F34**. *Rejected — build the
  reference Compose file here so it "runs locally":* that pulls F34's scope forward and re-decides its volume /
  network / secrets shape prematurely; F32's job is the ingress ingredient and its contract, verifiable in
  isolation.

- **D-2 — One base Caddyfile parameterized by environment (`{$ZWARDEN_DOMAIN}`, `{$ZWARDEN_ACME_EMAIL}`,
  upstream), with the two non-default modes as documented drop-in snippets. `[LOCKED]`** Public mode is the
  base file. Private mode swaps the site's TLS to `tls internal`; existing-reverse-proxy mode documents removing
  Caddy entirely and pointing the operator's proxy at Web. *Rejected — three separate full Caddyfiles:* three
  near-identical files drift; a single parameterized base with mode deltas is what F34 will template anyway.

- **D-3 — WebSocket forwarding relies on Caddy v2's native upgrade handling — no `@websocket` matcher / manual
  header dance — and F32 asserts the upgrade end-to-end. `[LOCKED]`** The Caddyfile is a plain
  `reverse_proxy` to Web; `X-Forwarded-*` are set by Caddy automatically. Web trusts Caddy via
  `trusted_proxies` scoped to the private network (research §1.5 — incoming `X-Forwarded-*` are ignored unless
  the upstream is trusted). *Rejected — hand-rolled `Connection: Upgrade` header plumbing:* unnecessary on Caddy
  v2 and a common source of subtly-broken SignalR proxies.

- **D-4 — Test surface = `caddy validate` + `caddy fmt --diff` CI gate, plus one thin integration test
  (Caddy container + stub upstream, `tls internal`) asserting the HTTP→HTTPS **308** redirect and a **WebSocket
  upgrade** pass-through. `[LOCKED — maintainer, 2026-09-15]`** This honors mandatory TDD by testing the config's
  behavioral contract (redirect + WS) rather than Caddy's internals, using `tls internal` so no real ACME is
  needed in CI. *Rejected — validate/fmt only* (misses semantic breakage like a dropped `reverse_proxy`); *and
  a full ACME harness via Pebble across all three modes* (heaviest CI cost, pre-empts F34's stack assembly).

- **D-5 — HSTS is set explicitly, and security headers are the ingress's responsibility. `[LOCKED]`** Caddy
  does **not** emit HSTS automatically (research §1.2); the base Caddyfile adds a `header` directive for
  `Strict-Transport-Security` (and reviews the baseline security-header set) so the reference deployment is
  secure by default. *Rejected — assume automatic HTTPS implies HSTS:* it does not; omitting it ships an
  insecure-by-default reference.

## TDD plan (mandatory — PRD 2.2)

1. **`caddy validate` gate** — CI step runs `caddy validate --config deploy/caddy/Caddyfile` (and each mode
   snippet composed into a full config) so a malformed Caddyfile fails the build. `caddy fmt --diff` enforces
   canonical formatting.
2. **Integration test — redirect + WebSocket upgrade** (the one behavioral test, D-4). A test harness stands up
   Caddy (`tls internal`) in front of a minimal stub upstream that answers a WebSocket handshake, then asserts:
   (a) an HTTP request to `:80` returns **308** with an `https://` `Location`; (b) a `wss://` request completes
   the upgrade and echoes through the proxy. Lives in the integration-test tier; skipped gracefully where the
   Caddy binary/container is unavailable, consistent with the repo's environment-gated integration tests.
3. **Docs-as-contract check (light)** — an arch/doc test (or link check) asserting the three PRD §46 modes are
   each documented and the `deploy/caddy/` referenced files exist, so the deployment guide and the shipped
   config cannot silently diverge.

## Deliverables

- `deploy/caddy/Caddyfile` — the parameterized Public-mode base (HTTPS/ACME, 308 redirect, `reverse_proxy` to
  Web, explicit HSTS/security headers, `trusted_proxies`).
- `deploy/caddy/` mode snippets/notes — Private (`tls internal`) and Existing-reverse-proxy deltas.
- `docs/deployment/https-reference.md` (or equivalent) — public + private deployment documentation covering the
  three §46 TLS modes, DNS/port prerequisites (80/443), certificate lifecycle & renewal, and the DNS-01
  "future advanced mode" note.
- `docs/adr/0035-caddy-reference-reverse-proxy-ingress.md` — the ingress decision + three-mode contract +
  `trusted_proxies` posture.
- CI wiring for the `caddy validate`/`fmt` gate; the integration test.

## Out of scope

- The reference **Docker Compose** distribution, volumes, networks, secrets bootstrap, SQLite/Postgres mode
  wiring — **F34 (#53)**.
- **First-run setup** and TLS-mode selection UX — **F33 (#52)**.
- **Hosted SaaS** HTTPS/WebSocket configuration and multi-tenant ingress — **v1.1 F33A**.
- **DNS-01** ACME (requires an xcaddy-compiled provider plugin) — future advanced mode, documented only.
- **mTLS / private CA** for the agent channel — **v1.1** (ADR 0007); v1.0 uses outbound WSS.

## PR shape

Single PR (config + docs + ADR + the one integration test + CI gate) unless the integration-test harness proves
large enough to warrant splitting the test tier from the config/docs.
