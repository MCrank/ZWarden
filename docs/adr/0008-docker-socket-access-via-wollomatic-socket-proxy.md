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

## The allowlist — ten entries

`GET|HEAD /_ping` · `GET /version` · `GET /info` · `GET /containers/json` ·
`GET /containers/{id}/json` · `GET /containers/{id}/logs` · `POST /containers/create` ·
`POST /containers/{id}/start` · `POST /containers/{id}/stop` · `POST /containers/{id}/restart`

Denied explicitly, among others: `DELETE /containers/{id}`, `POST /containers/prune`,
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
