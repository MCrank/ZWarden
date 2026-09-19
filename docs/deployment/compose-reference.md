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

Until the Agent trusts the CA, enrollment's TLS handshake fails — but the Agent does **not** crash: it logs a
single actionable warning pointing here and retries enrollment in the background with capped backoff. Once you
apply the trust fix above (or switch to Public mode), the Agent enrols on the next retry with **no restart**.

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

## Where Project Zomboid data lives

Provisioned game servers keep their world data and backups on the **host** under `/srv/zwarden` — the world
tree at `/srv/zwarden/pz-data/<server-id>` and backups at `/srv/zwarden/pz-backups`. This is a persistent host
path (not `/tmp`, so a reboot or `tmpfiles` cleanup can't wipe worlds; ADR 0008 as amended). The stack prepares
it automatically on first `up` via the one-shot `pz-data-init` service, which creates the tree with the shared
ownership the Agent (uid 10001) and the game-server containers (uid 10000) both need. **Back up `/srv/zwarden`**
(and use the in-app backup feature, F24/F25) as part of your routine. To place it on a different disk, change the
`/srv/zwarden/...` host paths in the `pz-data-init` and `agent` service `volumes`, the Agent's
`Agent__DataMountRoot`/`Agent__BackupRoot`, and wollomatic's `-allowbindmountfrom` together (they must agree).

## The Project Zomboid admin account

Each server boots with an in-game administrator account. Set its password with `ZW_PZ_ADMIN_PASSWORD` (per
server, via the container environment); left unset, a strong one is generated on first boot and persisted under
the server's data directory. ZWarden itself manages servers over **RCON**, not this in-game account, so it is a
secondary credential.

> **Note:** Project Zomboid only accepts this password on the command line (`-adminpassword`), so it is visible
> in the container's **process table** (e.g. `docker top`) to anyone with Docker-daemon/host access. It is kept
> out of the container logs and is never baked into the image, but do not treat the generated admin password as
> a secret hardened against someone who already has host access to the box.

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

## Running signed release images

The Compose above builds the app images from source. A tagged release also **publishes** them to GHCR, signed
and with a full bill of materials (F40 / ADR 0039), so you can run vendor-built images and verify their
provenance instead of building your own. Each release (`v*`) carries these assets:

- `image-digests.txt` — the exact `@sha256:` digest of each of the three images (`zwarden-web`,
  `zwarden-agent`, `zwarden-pzserver`).
- `compose.release.yaml` — a self-contained Compose file already **pinned to those digests** (no `build:`).
- `sbom-*.cdx.json` — a CycloneDX SBOM per image (also attached to each image as a cosign attestation).
- `SHA256SUMS` — checksums over the assets above.

**Verify before you run.** Every image is signed with cosign keyless, so the signature's identity is this
repository's release workflow — there is no key to distribute or trust out of band:

```bash
# 1. Confirm the asset bundle is intact.
sha256sum -c SHA256SUMS

# 2. Verify each image's signature (identity = the release workflow, issuer = GitHub's OIDC).
cosign verify \
  --certificate-identity-regexp "https://github.com/MCrank/ZWarden/.github/workflows/release.yml@.*" \
  --certificate-oidc-issuer "https://token.actions.githubusercontent.com" \
  ghcr.io/mcrank/zwarden-web:vX.Y.Z

# 3. (Optional) Inspect the attested SBOM.
cosign verify-attestation --type cyclonedx \
  --certificate-identity-regexp "https://github.com/MCrank/ZWarden/.github/workflows/release.yml@.*" \
  --certificate-oidc-issuer "https://token.actions.githubusercontent.com" \
  ghcr.io/mcrank/zwarden-web:vX.Y.Z
```

Then run the pinned Compose (still supply your `.env`, and add the Postgres overlay if you use it):

```bash
docker compose -f compose.release.yaml up -d
```

Because the services are pinned by digest, `docker compose up` pulls the exact signed images — an upgrade is a
new release's `compose.release.yaml`, not a local rebuild.

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
