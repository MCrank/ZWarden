# HTTPS reference deployment (Caddy)

ZWarden's reference deployment puts **[Caddy](https://caddyserver.com) 2.11.4** in front of ZWarden.Web as the
**sole front door**. Caddy terminates HTTPS, obtains and renews certificates automatically, redirects HTTP to
HTTPS, and forwards every request — including the SignalR agent-hub WebSocket and the live-log/console streams —
to ZWarden.Web. **ZWarden.Web is never exposed to the Internet directly**; only Caddy reaches it (PRD §45).

This document is the F32 deliverable: the ingress configuration and its deployment guidance. The full
Docker Compose distribution that wires Caddy, ZWarden.Web, the Agent, and the database together is **F34** — this
guide describes the ingress layer that distribution assembles. The decision record is
[ADR 0035](../adr/0035-caddy-reference-reverse-proxy-ingress.md).

## Configuration

The ingress is a single, environment-parameterized Caddyfile:

| File | Mode | Certificate source |
| --- | --- | --- |
| [`deploy/caddy/Caddyfile`](../../deploy/caddy/Caddyfile) | **Public** (default) | Let's Encrypt via HTTP-01 |
| [`deploy/caddy/Caddyfile.internal`](../../deploy/caddy/Caddyfile.internal) | **Private** | Caddy's built-in CA (`tls internal`) |
| *(none — remove Caddy)* | **Existing reverse proxy** | operator-provided |

Two environment variables parameterize it:

| Variable | Meaning | Example |
| --- | --- | --- |
| `ZWARDEN_DOMAIN` | the hostname clients use to reach ZWarden | `zwarden.example.com` |
| `ZWARDEN_UPSTREAM` | the ZWarden.Web address Caddy proxies to (default `web:8080`) | `web:8080` |

## The three TLS modes (PRD §46)

### Public — public DNS hostname with Let's Encrypt

The default. Point a public DNS `A`/`AAAA` record at the host, set `ZWARDEN_DOMAIN` to that hostname, and Caddy
obtains a Let's Encrypt certificate over the **HTTP-01** challenge on first start.

Prerequisites:

- **Ports 80 and 443 must be reachable from the Internet.** HTTP-01 validation and the HTTP→HTTPS redirect both
  require port 80; HTTPS serves on 443.
- The DNS name must already resolve to the host before first start.
- **Optional (recommended):** set an ACME contact e-mail so the CA can send expiry/security notices. Let's
  Encrypt does **not** require one, and Caddy errors on an empty `email` directive, so it is intentionally not
  wired as an environment variable — set it directly by adding a global options block at the top of
  [`deploy/caddy/Caddyfile`](../../deploy/caddy/Caddyfile):

  ```caddyfile
  {
  	email ops@example.com
  }
  ```

Caddy stores certificates under its data directory (`/data` in the official image — persist it with a volume so
renewals and account keys survive restarts) and renews automatically at roughly two-thirds of the certificate
lifetime.

### Private — internal / locally-trusted CA

For an internal or air-gapped install that cannot reach a public CA, use
[`deploy/caddy/Caddyfile.internal`](../../deploy/caddy/Caddyfile.internal), whose only difference is a
`tls internal` directive. Caddy issues the site certificate from its **own built-in CA**.

Clients — browsers **and the ZWarden Agent** — must trust that CA. Export Caddy's root certificate and install it
in each client's trust store:

```sh
# Copy the root CA out of the running Caddy container/data directory:
#   <data>/caddy/pki/authorities/local/root.crt
# then install it into the OS/container trust store the Agent and browsers use.
```

Set `ZWARDEN_DOMAIN` to the internal hostname (for example `zwarden.lan`); it must resolve on the internal
network.

### Existing reverse proxy — bring your own ingress

If you already run an ingress (nginx, Traefik, a cloud load balancer, corporate TLS), **do not run Caddy**. Point
your proxy at ZWarden.Web directly and let it manage certificates. Your proxy must:

- terminate TLS and **forward WebSocket upgrades** to ZWarden.Web (the agent hub and log/console streams depend
  on it);
- send **`X-Forwarded-Proto`**, `X-Forwarded-Host`, and `X-Forwarded-For` — ZWarden.Web trusts these to learn the
  original request scheme (see *Forwarded headers* below);
- reach ZWarden.Web over the private network only. ZWarden.Web accepts plain HTTP **from the trusted proxy**; it
  must never be bound to a public interface.

## Forwarded headers and proxy trust

ZWarden.Web trusts the ingress' `X-Forwarded-Proto`/`Host`/`For` headers so that, behind TLS termination, HTTPS
redirection does not loop and secure cookies are scoped correctly (`app.UseForwardedHeaders()`, ADR 0035 §5).
This trust is safe **only** because ZWarden.Web is never exposed to the Internet directly — the deployment must
preserve that invariant. At the edge, Caddy itself does not trust inbound `X-Forwarded-*`; it sets them from the
real client connection.

## Certificate lifecycle

- **Issuance** happens on first start (Public: Let's Encrypt/HTTP-01; Private: the internal CA).
- **Renewal** is automatic; persist Caddy's data directory so certificates and ACME account keys survive
  restarts.
- **HSTS** is sent explicitly (`Strict-Transport-Security`) — Caddy's automatic HTTPS does not add it on its own.

## Future advanced mode: ACME DNS-01

DNS-01 (for wildcard certificates, or when inbound 80/443 is unavailable) requires a DNS-provider plugin compiled
into Caddy with `xcaddy` — it is **not** in the stock image. It is out of scope for the v1.0 reference deployment
and documented here only as the intended advanced mode.

## Verifying the config

The committed Caddyfiles are checked in CI (`caddy validate` + `caddy fmt`). To validate locally with a Caddy
container:

```sh
docker run --rm -e ZWARDEN_DOMAIN=zwarden.example.com \
  -v "$PWD/deploy/caddy/Caddyfile:/etc/caddy/Caddyfile:ro" \
  caddy:2.11.4 caddy validate --config /etc/caddy/Caddyfile --adapter caddyfile
```

For a quick end-to-end smoke test without a public CA, set `ZWARDEN_DOMAIN=localhost`: Caddy automatically uses
its internal issuer, so it serves HTTPS and redirects HTTP with no ACME round-trip. This is exactly what the
`CaddyReferenceDeploymentTests` integration test does against the real image.
