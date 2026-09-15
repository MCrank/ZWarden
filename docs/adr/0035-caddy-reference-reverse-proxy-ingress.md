# 35. Caddy is the reference reverse-proxy ingress; three TLS modes; the app trusts the proxy

ZWarden's reference deployment terminates HTTPS at **Caddy 2.11.4**, the **sole front door**: it obtains and
renews certificates automatically, redirects HTTP to HTTPS, and forwards every request — including the SignalR
agent-hub WebSocket and the live-log/console streams — to ZWarden.Web, which is **never exposed to the Internet
directly** (PRD §45). The ingress config is **one base Caddyfile parameterized by environment** covering the
**Public** mode (a public DNS hostname with a Let's Encrypt certificate over the HTTP-01 challenge), with the
**Private** (`tls internal`, Caddy's built-in CA) and **Existing reverse proxy** (operator-provided ingress)
modes of PRD §46 as documented variants. Because only the ingress can reach ZWarden.Web, the app **trusts the
proxy's `X-Forwarded-*`** headers (honouring the original scheme/host/client-IP) so HTTPS redirection and secure
cookies behave correctly behind TLS termination.

- Status: accepted
- Decided in: #51 (F32 — HTTPS Reference Deployment); mini-plan `docs/feature-plans/F32-https-reference-deployment.md`
- Bears on: PRD §45 (HTTPS / reverse-proxy responsibilities; Web not Internet-exposed), PRD §46 (the three
  reference TLS modes; DNS-01 as a future advanced mode), scope-and-sequencing §6 (F32) and §11 criterion 1
  ("deploy the supported stack with minimal setup", satisfied by F32+F33+F34 together);
  `docs/research/deployment-security-standards.md` §1 (the verified Caddy 2.11.4 facts). Builds on ADR 0031
  (Aspire is dev/test only — Caddy does **not** enter the Aspire graph; production packaging stays F34), ADR
  0007 (v1.0 agent auth is outbound WSS via enrollment credential, not mTLS, so the agent connects through
  Caddy's TLS), and ADR 0006 (host-header filtering, which still validates the forwarded Host). Feeds F33
  (First-Run Setup consumes the "TLS mode" concept) and F34 (the Compose distribution wires this Caddyfile into
  the shipped stack).

## Context

ZWarden.Web serves the control-plane UI/API and the SignalR endpoint agents connect to. It must be reachable
over HTTPS with a valid certificate, and the agent control plane (F10) and the live-log/console streams
(F27/F28) ride WebSocket upgrades that the edge must forward transparently. PRD §45 fixes the answer's shape:
a reverse proxy terminates TLS, handles ACME/Let's Encrypt issuance and automatic renewal, redirects HTTP to
HTTPS, forwards WebSockets, and shields ZWarden.Web from direct Internet exposure. PRD §46 requires three
reference TLS modes — Public (public DNS + Let's Encrypt), Private (an internal/locally-trusted CA), and
Existing reverse proxy (bring-your-own ingress) — and names DNS-01 a future advanced mode. The verified research
(`deployment-security-standards.md` §1) establishes the load-bearing Caddy facts: automatic HTTPS redirects with
a **308**; Caddy v2 forwards **WebSockets natively** with no special directive; **HSTS is not automatic**;
`localhost`/IP/`*.localhost` site addresses auto-select the **internal issuer** (no ACME); and `tls internal`
exposes Caddy's built-in CA for the Private mode.

Two forces shape the decision. First, F32 is the ingress **ingredient**, not the deployment: the Compose stack
(volumes, networks, both DB modes, secrets bootstrap) is F34, and Aspire is dev/test only (ADR 0031), so Caddy
belongs in neither the Aspire graph nor a compose file shipped here. Second, TLS termination at the edge is only
correct if the app behind it knows the request's original scheme: without honouring `X-Forwarded-Proto`,
`UseHttpsRedirection` loops on an already-HTTPS request and secure cookies are mis-scoped — so the app must trust
the proxy, and ASP.NET Core ignores forwarded headers from untrusted sources by default.

## Decision

1. **Caddy 2.11.4 is the reference ingress and the sole front door.** It terminates HTTPS, runs ACME, redirects
   HTTP→HTTPS, and reverse-proxies to ZWarden.Web. ZWarden.Web is never bound to a public interface; only the
   ingress reaches it (PRD §45).

2. **One base Caddyfile, parameterized by environment.** `deploy/caddy/Caddyfile` is the **Public** mode:
   `{$ZWARDEN_DOMAIN}` selects the site (and, when it is `localhost`/an IP, Caddy's internal issuer, so no ACME),
   and `{$ZWARDEN_UPSTREAM:web:8080}` is the ZWarden.Web address. The **Private** mode adds `tls internal`
   (`deploy/caddy/Caddyfile.internal` is the worked example); the **Existing reverse proxy** mode removes Caddy
   entirely and points the operator's proxy at ZWarden.Web (documentation only). Three separate full Caddyfiles
   were rejected — they drift; a base plus deltas is what F34 will template.

3. **WebSocket forwarding relies on Caddy v2's native upgrade handling.** The Caddyfile is a plain
   `reverse_proxy`; no `@websocket` matcher or manual `Connection: Upgrade` plumbing (unnecessary on Caddy v2 and
   a common source of subtly-broken SignalR proxies).

4. **HSTS is set explicitly.** Automatic HTTPS does not emit HSTS, so the base Caddyfile adds a
   `Strict-Transport-Security` header — the reference deployment is secure by default.

5. **ZWarden.Web trusts the ingress' forwarded headers.** The app honours `X-Forwarded-Proto`/`Host`/`For`
   (`app.UseForwardedHeaders()` first in the pipeline, `AddProxyForwardedHeaders`) and **clears the default
   loopback-only proxy allowlists**, trusting the forwarding proxy regardless of its container-assigned address.
   This is safe because only the ingress can reach ZWarden.Web (PRD §45) and host filtering (ADR 0006) still
   validates the forwarded Host. At the **edge**, Caddy does **not** trust inbound `X-Forwarded-*` (no
   `trusted_proxies`) — it sets them from the real connection.

## Consequences

- The reference deployment works end-to-end behind TLS termination: no redirect loop, correct secure-cookie
  scoping, and transparent agent-hub/stream WebSockets. A networked integration test
  (`CaddyReferenceDeploymentTests`) runs the real `caddy:2.11.4` image with the committed Caddyfile and asserts
  the 308 redirect and a WebSocket upgrade pass-through; a `caddy validate`/`caddy fmt` CI gate keeps the config
  well-formed; offline tests assert the forwarded-headers options and that the three modes stay documented.
- Trusting the proxy depends on the PRD §45 invariant. If ZWarden.Web is ever bound to a public interface, the
  cleared allowlists would let a client spoof `X-Forwarded-*`. The invariant is the control; F34's Compose
  networking must preserve it, and F40's trust-boundary review must confirm it.
- DNS-01 (wildcard / no inbound 80/443) needs an xcaddy-compiled provider plugin and is out of scope here —
  documented as the future advanced mode. mTLS / a private CA for the agent channel remains v1.1 (ADR 0007).
- The hosted SaaS ingress (multi-tenant HTTPS/WebSocket) is v1.1 (F33A) and is not governed by this ADR.
