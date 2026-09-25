# 8. Docker socket access: `wollomatic/socket-proxy` behind a ten-entry allowlist

The restricted Docker API proxy PRD 27 asks for is **`wollomatic/socket-proxy:1.13.1`** (MIT,
`sha256:3935b709…`), carrying a **ten-entry allowlist**. It is classified as
**bug-containment, not compromise-containment**, and it is **never on the correctness path** —
ZWarden.Agent must be correct with the socket unproxied.

**`tecnativa/docker-socket-proxy` is rejected**, on measured grounds, and the rejection is
recorded here explicitly because PRD 27's diagram implicitly gestures at it and a future reader
will otherwise re-propose it.

**No proxy makes `POST /containers/create` safe.** That is not a limitation of the chosen
component; it is a property of the endpoint.

- Status: accepted
- Decided in: [#17](https://github.com/MCrank/ZWarden/issues/17); full evidence in [`docs/research/docker-socket-proxy.md`](../research/docker-socket-proxy.md). Every capability claim was measured against a live daemon (Docker Desktop 4.61.0, Engine 29.2.1, API 1.53), not read off a README.
- Builds on: [#6](https://github.com/MCrank/ZWarden/issues/6) (no mature proxy can authorize by container label), [#7](https://github.com/MCrank/ZWarden/issues/7) / `docs/trust-boundaries.md` §1 and §4

## Context

PRD 25 requires that Agents manage only containers explicitly assigned to them, and PRD 27 wants
a restricted proxy in front of the socket. [#6](https://github.com/MCrank/ZWarden/issues/6)
established the hard constraint: **Docker ships no first-party label-based authorization**, and
the only projects that do per-container label scoping are young, tiny and effectively
single-maintainer. `docs/trust-boundaries.md` §1 took the consequence seriously — the proxy is
defence in depth, not a boundary, and the Agent's own label-and-assignment check is the actual
control.

What remained was which proxy, and what it may pass through.

## The allowlist — eleven entries (ten original + one added by F16)

`GET|HEAD /_ping` · `GET /version` · `GET /info` · `GET /containers/json` ·
`GET /containers/{id}/json` · `GET /containers/{id}/logs` · `GET /containers/{id}/stats?stream=false` ·
`POST /containers/create` · `POST /containers/{id}/start` · `POST /containers/{id}/stop` ·
`POST /containers/{id}/restart`

> **Amendment (F16, [#37](https://github.com/MCrank/ZWarden/issues/37)):** a single **read-only** eleventh
> entry, `GET /containers/{id}/stats` (used non-streaming, `?stream=false`), was added so the Agent's
> runtime-metrics sampler can read a container's CPU and memory (F16 PR-B). It is a `GET`, sits beside the
> existing `json`/`logs` reads, and carries **no mutation and no exec** — it does not widen the compromise
> surface the way a write verb would, so it does not disturb this ADR's bug-containment-not-compromise-
> containment classification. Disk usage is read from the host bind-mount directly, not through the socket, and
> **no streaming stats** are used (a long-lived stats stream through wollomatic is untested — the same caveat
> that stands for log streaming below). The integration drift test carries the amended allowlist so any
> divergence between what the Agent calls and what the deployment permits still fails the build.

> **Amendment (#184):** the bind-mount source prefix `-allowbindmountfrom` was relocated from `/tmp` to
> **`/srv/zwarden`**. The original `/tmp` was chosen only as a conveniently-writable prefix, but Project Zomboid
> **world data lives under it**, and a host's `/tmp` is subject to `tmpfiles`/reboot cleanup — a data-loss risk.
> `/srv/zwarden` is a persistent host location that the reference Compose bind-mounts into the Agent at the
> **same path** (so the path the Agent seeds is the path the daemon resolves the bind source against) and
> prepares with the shared Agent/PZ ownership the world tree needs. The security property is unchanged: bind
> sources are still constrained to a single fixed prefix; only the prefix moved. The value is deployment
> configuration, not part of the canonical **verb** allowlist — the integration drift test continues to pin the
> verb set (create/start/stop/restart + the read `GET`s), which is the security-critical invariant.
>
> The same change also taught the closed create-template to run SteamCMD under the read-only rootfs (Invariant 8):
> it mounts a small ephemeral **`/tmp` tmpfs** and redirects **`HOME`/`TMPDIR`** onto the existing `/pz/runtime`
> tmpfs, so SteamCMD's temp files (including its breakpad `/tmp/dumps`) and client state (`~/.steam`) have
> writable storage without loosening the read-only root or adding a persistent mount. Nothing there survives a
> recreate. It is enforced by the F13 factory tests alongside the other invariants.

> **Amendment ([ADR 0045](./0045-recreate-removes-a-stopped-container-by-its-canonical-name-only.md), #229):** a
> twelfth entry, `DELETE /containers/srv-<uuid>`, admits removal of a **canonical container by its ServerId name
> only** — for the Recreate Operation (ports now, memory and branch later). A hex id or any other name is still
> a 403, and the Agent never forces a remove or drops volumes. The unscoped `DELETE /containers/{id}` below
> remains denied.

Denied explicitly, among others: `DELETE /containers/{id}` (any path but a canonical `srv-<uuid>` name), `POST /containers/prune`,
`/containers/{id}/kill`, `/containers/{id}/exec` **and** `/exec/{id}/start`,
`PUT|GET|HEAD /containers/{id}/archive`, `/update`, `/rename`, `/attach`, `/export`, `/pause`,
`/top`, `/changes`, and all of `/images/*`, `/volumes/*`, `/networks/*`, `/build`, `/commit`,
`/session`, `/grpc`, `/auth`, `/secrets/*`, `/configs/*`, `/swarm/*`, `/plugins/*`, `/system/*`.

**Two structural traps, both measured:**

1. **`POST /containers/{id}/exec` lives under the `/containers` prefix, not `/exec`.** Any
   section-level allowlist that grants "containers" grants exec-create. Confirmed on Tecnativa
   with `EXEC=0` returning **201 Created**.
2. **`HEAD /_ping` is load-bearing**, because it carries the `Api-Version` header. Blocking it
   does not error — it **silently downgrades** the negotiated API version.

## Why Tecnativa is rejected

Not a maintenance judgement. It **measurably cannot express this allowlist in either
direction**:

- Its `POST` variable really means "allow any non-`GET` method".
- Its section rules are unanchored prefix matches.
- The surprise: its `ALLOW_START` / `ALLOW_STOP` / `ALLOW_RESTARTS` variables are **inert when
  `POST=0`**, because the unconditional deny precedes them in the generated HAProxy config.

So the only configuration that permits `POST /containers/create` **also** permits
`DELETE /containers/{id}` (204), `POST /containers/prune` (200 — really deleted),
`PUT /containers/{id}/archive` (200) and exec-create (201). Shipping it and recording that the
socket is restricted would be false.

`linuxserver/socket-proxy` is strictly better — its `ALLOW_*` genuinely work with `POST=0`,
giving **nine of the ten** entries cleanly, and its bus factor is better — but there is **no
`ALLOW_CREATE`**. It is worth revisiting if container provisioning ever moves off the Agent.

**"No proxy at all" remains defensible** and was considered honestly. What is not defensible is
shipping Tecnativa and calling the socket restricted.

## Why no proxy can make container creation safe

wollomatic's `-allowbindmountfrom` does real work: `Binds:["/:/host"]` and `Mounts` bind sources
are blocked with 403. But **with that filter active**, every one of these returned **201
Created** and reached the daemon unsanitized:

- `Privileged: true`, `NetworkMode: host`, `PidMode: host`, `CapAdd: [SYS_ADMIN]`
- `IpcMode: host`, `UsernsMode: host`, `Devices: [/dev/sda rwm]`

Any one of those is root on the host. **The create request body is therefore an Agent-side
correctness requirement, not a hardening measure.** Twelve invariants (privileged false, no host
namespaces, no devices, pinned image digest, canonical labels, `/pz/` mounts only, …) are
enumerated in the research document and belong in Feature 13's test plan.

A ZWarden-authored filter in front of the daemon was considered and rejected: it would duplicate,
one call later and with less domain context, exactly what the Agent must assert anyway.

## What the proxy actually buys, stated honestly

It converts a class of **Agent bugs** — a label check that matches one container too many —
from "destroys a foreign container" into "HTTP 403". It buys approximately nothing against a
*compromised* Agent, because the one verb it must allow is itself a host escape. That is
`docs/trust-boundaries.md` §1 with a measurement attached, and it is why the classification in
this ADR's first paragraph is written the way it is.

## Two objections that did not survive, and one that got worse

- **Version coupling: dropped.** No proxy in the candidate set parses the Docker API version;
  they pass the prefix through opaquely. The coupling is Agent↔daemon either way. Separately,
  the probe host's daemon maxed at **API 1.53** while [#6](https://github.com/MCrank/ZWarden/issues/6)
  recorded Engine API **v1.56** as current and the CI runner reported **1.48** — which settles
  the real requirement: **the Agent negotiates via `/_ping` and never pins a `/v1.xx` prefix.**
- **Bus factor: worse than recorded.** [#6](https://github.com/MCrank/ZWarden/issues/6) called
  Tecnativa an active multi-contributor org repo; measured, it is an org in maintenance mode at
  **11 human commits in 12 months against 52 open issues**. wollomatic is one human plus
  dependabot. Five of seven candidates are one person. **There is no healthy option**, which is
  itself an argument for the never-on-the-correctness-path posture. (Also corrected: wollomatic
  is MIT per its LICENSE, not the `NOASSERTION` the earlier research reported.)

## Consequences

- **The canonical PZ image must be pre-provisioned.** Denying `/images/*` means container
  creation cannot pull it (PRD 22). The failure is clean — `404 {"message":"No such image: …"}`
  — so this is a reference-deployment requirement plus a Feature 13 diagnostic. It was written
  nowhere before this ADR.
- **Long-lived multiplexed log streaming (PRD 38) through wollomatic was never tested.** It is
  the sharpest unverified item behind this recommendation and the likeliest place for an
  unpleasant surprise. Prove it early in Feature 27; if it does not hold, **the allowlist or the
  proxy choice is what gives, not the feature.**
- **Testcontainers is a different trust context and is named as one.** Six of the thirteen
  endpoints a *minimal* Testcontainers 4.15.0 container needs are ones this allowlist must deny
  — `POST /images/create`, `POST /containers/{id}/exec`, `POST /exec/{id}/start`,
  `GET /exec/{id}/json`, `GET /images/{name}/json`, `DELETE /containers/{id}` — and that is the
  floor, with Ryuk off and no networks or volumes. Ryuk itself bind-mounts the Docker socket and
  can run privileged. CI runners and dev machines are inside the trust boundary of their own
  disposable daemon. **The test harness must never be a reason to widen the production
  allowlist.**
- **One adoptable test claim:** exercise the Agent's Docker abstraction in at least one
  integration test *through* a proxy carrying this allowlist, so drift between what the Agent
  calls and what the deployment permits fails the build.
- **PRD 27's "preferred long-term architecture" framing is kept exactly as written** and not
  upgraded to a guarantee. Its diagram is achievable; its implied component must not be
  Tecnativa.
