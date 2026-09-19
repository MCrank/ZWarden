# Add a remote host (remote Agent)

*Feature 35 · ADR 0038 · satisfies PRD §11 criterion 14 (manage remote servers without privileged inbound
ports).*

ZWarden manages Project Zomboid servers across **more than one host**. Each host runs a **ZWarden.Agent** — the
same component the reference stack co-locates with the control plane ([compose-reference.md](./compose-reference.md)),
just on its own machine. The Agent dials the control plane **outbound over `wss://`**, so a remote host needs
**no inbound port open for ZWarden** — only the ability to make outbound HTTPS/WSS connections. This guide adds
one remote host to an existing control plane.

> **You need a running control plane first.** Stand it up with the reference distribution and finish the
> First-Run wizard before adding remote hosts. The remote Agent holds **no database and no key ring** — it
> carries only a one-time enrollment secret and, after enrolling, its own per-Agent credential.

## What ships

`deploy/compose/remote-agent/`:

| File | Purpose |
| --- | --- |
| `compose.yaml` | The Agent + the wollomatic socket-proxy (ADR 0008). No Caddy, no Web. |
| `.env.example` | The two required settings (control-plane domain, one-time enrollment secret) + PZ image. |
| `bootstrap-env.sh` / `.ps1` | Scaffolds a git-ignored `.env` from the template (generates no secrets). |
| `.gitignore` | Keeps `.env` (your enrollment secret) out of git. |

The wollomatic allowlist is **identical** to the reference stack and the F13 runtime — a remote host is not a
relaxed host. An offline test pins all copies together so they cannot drift.

## Prerequisites on the remote host

- Docker Engine + the Compose plugin.
- Outbound reachability to the control plane's domain on 443 (WSS). **No inbound rule is required.**
- The repository checked out (the Agent image builds from it), or access to a pre-built `zwarden/agent` image.

## Steps

### 1. Get a one-time enrollment secret from the control plane

On the control plane, sign in and open the **Enroll** flow (the First-Run wizard's enroll step, or mint one as
an operator). It issues a **single-use, short-lived** enrollment secret. Copy it — you will paste it on the
remote host next.

### 2. Scaffold the environment on the remote host

```bash
cd deploy/compose/remote-agent
./bootstrap-env.sh          # or: pwsh ./bootstrap-env.ps1   (Windows)
```

Edit `.env`:

- `ZWARDEN_DOMAIN` — the **same public domain** the control plane serves (e.g. `zwarden.example.com`). The
  Agent connects to `wss://<domain>/agent/hub`.
- `ZWARDEN_ENROLLMENT_SECRET` — paste the secret from step 1.
- `ZWARDEN_PZ_IMAGE` — optional now; a **specific** PZ image reference (any tag except a floating `:latest`,
  which is rejected — ADR 0008), needed before you provision a server on this host. A `repo@sha256:…` digest is
  the hardened production form; a locally built, unpushed image has no digest, so pin its build tag.

### 3. Bring the Agent up

```bash
docker compose up -d
```

The Agent enrols **once** with the secret, persists its own per-Agent credential on the `agent_state` volume,
and connects. A container restart reuses that credential — it never re-enrols. You can blank
`ZWARDEN_ENROLLMENT_SECRET` in `.env` again afterward.

### 4. Verify the host appears

Back on the control plane, open **`/hosts`**. The new host shows up with its self-reported **hostname**, agent
version and OS, and a **Connected** indicator. Register or import servers on it exactly as on the co-located
host — the host picker on `/servers` now offers this Agent as a target.

## Private (internal-CA) TLS mode

If the control plane runs Caddy in **Private** mode (`tls internal`, from
[https-reference.md](./https-reference.md)), its certificate is signed by Caddy's **own CA**, which the remote
Agent's container does not trust by default — the outbound wss handshake will fail. Export the control plane's
Caddy **root CA** and mount it into the Agent's trust store (the same step the co-located guide describes), for
example by bind-mounting the CA file and pointing the container's CA bundle at it. In **Public** TLS mode
(Let's Encrypt), the certificate chains to a public root and this step is unnecessary.

## Troubleshooting

- **The host never appears in `/hosts`.** Check `docker compose logs agent`. A `wss` connection error usually
  means DNS/routing to `ZWARDEN_DOMAIN` or, in Private mode, an untrusted CA (see above). An enrollment
  rejection means the secret was already used or expired — mint a fresh one.
- **It connected once, then dropped.** Expected over a flaky WAN link — the Agent reconnects on its own with an
  exponential backoff capped at 30 s and never gives up, re-sending its state on each reconnect. `/hosts` shows
  it as disconnected until it returns, then Connected again.
- **Provisioning a server fails with "no such image".** Set `ZWARDEN_PZ_IMAGE` to the pinned PZ image digest
  and re-run — the socket-proxy denies image pulls by design (ADR 0008), so the image must be present/pinned.

## Security notes

- **No inbound port.** The remote host exposes nothing for ZWarden; all traffic is Agent-initiated outbound
  WSS (criterion 14).
- **Trust is a bearer credential** (ADR 0007), not mutual TLS in v1.0. Revoke or rotate a host's credential from
  the control plane at any time — revocation drops the live connection immediately. mTLS + an Agent CA arrive in
  v1.1.
- **Self-reported host facts are display-only.** The hostname/version/OS shown in `/hosts` are what the Agent
  reports; they are never used to authorize anything. The `agt-` id and the credential are the real identity.
