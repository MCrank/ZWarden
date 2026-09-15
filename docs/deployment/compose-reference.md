# ZWarden reference deployment — Docker Compose

This guide stands up a complete self-hosted ZWarden control plane with Docker Compose (F34, ADR 0037). It
is the production twin of the dev/test Aspire graph (ADR 0031), minus Aspire. For the TLS/ingress details
behind it, see [`https-reference.md`](./https-reference.md) (F32, ADR 0035).

## What you get

```
                 Internet
                    │  80/443 (the only published ports)
              ┌─────▼─────┐
              │   Caddy   │  TLS termination, HTTP→HTTPS, ACME, WebSocket forwarding
              └─────┬─────┘
        frontend    │
              ┌─────▼─────┐        ┌───────────────┐
              │    Web    │◄───────│     Agent     │  wss (through Caddy)
              │ (control  │        │ (host worker) │
              │  plane)   │        └───────┬───────┘
              └─────┬─────┘   docker-proxy │ (internal only)
       backend(pg)  │                ┌─────▼──────┐
              ┌─────▼─────┐          │ wollomatic │  Docker socket-proxy (allowlisted)
              │ Postgres  │          └─────┬──────┘
              │ (pg mode) │                │ host Docker daemon
              └───────────┘          ┌─────▼──────────────┐
                                     │  PZ server(s)      │  created at runtime by the Agent (F13)
                                     └────────────────────┘
```

- **Caddy** is the sole front door: it terminates TLS and is the only service that publishes ports.
- **ZWarden.Web** (the control plane) is never exposed to the Internet directly (PRD §45).
- **ZWarden.Agent** reaches the Docker daemon only through **wollomatic**, a socket-proxy constrained to a
  fixed allowlist (ADR 0008). It connects to the control plane **outbound over `wss`** through Caddy.
- **Project Zomboid game servers are not Compose services** — the Agent creates them at runtime on the host
  Docker daemon once you register and start one.

## Prerequisites

- A Linux host with Docker Engine and the Compose v2 plugin.
- Ports **80 and 443** reachable from wherever your operators (and, in Public TLS mode, Let's Encrypt) connect.
- For Public TLS mode: a **public DNS record** pointing at the host.

## Quick start

```bash
cd deploy/compose

# 1. Generate the secrets (AES key ring + database password) into a git-ignored .env.
./bootstrap-secrets.sh            # Windows hosts: ./bootstrap-secrets.ps1

# 2. Set your hostname (and, for Public TLS, an ACME contact e-mail) in .env:
#      ZWARDEN_DOMAIN=zwarden.example.com
#      ZWARDEN_ACME_EMAIL=you@example.com

# 3. Bring up the stack (SQLite mode — the default).
docker compose up -d

# 4. Open https://zwarden.example.com and complete the First-Run setup wizard
#    (first administrator → TLS mode → optionally enroll the Agent). See "Enrolling the Agent" below.
```

That is the whole happy path: `bootstrap-secrets` → set the domain → `docker compose up -d` → finish in the
browser.

## Choosing a database mode

ZWarden ships **both** database providers (ADR 0005). Pick one at deploy time.

### SQLite mode (default)

The base `compose.yaml` runs SQLite with the database on a persisted named volume. Nothing extra to do — this
is what `docker compose up -d` uses. Best for a single host and a modest fleet.

### PostgreSQL mode

Add the overlay, which starts a `postgres` service and points Web at it:

```bash
docker compose -f compose.yaml -f compose.postgres.yaml up -d
```

The database password is the `POSTGRES_PASSWORD` your `bootstrap-secrets` run generated in `.env`. Postgres
lives on an internal-only network and is never host-published. **Use the same `-f compose.yaml -f
compose.postgres.yaml` pair for every subsequent command** (`ps`, `logs`, `down`, `up -d`), or Compose will
act on the SQLite topology instead.

> Switching modes on an existing install is a data migration, not a config flip — the two databases hold
> separate state. Decide before your first `up`.

## Secrets

`bootstrap-secrets.{sh,ps1}` writes a git-ignored `.env` containing:

- **`ZW_SECRET_KEYS` / `ZW_SECRET_ACTIVE_KEY_ID`** — the AES-256-GCM key ring the control plane fails closed
  without (ADR 0015). **Back this up.** Losing it makes every encrypted column (e.g. MFA secrets)
  undecryptable. The script refuses to overwrite an existing `.env` for exactly this reason.
- **`POSTGRES_PASSWORD`** — the database password (used only in PostgreSQL mode).

You supply the non-secret values by hand: `ZWARDEN_DOMAIN`, `ZWARDEN_ACME_EMAIL`, the one-time
`ZWARDEN_ENROLLMENT_SECRET` (below), and the PZ image reference.

`.env` values are visible via `docker inspect`. For a hardened install you can move them to Docker `secrets`
(mounted files); that is a supported upgrade, not the default. For a single-node self-hosted deployment the
`.env` baseline is the documented norm.

## TLS modes

The three modes come from [`https-reference.md`](./https-reference.md) (PRD §46):

- **Public** — a public DNS hostname with an automatic Let's Encrypt certificate. The default `deploy/caddy/Caddyfile`.
- **Private** — `tls internal` (Caddy's built-in CA) for an internal/air-gapped install. Mount
  `deploy/caddy/Caddyfile.internal` over Caddy's Caddyfile instead of the base one.
- **Existing reverse proxy** — you run your own ingress; drop Caddy and point your proxy at the `web` service.

### Private mode: trusting Caddy's CA in the Agent

In Private mode Caddy serves a certificate from its **own** CA, which nothing trusts by default — including
the co-located Agent, which connects over `wss://${ZWARDEN_DOMAIN}` through Caddy (D-8). You must make the
Agent trust that CA, or its control-plane connection fails the TLS handshake. Two options:

1. **Use Public mode** (a publicly trusted certificate) if the host can reach Let's Encrypt — simplest.
2. **Bake Caddy's root CA into the Agent image.** Export the root once Caddy has generated it:

   ```bash
   docker compose cp caddy:/data/caddy/pki/authorities/local/root.crt ./caddy-root.crt
   ```

   Then extend the Agent image with a tiny Dockerfile that installs it into the OS trust store, and point the
   `agent` service's `build.dockerfile` at it:

   ```dockerfile
   FROM zwarden/agent:latest
   USER root
   COPY caddy-root.crt /usr/local/share/ca-certificates/caddy-root.crt
   RUN update-ca-certificates
   USER 10001
   ```

   Rebuild: `docker compose -f compose.yaml … up -d --build agent`.

## Enrolling the Agent

Agent enrollment is operator-driven (D-4): the stack does not bake a shared enrollment secret.

1. Bring the stack up and finish the **First-Run** wizard. On the enrollment step it issues a **one-time,
   short-lived enrollment secret** (F9).
2. Paste it into `.env` as `ZWARDEN_ENROLLMENT_SECRET=…`.
3. `docker compose up -d agent` (add the `-f` overlay pair in PostgreSQL mode).

The Agent enrols once, persists its own per-Agent credential on the `agent_state` volume, and reuses it across
restarts — so you can blank `ZWARDEN_ENROLLMENT_SECRET` again afterwards. To run game servers, set
`ZWARDEN_PZ_IMAGE` to a pinned `repo@sha256:…` digest of the PZ image built from `src/ZWarden.PZServer` (a
floating tag like `:latest` is rejected, ADR 0008).

## Upgrade

ZWarden.Web applies any new database migrations automatically on startup, so an upgrade is pull-and-recreate:

```bash
# 1. Back up first (see the F24/F25 backup & restore features) — always, before an upgrade.
# 2. Pull new base images and rebuild the app images.
docker compose pull
docker compose build --pull

# 3. Recreate. Web runs pending migrations before serving; health-ordered startup handles the sequencing.
docker compose up -d
```

In PostgreSQL mode, add `-f compose.yaml -f compose.postgres.yaml` to each command. Because migrations run on
boot, roll forward one version at a time and keep the pre-upgrade backup until you have confirmed the new
version is healthy. To roll back you restore the backup and redeploy the previous image tags.

## Health & troubleshooting

- `docker compose ps` shows health. Web is healthy once `/healthz` answers 200; Caddy and the Agent wait for
  that before they start doing work.
- `docker compose logs -f web` / `agent` / `caddy` for live logs.
- **Web unhealthy:** almost always a missing/blank `ZW_SECRET_KEYS` (it fails closed) or a bad connection
  string. Check `docker compose logs web`.
- **Agent won't connect:** in Private TLS mode, confirm it trusts Caddy's CA (above); otherwise confirm the
  enrollment secret was issued by the wizard and is current (it is single-use and short-lived).
- **Certificate issues in Public mode:** confirm 80/443 are reachable and DNS points at the host — Caddy needs
  the HTTP-01 challenge to succeed.
