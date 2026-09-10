# The restricted Docker socket proxy and its allowlist

Research resolving [#17](https://github.com/MCrank/ZWarden/issues/17). Researched **2026-09-10**.

Unlike [`deployment-security-standards.md`](deployment-security-standards.md), which is facts only,
this document **ends in a recommendation** — the ticket asks for one, and PRD 27 plus the
technology-baseline ADRs need a named component or a named refusal.

## How to read this

Every claim carries a source. Confidence labels:

- **Measured** — verified by executing it during this research against a live Docker daemon. The
  probe method is in §4.1; every "measured" line has a corresponding HTTP status code in §4.
- **High** — primary source: the component's own repository, its rendered config, the Moby OpenAPI
  spec, or a registry API.
- **Inferred** — reasoning over the above, labelled as such.

| Tier | Source |
| --- | --- |
| Primary — spec | `raw.githubusercontent.com/moby/moby/master/api/swagger.yaml` (Engine API **v1.56**), `docs.docker.com/reference/api/engine/` |
| Primary — component | each project's GitHub repo, its `README.md`, its rendered `haproxy.cfg`, its `LICENSE` |
| Primary — registry | `hub.docker.com/v2/repositories/...` for real tags and digests; GitHub REST API for maintenance figures |
| Primary — executed | Docker Desktop 4.61.0, Engine **29.2.1**, API **1.53**, on WSL2, 2026-09-10 |

### What this document does not re-derive

Settled by [#6](https://github.com/MCrank/ZWarden/issues/6) and
[`deployment-security-standards.md`](deployment-security-standards.md) §2.1 and §3, and taken as
given here:

- Docker Engine API **v1.56** is current; API versions before v1.40 are unsupported.
- **No mature socket proxy does label-scoped authorization** of the *target* container.
- The candidate field itself (§3.3 of that document) — this document re-verifies its maintenance
  figures, corrects two of them, and adds what that survey did not cover: **what each candidate
  actually does when you run it**.

Settled by [`../trust-boundaries.md`](../trust-boundaries.md) §1 and §4, and built on rather than
reopened:

- ZWarden.Agent sits inside the host's trust domain; **a compromised Agent means a compromised host**.
- Label-scoped enforcement lives in **ZWarden.Agent's own code**.
- A proxy is **defence in depth**: it narrows *what kind* of call is possible, never *which
  container* it lands on.

---

## 1. The recommendation, up front

**Adopt `wollomatic/socket-proxy` in the reference deployment, and classify it in the ADR as
bug-containment, not compromise-containment.** It is the only surveyed component that can express
Feature 13's allowlist at all (§4.4) — every other candidate either cannot permit container creation
without simultaneously permitting container *deletion*, or cannot permit creation at all.

**Reject `tecnativa/docker-socket-proxy`**, the component PRD 27 implicitly gestures at. It is not a
close call and it is not a maintenance judgement: measured against a live daemon, its rule model
**cannot express Feature 13's allowlist in either direction** (§4.2). The single configuration that
permits `POST /containers/create` also permits `DELETE /containers/{id}`, `POST /containers/prune`,
`PUT /containers/{id}/archive` and `POST /containers/{id}/exec`.

**But the proxy must not be on the correctness path, and ZWarden.Agent must be correct with the
socket unproxied.** The reason is measured and decisive (§5): Feature 13 requires
`POST /containers/create`, no candidate can constrain that request's body beyond bind-mount sources,
and a create body carrying `"Privileged": true` is a full host escape. A proxy that allows create
does not prevent host compromise — it only makes it less convenient. This is not a new concession;
it is `trust-boundaries.md` §1 restated with a measurement attached.

The honest summary of what the proxy buys: it converts a class of **Agent bugs** — the label check
that matches one container too many, the id that gets confused, the retry that fires the wrong verb —
from "destroys a foreign container" into "HTTP 403". `trust-boundaries.md` §4 names exactly that risk
("the only thing standing between an Agent bug and any container on the host"). It buys close to
nothing against an attacker who already runs code as the Agent.

If that trade is judged not worth a fourth component in the reference deployment, **"no proxy" is a
defensible v1.0 answer** and §6 makes that case without hedging. What is *not* defensible is shipping
Tecnativa and recording in an ADR that the socket is restricted.

---

## 2. The candidates

Maintenance figures re-read from the GitHub REST API on **2026-09-10**; "commits/12mo" counts
`?since=2025-09-10`, and separates humans from bots.

| Project | Image reference | Latest | Licence | Stars | Open issues | Commits/12mo (humans) | Bus factor |
| --- | --- | --- | --- | --- | --- | --- | --- |
| [Tecnativa/docker-socket-proxy](https://github.com/Tecnativa/docker-socket-proxy) | `tecnativa/docker-socket-proxy:v0.5.0` | v0.5.0, **2026-07-27** | Apache-2.0 | 2,754 | **52** | **11**, 6 people | Org, but near-dormant |
| [linuxserver/docker-socket-proxy](https://github.com/linuxserver/docker-socket-proxy) | `lscr.io/linuxserver/socket-proxy:3.4.4` | `3.4.4-r0-ls97`, **2026-09-07** | GPL-3.0 | 312 | 2 | 32 human + 43 CI | Org-backed; **1 dominant human** (thespad) |
| [wollomatic/socket-proxy](https://github.com/wollomatic/socket-proxy) | `wollomatic/socket-proxy:1.13.1` | 1.13.1, **2026-08-15** | **MIT** (see below) | 427 | 18 | **71, one person** + 29 dependabot | **1** |
| [FoxxMD/docker-proxy-filter](https://github.com/FoxxMD/docker-proxy-filter) | `foxxmd/docker-proxy-filter:0.1.2` | 0.1.2, **2026-06-11** | MIT | 63 | 5 | 2 contributors | 1; **3 months stale** |
| [mikesir87/docker-socket-proxy](https://github.com/mikesir87/docker-socket-proxy) | `mikesir87/docker-socket-proxy:v1.3.1` | v1.3.1, **2026-02-06** | Apache-2.0 | **9** | 3 | last push 2026-03-02 | 1; **~6 months stale** |
| [CodesWhat/sockguard](https://github.com/CodesWhat/sockguard) | `sockguard` per its README | v2.2.1, **2026-09-08** | Apache-2.0 | **8** | 1 | **96, one person** + renovate | 1; created **2026-03-22** |
| [knrdl/docker-socket-protector](https://github.com/knrdl/docker-socket-protector) | `knrdl/docker-socket-protector` | v2.4.3, **2025-11-29** | MIT | 37 | 1 | — | 1; release **9 months** old |

Exact digests read from the Docker Hub registry API on 2026-09-10:

- `tecnativa/docker-socket-proxy:v0.5.0` → `sha256:1f5038b54f06c3e18422902cf00ba21803d1c97805aae032e5e6673d532d3459`
  (`latest` resolves to the same digest). **Measured** — this is the digest the probe ran against.
- `lscr.io/linuxserver/socket-proxy:3.4.4` → `sha256:ba211325155c463a1a6e6a038c928f21447a8e60ce02ecf41302047974097e84`
  (`latest`, `version-3.4.4-r0` and `3.4.4-r0-ls97` all resolve to the same digest). **Measured.**
- `wollomatic/socket-proxy:1.13.1` → `sha256:3935b709275e4ec35d6ed5a5c4a1f0d01ed31eec5e7234efc3357ecd47689002`
  (the floating `1` tag resolves to the same digest today). **Measured.** Multi-arch images are
  cosign-signed from v1.6 onward; `.sig` tags are present in the registry listing.

### 2.1 Two corrections to `deployment-security-standards.md` §3.4

1. **wollomatic/socket-proxy is MIT-licensed.** That document reported GitHub's
   `license.spdx_id: NOASSERTION` and advised reading the `LICENSE` file directly before relying on
   the terms. Done: <https://raw.githubusercontent.com/wollomatic/socket-proxy/main/LICENSE> begins
   `MIT License / Copyright (c) 2023 Wolfgang Ellsässer (wollomatic)`. GitHub's classifier is wrong;
   the licence is MIT. **High.**
2. **Tecnativa is far less active than its contributor count suggests.** That document described it
   as "an active multi-contributor org repo, not a single maintainer" (27 contributors, 2,754 stars).
   Over the trailing 12 months it received **11 commits from 6 people**, the largest contributing
   3, against **52 open issues and 16 open PRs**. It is an org repo in maintenance mode, not an
   actively developed one. **High.**

### 2.2 Bus factor, treated as a first-class finding

The ticket asks for this explicitly, and the picture is worse than the star counts imply. Of the
seven candidates:

- **Five are effectively one person** (wollomatic, sockguard, FoxxMD, mikesir87, knrdl).
- **One is an org in maintenance mode** (Tecnativa: 11 human commits in a year, 52 open issues).
- **One is org-backed with real release machinery but one dominant human** (linuxserver: thespad
  authored 19 of the 32 human commits; the other 43 are `LinuxServer-CI` package rebuilds).

There is no option here with a healthy bus factor. That is a genuine argument for **not** taking a
dependency at all (§6), and it means whichever component is chosen, the ADR should record that
ZWarden must be able to remove it without a code change. The recommended posture in §1 — proxy
never on the correctness path — makes that removal a one-line deployment change by construction.

Two candidates are excluded from further consideration on staleness alone:
**mikesir87/docker-socket-proxy** (9 stars, no push since 2026-03-02) and
**knrdl/docker-socket-protector** (last release 2025-11-29, and it does not do label scoping anyway).
**CodesWhat/sockguard** is excluded on track record: 8 stars, created 2026-03-22, releases every one
to two days. Its capability claims are the broadest in the field and partly source-corroborated, but
adopting a six-month-old, single-author, 8-star component as a *security* boundary in a reference
deployment inverts the point of having one.

### 2.3 Docker API version support against the v1.56 target

This is the question `deployment-security-standards.md` did not answer, and the answer is that it is
largely a non-question — but for a reason worth writing down, because it removes a version-coupling
argument that would otherwise count against adopting a proxy.

**None of the HAProxy- or regex-based proxies parse or validate the Docker API version.** They match
the version prefix as an opaque path segment and pass it through untouched.

- **Tecnativa** matches `^(/v[\d\.]+)?` on every rule, read from its rendered config at
  <https://raw.githubusercontent.com/Tecnativa/docker-socket-proxy/master/haproxy.cfg>. Any
  `v<digits and dots>` matches, so v1.44 through v1.56 and beyond all pass. There is **no API version
  ceiling in the proxy**. **High.**
- **wollomatic** takes user-supplied regexes, so the ceiling is whatever the operator writes. Its
  README example is `'-allowGET=/v1\..{1,2}/(version|containers/.*|events.*)'` — `.{1,2}` matches
  `56` but **would not match a future `/v1.100/`**. That is a documentation footgun, not a product
  limit; ZWarden's config should use `(/v1\.[0-9]+)?` instead. **High.**
- **linuxserver** uses the same HAProxy path-prefix model as Tecnativa, extended with `(/libpod)?`
  for Podman. **High.**

**Measured:** with the proxy in front, `GET /v1.56/version` returned **HTTP 400** and
`GET /v1.56/containers/json` returned `{"message":"client version 1.56 is too new. Maximum supported
API version is 1.53"}`. The 400 came **from the daemon, not the proxy** — `GET /v1.53/version` and
unversioned `GET /version` both returned 200 through the same proxy.

Two consequences:

1. **Version coupling is between ZWarden.Agent and the Docker daemon, not between ZWarden and the
   proxy.** The "another version coupling in the reference deployment" objection in the ticket does
   not survive contact with the evidence. It should be dropped from the case against.
2. **v1.56 is the spec's current version, not the version a current host serves.** The probe host
   runs Docker Desktop 4.61.0 / Engine 29.2.1, whose maximum API version is **1.53**. Pinning the
   Agent to a literal `/v1.56` prefix would break against a Docker installation that is six months
   old. The Agent should negotiate — `GET`/`HEAD /_ping` returns the `Api-Version` header — rather
   than hard-code, which is what `Docker.DotNet.Enhanced` does by default. See §3.2 for the allowlist
   consequence.

---

## 3. The allowlist

Endpoint paths and methods are read from the Moby OpenAPI spec, `version: "1.56"`
(<https://raw.githubusercontent.com/moby/moby/master/api/swagger.yaml>). Every path below may carry
an optional `/v1.NN` prefix. **High** for every method/path pairing in this section.

Feature 13's deliverables, from
[`../scope-and-sequencing.md`](../scope-and-sequencing.md) §6: *"Docker API abstraction, container
discovery, canonical-label validation, inspect/start/stop/restart, allowed-container enforcement,
Docker health diagnostics, two-port-stride allocation"*, plus creation of canonical PZ containers per
`trust-boundaries.md` §4.

### 3.1 The minimal allow set

| Method | Path | Why Feature 13 needs it |
| --- | --- | --- |
| `GET` | `/_ping` | liveness; the `Api-Version` response header |
| `HEAD` | `/_ping` | **API version negotiation** — see §3.2 |
| `GET` | `/version` | daemon version, Docker health diagnostics |
| `GET` | `/containers/json` | container discovery, inventory (F13, F14) |
| `GET` | `/containers/{id}/json` | inspect: state, health, **canonical labels** (PRD 25), port bindings |
| `GET` | `/containers/{id}/logs` | log streaming (PRD 38) |
| `POST` | `/containers/create` | creation of canonical PZ containers — **see §5** |
| `POST` | `/containers/{id}/start` | lifecycle (F15) |
| `POST` | `/containers/{id}/stop` | lifecycle (F15) |
| `POST` | `/containers/{id}/restart` | lifecycle (F15) |

Ten entries. That is the whole of it.

### 3.2 Three judgement calls inside the allow set

**`HEAD /_ping` must be allowed, and this is not optional.** The Engine API exposes the daemon's
maximum supported API version through the `Api-Version` header on `/_ping`. wollomatic's README
carries a prominent warning that when Traefik changed to discovering the API version via `HEAD
/_ping`, proxies that did not allow `HEAD` caused Traefik to *"fall back to API version 1.51, which
would break the Docker provider on older Docker versions"*
(<https://github.com/wollomatic/socket-proxy>, referencing traefik/traefik#12256). A proxy that
blocks `HEAD /_ping` does not fail loudly; it **silently downgrades the negotiated API version**.
Given §2.3's finding that the Agent must negotiate rather than pin, this is load-bearing.

*Aside, since it corrects a plausible misreading of Tecnativa's config:* the config's gate is
`http-request deny unless METH_GET || { env(POST) -m bool }`, which looks like it denies `HEAD` when
`POST=0`. It does not. HAProxy's predefined ACL `METH_GET` is defined as `method GET HEAD` — *"match
HTTP GET or HEAD method"* — per line 28730 of
<https://raw.githubusercontent.com/haproxy/haproxy/master/doc/configuration.txt>. **Measured:**
`HEAD /_ping` returned 200 through Tecnativa with `POST=0`. Its README's claim that "only `GET` and
`HEAD` operations are allowed" is accurate.

**`GET /info` — allow, narrowly.** Feature 13 lists "Docker health diagnostics". `/info` is the
natural source, and `POST /containers/create` is measurably useless for reconnaissance by comparison.
The cost: `/info` discloses host-wide container and image counts, kernel and OS details, storage
driver, cgroup version, registry configuration and any daemon-level insecure registries. That is
reconnaissance value handed to a compromised Agent, but a compromised Agent is on the host anyway
(`trust-boundaries.md` §1). Allow it; note it in the ADR rather than pretending it is free.

**`GET /containers/{id}/stats` and `GET /events` — defer until a feature needs them.** Neither is
required by Feature 13's stated deliverables. `/events` in particular is **host-wide and cannot be
filtered to ZWarden's containers at the proxy** — the server-side `filters` query parameter is
supplied by the *caller*, so it is a convenience, not a control, and §2.3's finding that query
strings are not matched (measured, §4.4) means the proxy cannot enforce it either. If Feature 16
later wants event-driven state, add `GET /events` then, with that limitation recorded.

### 3.3 What must be explicitly denied

The critical structural point, and the reason a section-level allowlist is not good enough:
**`POST /containers/{id}/exec` lives under the `/containers` prefix, not `/exec`.** Any allowlist
that grants "containers" as a section grants the creation of exec instances. **Measured** on
Tecnativa with `EXEC=0`: `POST /containers/{id}/exec` against a running container returned **201
Created** (§4.2).

| Method | Path | Why it must be denied |
| --- | --- | --- |
| `DELETE` | `/containers/{id}` | container removal; the Agent never deletes |
| `POST` | `/containers/prune` | mass removal of every stopped container on the host |
| `POST` | `/containers/{id}/kill` | bypasses the PRD 23 / F15 `save`-then-`quit` stdin FIFO stop path — a data-loss verb, not a lifecycle verb |
| `POST` | `/containers/{id}/exec` | creates an exec instance |
| `POST` | `/exec/{id}/start` | **arbitrary command execution**; directly contradicts PRD 19 |
| `POST` | `/exec/{id}/resize`, `GET` `/exec/{id}/json` | the rest of the exec surface |
| `PUT` | `/containers/{id}/archive` | **arbitrary file write into any container** |
| `GET`, `HEAD` | `/containers/{id}/archive` | arbitrary file read out of any container |
| `POST` | `/containers/{id}/update` | raises resource limits set by PRD 24 |
| `POST` | `/containers/{id}/rename` | breaks the name/label invariants F13 relies on |
| `POST` | `/containers/{id}/attach` | hijacked bidirectional stream, including stdin |
| `GET` | `/containers/{id}/attach/ws` | the same over WebSocket |
| `GET` | `/containers/{id}/export` | exfiltrates a whole container filesystem |
| `POST` | `/containers/{id}/pause`, `/unpause`, `/resize` | not in Feature 13 |
| `GET` | `/containers/{id}/top`, `/changes` | not in Feature 13 |
| `POST` | `/containers/{id}/wait` | not needed; the Agent polls inspect |
| — | **all of `/images/*`** | esp. `POST /images/create` (pull anything), `POST /images/load`, `DELETE /images/{name}`, `GET /images/{name}/get` |
| — | **all of `/volumes/*`** | PRD 24's "explicit writable mounts" is an Agent-side assertion, not a volume-admin capability |
| — | **all of `/networks/*`** | PRD 26's network isolation is a deployment property; the Agent must not be able to rewrite it |
| — | `/build`, `/commit`, `/session`, `/grpc` | image build; `/session` and `/grpc` are BuildKit's transports and are **not covered by Docker's AuthZ plugin hook** (see §6.3) |
| — | `/auth`, `/secrets/*`, `/configs/*` | registry credentials and secret material |
| — | `/swarm/*`, `/nodes/*`, `/services/*`, `/tasks/*` | orchestration; ZWarden is single-host |
| — | `/plugins/*` | daemon extension — the daemon's own configuration |
| — | `/system/df`, `/system/prune` | host-wide disclosure and host-wide destruction |

### 3.4 The image-pull tension, and how to resolve it

Denying `/images/*` collides with "creation of canonical PZ containers": `POST /containers/create`
fails if the ZWarden.PZServer image (PRD 22) is not present locally, and pulling it is
`POST /images/create` — which the allowlist denies.

The resolution is to **pre-provision the image in the reference deployment** (a `docker compose pull`
or an explicit operator step) and keep `/images/*` denied. This is compatible with PRD 24's "pinned
production image references" and PRD 55's supply-chain posture, and it is strictly better than
granting the Agent the ability to pull arbitrary images — which, combined with an allowed
`POST /containers/create`, would let a compromised Agent run **any image from any registry** on the
host.

**Measured:** the failure mode when the image is absent is clean and actionable —
`POST /containers/create {"Image":"alpine"}` returned **404** with
`{"message":"No such image: alpine:latest"}`. Feature 13 can surface that as a specific,
operator-fixable diagnostic rather than a generic failure. That measurement is what makes "deny
images, pre-provision" a comfortable choice rather than a risky one.

Optional, if the diagnostic is judged insufficient: allow **`GET /images/{name}/json`** (image
inspect) read-only, so the Agent can preflight "is the pinned digest present" before attempting a
create. It is a read of one named image and discloses nothing the Agent could not learn from the
404. Recorded here as a decision point, not a recommendation.

### 3.5 The concrete wollomatic configuration

```yaml
# reference deployment, docker-socket-proxy service
command:
  - '-loglevel=INFO'
  - '-allowfrom=zwarden-agent'          # per-client, resolved by name on the control network
  - '-allowGET=(/v1\.[0-9]+)?/(_ping|version|info|containers/json|containers/[a-zA-Z0-9_.-]+/(json|logs))'
  - '-allowHEAD=(/v1\.[0-9]+)?/_ping'
  - '-allowPOST=(/v1\.[0-9]+)?/(containers/create|containers/[a-zA-Z0-9_.-]+/(start|stop|restart))'
  - '-allowbindmountfrom=/srv/zwarden'  # see §5 — narrows, does not secure
  - '-watchdoginterval=30'
  - '-stoponwatchdog'
```

Notes, all **measured** in §4.4 unless marked:

- Patterns are **auto-anchored** with `^`/`$` by wollomatic; do not add your own.
- **No `-allowPUT`, `-allowDELETE`, `-allowPATCH` is set at all**, so those methods return
  **405 Method Not Allowed** rather than 403 — a useful distinction in logs: 405 means "no rule for
  this method", 403 means "method allowed, path did not match".
- `-allowbindmountfrom` requires an absolute path and is the *only* body-inspecting control
  available. §5 explains precisely how far it goes.
- The version group is `(/v1\.[0-9]+)?`, **not** the README's `/v1\..{1,2}/`, per §2.3.
- Query strings are excluded from matching, so `?stdout=1` on logs and `?name=` on create both work
  without appearing in the pattern — and correspondingly **cannot be constrained**.
- `-allowfrom` defaults to `127.0.0.1/32`; on a Docker network it must name the client. This is a
  *caller*-identity control and, per `deployment-security-standards.md` §3.4, wollomatic's Docker
  label feature is also caller-identity — **neither scopes the target container**.
- **Inferred:** `-watchdoginterval`/`-stoponwatchdog` make proxy failure loud rather than silent;
  not measured here.

---

## 4. What the candidates actually do — measured

`deployment-security-standards.md` §3 built its capability matrix from READMEs and source-tree
evidence, and flagged several claims as unverified. This section runs them.

### 4.1 Probe method

Docker Desktop 4.61.0, Engine 29.2.1, API 1.53, WSL2 backend, 2026-09-10. Each proxy ran as a
container on a private bridge network with `/var/run/docker.sock` bind-mounted; requests were issued
from a `curlimages/curl` container on the same network, so nothing was published to the host. Images
were pinned by tag and the resolved digests recorded in §2. Every proxy, network and test container
created was removed afterwards.

One side effect is worth recording plainly: the `POST /containers/prune` probe in §4.2 **really
pruned**, removing the stopped containers present on the probe host at that moment. That is the
finding, not an accident — but it is why that probe is not one to repeat casually.

### 4.2 Tecnativa v0.5.0 — cannot express the allowlist

Its whole authorization model is 30 lines of HAProxy ACLs, read from
<https://raw.githubusercontent.com/Tecnativa/docker-socket-proxy/master/haproxy.cfg>. Two properties
of that file decide the outcome:

1. The gate is `http-request deny unless METH_GET || { env(POST) -m bool }`. The variable named
   `POST` is really **"allow all non-GET methods"** — there is no per-method or per-section
   distinction, and no `DELETE` variable exists.
2. Section rules are **unanchored prefix matches**: `CONTAINERS=1` compiles to
   `path,url_dec -m reg -i ^(/v[\d\.]+)?/containers`, which matches every path *beginning* with
   `/containers`.

**Configuration A — `CONTAINERS=1 POST=1 PING=1 VERSION=1`** (the only configuration that permits
container creation):

| Request | Result |
| --- | --- |
| `POST /containers/create` (privileged, `Binds:["/:/host"]`, `NetworkMode:host`, `CapAdd:[SYS_ADMIN]`, `PidMode:host`) | **201 Created** |
| `DELETE /containers/{id}?force=true` | **204** — `POST=1` permits `DELETE` |
| `POST /containers/prune` | **200**, `{"ContainersDeleted":[...]}` — really deleted |
| `PUT /containers/{id}/archive?path=/tmp` | **200** — arbitrary file write into a container |
| `POST /containers/{id}/rename?name=...` | **204** |
| `POST /containers/{id}/exec` (running container, **`EXEC=0`**) | **201 Created** |
| `POST /containers/{id}/update` | reached the daemon (409, container not running) |
| `GET /images/json`, `/volumes`, `/networks`, `/info`, `/exec/{id}/json` | 403 |

**Configuration B — `CONTAINERS=1 POST=0` plus `ALLOW_START=1 ALLOW_STOP=1 ALLOW_RESTARTS=1`**:

| Request | Result |
| --- | --- |
| `GET /_ping`, `HEAD /_ping`, `/containers/json`, `/containers/{id}/json`, `/containers/{id}/logs` | 200 |
| `POST /containers/{id}/start` | **403** |
| `POST /containers/{id}/stop` | **403** |
| `POST /containers/{id}/restart` | **403** |
| `POST /containers/{id}/kill` | 403 |
| `POST /containers/create`, `DELETE /containers/{id}`, `POST /containers/prune` | 403 |

**The `ALLOW_*` variables are inert when `POST=0`.** This is a genuine surprise and it is visible in
the config once you look: the unconditional `http-request deny unless METH_GET || env(POST)` line
precedes every `http-request allow ... env(ALLOW_START)` rule, so a `POST` is denied before it can
reach them. **Configuration C** confirms the semantics — with `CONTAINERS=0 POST=1
ALLOW_RESTARTS=1`, `POST /containers/{id}/restart` returned **204** while `GET /containers/json`,
`GET /containers/{id}/json`, `POST /containers/create` and `DELETE /containers/{id}` all returned
403. So `ALLOW_*` only does anything when `POST=1` **and** `CONTAINERS=0` — a configuration that
grants lifecycle verbs while denying inspect and list, which is useless to Feature 13.

**Conclusion.** Tecnativa offers exactly two reachable shapes: read-only with no lifecycle at all, or
full `/containers` write access including delete, prune, archive-write and exec-create. Feature 13
needs inspect **and** logs **and** lifecycle **and** create **and not** delete. Tecnativa cannot
express that. **Measured, and decisive.**

### 4.3 linuxserver 3.4.4 — expresses everything except create

Same HAProxy lineage, but rebuilt: its README states, in a table heading, **"These options work even
when `POST=0`"** for `ALLOW_PAUSE`, `ALLOW_RESTARTS`, `ALLOW_START`, `ALLOW_STOP`, `ALLOW_UNPAUSE`
(<https://raw.githubusercontent.com/linuxserver/docker-socket-proxy/main/README.md>). That claim is
**true**, and it is the single behavioural difference that matters between the two forks.

With `CONTAINERS=1 POST=0 ALLOW_START=1 ALLOW_STOP=1 ALLOW_RESTARTS=1 ALLOW_LOGS=1`:

| Request | Result |
| --- | --- |
| `GET /_ping`, `/containers/json`, `/containers/{id}/json`, `/containers/{id}/logs` | **200** |
| `POST /containers/{id}/start`, `/stop`, `/restart`, `/kill` | **204** |
| `POST /containers/create` | **403** |
| `DELETE /containers/{id}` | **403** |
| `POST /containers/prune` | **403** |
| `POST /containers/{id}/exec` | **403** |
| `PUT /containers/{id}/archive` | **403** |
| `POST /containers/{id}/update` | **403** |
| `POST /containers/{id}/rename` | **403** |

This is a clean, tight, org-maintained allowlist — **nine of Feature 13's ten entries**, with exec,
archive-write, delete, prune, update and rename all correctly denied, and it is strictly better than
Tecnativa on every axis measured. Its single defect is fatal for Feature 13: **there is no
`ALLOW_CREATE`**, and the full `ALLOW_*` table confirms it. The only way to reach
`POST /containers/create` is `POST=1`, which restores Tecnativa Configuration A's entire blast
radius.

Two notes for anyone reading the variable names: `ALLOW_CHANGES` is
`/containers/{id}/changes` — the filesystem-diff endpoint — **not** a general "allow mutations"
switch; and `ALLOW_RESTARTS` covers `stop`, `restart` **and `kill`** together, so it cannot be used
to permit restart while denying the data-loss `kill` path of §3.3.

**If ZWarden ever moves container provisioning off the Agent** — an operator-run or Compose-driven
creation step, with the Agent only operating containers it did not create — **linuxserver becomes the
better choice**: org-backed, higher release cadence, and a materially better bus factor than
wollomatic. Worth recording as the condition under which this decision should be revisited.

### 4.4 wollomatic 1.13.1 — expresses the allowlist exactly

Configured with the §3.5 patterns:

| Request | Result |
| --- | --- |
| `GET /_ping`, `GET /v1.53/containers/json`, `GET /containers/{id}/json`, `GET /containers/{id}/logs?stdout=1` | **200** |
| `POST /containers/{id}/restart`, `/start` | **204** / 304 |
| `POST /containers/create?name=...` | **201** |
| `DELETE /containers/{id}` | **405 Method Not Allowed** |
| `PUT /containers/{id}/archive` | **405 Method Not Allowed** |
| `POST /containers/prune` | **403** |
| `POST /containers/{id}/exec` | **403** |
| `POST /containers/{id}/kill` | **403** |
| `GET /images/json`, `/volumes`, `/networks`, `/info` | **403** |

Every Feature 13 operation permitted; every §3.3 denial enforced. **It is the only candidate that
does this.**

**Path-traversal and encoding resistance — measured.** An anchored regex allowlist invites the
question of whether a normalizing daemon downstream can be reached by a path the regex did not
recognise. With the allowlist above and `curl --path-as-is`, all of the following returned **403**:

```
/containers/json/../../images/json          /containers/%2e%2e/images/json
/containers/json/../../v1.53/images/json    /images/json%00
//images/json                               /IMAGES/json
/v1.53/containers/json/../images/json       /./images/json
/v1.53//images/json
```

No bypass found. wollomatic is default-deny — anything not matching an explicit rule is refused — so
the failure mode of an unanticipated path is a 403, not a passthrough. **This is the strongest
positive finding about the component**, and it is the property that makes a regex allowlist
trustworthy enough to bother with.

---

## 5. Container creation, the awkward case

An allowlist cannot make `POST /containers/create` safe, and the measurements say exactly how
unsafe it remains.

### 5.1 What a proxy can constrain

Precisely one thing, in precisely one component: **wollomatic's `-allowbindmountfrom`**. It is the
only body-inspecting control in the maintained field, and it works. With
`-allowbindmountfrom=/srv/zwarden`:

| Create body | Result |
| --- | --- |
| `HostConfig.Binds: ["/:/host"]` | **403 Forbidden** |
| `HostConfig.Mounts: [{Type:"bind", Source:"/etc", Target:"/hostetc"}]` | **403 Forbidden** |
| `HostConfig.Binds: ["/srv/zwarden/x:/data"]` | **201 Created** |

Both the legacy `Binds` syntax and the modern `Mounts` array are covered, matching its README, which
also states it covers local volume-driver `bind`/`rbind` options and rejects `VolumesFrom`.

### 5.2 What no proxy can constrain

Everything else in `HostConfig` — and "everything else" includes several independent, complete host
escapes. Through wollomatic, **with `-allowbindmountfrom` active**:

| Create body | Result |
| --- | --- |
| `Privileged: true`, `NetworkMode: "host"`, `PidMode: "host"`, `CapAdd: ["SYS_ADMIN"]` | **201 Created** |
| `IpcMode: "host"`, `UsernsMode: "host"`, `Devices: [{PathOnHost:"/dev/sda", ...,"rwm"}]` | **201 Created** |

Inspected at the daemon afterwards, the created containers really carried
`Privileged=true Net=host Pid=host CapAdd=[SYS_ADMIN]` and
`Ipc=host Userns=host Devices=[{/dev/sda /dev/sda rwm}]`. Nothing was silently sanitized.

`Privileged: true` alone disables seccomp and AppArmor confinement and grants access to host devices;
`Devices` passes a raw block device into the container; `PidMode: host` exposes every host process.
**Any one of these is root on the host.** The bind-mount filter closes the most obvious door and
leaves several others standing open — which is exactly what its README says of itself, and the phrase
deserves quoting because it is the honest summary of the entire category:

> it "is a request filter with the limitations documented above, **not a sandbox for untrusted Docker
> API clients**."

### 5.3 Therefore: what ZWarden.Agent must assert itself

Because the proxy cannot, these are Agent-side invariants on the `POST /containers/create` body, and
they are **correctness requirements, not hardening**. The Agent must construct the create request
from a closed template and never from caller-influenced input, asserting before the call:

1. `Privileged` is `false`. Never settable, never configurable, no override.
2. `CapAdd` is empty; `CapDrop` reflects PRD 24's "reduced capabilities".
3. `SecurityOpt` contains `no-new-privileges:true` (PRD 24).
4. `NetworkMode` is a named ZWarden network from PRD 26 — never `host`, never `container:{id}`,
   never `none` where it would bypass isolation. The `data` network is never among them (PRD 24, and
   `trust-boundaries.md` §7).
5. `PidMode`, `IpcMode`, `UsernsMode`, `CgroupnsMode`, `UTSMode` are unset — never `host`.
6. `Devices`, `DeviceCgroupRules`, `DeviceRequests` are empty.
7. `Binds` / `Mounts` resolve under the canonical `/pz/` layout (PRD 23) only, with explicit
   read-only flags where PRD 24's "explicit writable mounts" applies; no symlink escape.
8. `ReadonlyRootfs` is set where practical (PRD 24).
9. The image is the **pinned digest** of the canonical ZWarden.PZServer image (PRD 22, PRD 24) —
   never a caller-supplied reference, never a floating tag.
10. `Labels` carry the full PRD 25 canonical set, including `io.zwarden.agent-id` matching this
    Agent's own identity.
11. `User` is non-root (PRD 24); resource limits are set (PRD 24).
12. Port bindings follow F13's two-port-stride allocation and bind only intended interfaces.

This list belongs in Feature 13's tests. `trust-boundaries.md` §4's "Consequence to accept" says the
label-and-assignment check "deserves the same test rigour as an authorization check"; **this list
deserves it too, and for a sharper reason** — the label check protects other containers, while item 1
protects the host. A PRD 15 architecture test asserting that no code path can set `Privileged` to
`true` is cheap and worth having.

### 5.4 Can any candidate filter on body content at all?

Answered against the field:

- **Tecnativa, linuxserver:** no. HAProxy ACLs here match method and path only. Structurally
  impossible in their design, not merely unimplemented. **High.**
- **wollomatic:** bind-mount sources only, as measured above. **Measured.**
- **mikesir87:** yes in principle — mutators can remap mount paths and inject labels, gates can match
  `label:key=value`. Excluded on maturity (§2.2): 9 stars, no push since 2026-03-02. Its
  admission-controller model is the right *shape*, and worth watching.
- **sockguard:** claims the broadest body filtering in the field — denying privileged and host-bound
  workloads, non-allowlisted mounts and devices. Excluded on track record (§2.2). **If a
  body-filtering proxy is ever wanted, this is the design to re-evaluate**, once it has more than 8
  stars and one author.
- **Docker's native AuthZ plugin hook:** receives the full `RequestBody` and could enforce all of
  §5.3. See §6.3 for why that is not the answer either.

**A ZWarden-authored filter is not recommended**, and §5.3 is why. Everything such a filter would
enforce, the Agent must already enforce one function call earlier, where it has the domain context —
the assignment record, the Server entity, the pinned digest — that a proxy parsing JSON does not.
Writing it twice would double the code that must be right and add a second parser over a
security-critical body, for no assertion the Agent cannot make itself. Docker's own AuthZ
documentation warns that a plugin *"must apply the same decoding semantics as the daemon"* to enforce
correctly; that is a real correctness burden and there is no reason to take it on.

---

## 6. Does a proxy earn its place? The honest case against

### 6.1 The case against, stated without hedging

1. **It cannot enforce the check that matters.** Not label scoping (settled by #6), and — the new
   finding — **not the create body either**. Feature 13 requires `POST /containers/create`; a create
   body with `Privileged: true` is root on the host; no candidate blocks it (§5.2, measured). Against
   a compromised Agent the proxy's blast-radius reduction is close to **zero**, because the one verb
   it must allow is itself a host escape.
2. **Another component in the reference deployment.** PRD 58 and PRD 2.4 ("simple deployment") both
   push against a fourth moving part whose failure mode — the Agent losing the Docker API — looks
   like a host-wide outage.
3. **Another failure mode.** A misconfigured regex silently denies a lifecycle verb; the symptom
   surfaces as an unexplained operation failure far from its cause. `HEAD /_ping` (§3.2) is the
   sharpest example: get it wrong and nothing errors — the API version silently downgrades.
4. **Bus factor.** The recommended component is one person plus dependabot (§2.2). There is no option
   in the field without this problem.
5. **It duplicates a check that must exist anyway.** Every §5.3 assertion is Agent-side regardless.
   The proxy's allowlist is a second, weaker statement of a subset of the same policy, and two
   statements of one policy drift.

The version-coupling objection from the ticket **does not survive measurement** and should be dropped
(§2.3): the proxies do not parse the API version, and the coupling that exists is between the Agent
and the daemon either way.

### 6.2 The case for, and why it is narrower than it looks

The proxy stops **Agent bugs**, not Agent compromise, and that distinction is the whole argument.

`trust-boundaries.md` §4 names the risk precisely: *"the label-and-assignment check is the only thing
standing between an Agent bug and any container on the host."* Today that is literally true — a
label-matching bug that returns one container too many, and the Agent stops, deletes or writes files
into something it does not own. With the §3.5 allowlist, the destructive verbs that bug could reach
are **gone**: `DELETE /containers/{id}` is 405, `prune` is 403, `PUT .../archive` is 405, `exec` is
403, `kill` is 403 (measured, §4.4). The worst a label bug can then do to a foreign container is
inspect, read logs, or stop/start/restart it — recoverable, visible, and not data-destroying.

That is a real reduction in the probability-weighted damage of the most likely failure, which is a
bug in new code, not a targeted attacker. It costs one 10 MB MIT-licensed Go binary.

It is also, honestly, most of what defence in depth ever buys, and `trust-boundaries.md` §1 already
declined to pretend otherwise. The document should not need amending.

### 6.3 Two options considered and rejected

**Docker's native AuthZ plugin.** It is the only mechanism that could enforce §5.3 *and* label
scoping, because it receives `RequestMethod`, `RequestURI` and the full `RequestBody`
(<https://docs.docker.com/engine/extend/plugins_authorization/>). Rejected because: the only
general-purpose implementation, `twistlock/authz`, has been dead since 2020 (no releases, no activity
in 6+ years), so this means ZWarden writing and shipping a daemon plugin; it requires **modifying the
Docker daemon's startup flags** (`--authorization-plugin=`), which is a far heavier imposition on a
self-hosted operator than adding a container, and directly contradicts PRD 2.4; and Docker's own docs
list disqualifying limitations — the response-inspection buffer *"has a fixed capacity of 64 KiB"*
(so log and event streams are not inspectable), plugins are called *"only for the initial HTTP
requests"* on connection-hijacking calls like `exec`, and *"authorization plugins enforce requests to
the Docker daemon's HTTP API only"*, leaving gRPC/BuildKit outside. A control with those holes,
written by us, maintained by us, requiring a daemon reconfiguration, is worse than no control.

**A ZWarden-authored filter.** Rejected in §5.4.

---

## 7. Testcontainers: a different trust context, and it must be named as one

[#8](https://github.com/MCrank/ZWarden/issues/8) left "Testcontainers/Docker-socket implications for
PRD 27" unverified. It lands here, and the answer is unambiguous once measured.

### 7.1 What Testcontainers actually calls — measured

A throwaway .NET 10 console probe referencing `Testcontainers` **4.15.0** started one `busybox:1.36`
container with a trivial `UntilCommandIsCompleted("true")` wait strategy, with `DOCKER_HOST` pointed
at a fully permissive wollomatic instance logging at `DEBUG`. The complete API surface of that
single, minimal container:

| Method | Path | In the §3 allowlist? |
| --- | --- | --- |
| `GET` | `/_ping` | allowed |
| `GET` | `/version` | allowed |
| `GET` | `/info` | allowed |
| `GET` | `/images/busybox:1.36/json` | **denied** (§3.3) |
| `POST` | `/images/create` | **denied** — image pull |
| `POST` | `/containers/create` | allowed |
| `GET` | `/containers/{id}/json` | allowed |
| `POST` | `/containers/{id}/start` | allowed |
| `POST` | `/containers/{id}/exec` | **denied** — wait strategy |
| `POST` | `/exec/{id}/start` | **denied** — wait strategy |
| `GET` | `/exec/{id}/json` | **denied** |
| `POST` | `/containers/{id}/stop` | allowed |
| `DELETE` | `/containers/{id}` | **denied** — cleanup |

**Six of the thirteen endpoints Testcontainers needs are ones the deployment allowlist must deny** —
and this is the floor, with the Resource Reaper disabled and without networks, volumes, bind mounts
or a `.NET` test fixture of any real complexity. Any wait strategy other than the trivial one adds
more; `WithNetwork` adds `/networks/*`; `WithVolumeMount` adds `/volumes/*`.

### 7.2 The Resource Reaper makes it structural, not incidental

With Ryuk enabled the probe **failed** — Ryuk never reached readiness through the proxied endpoint —
so it is not merely a matter of widening an allowlist. Reading why, from Testcontainers' own source
(`src/Testcontainers/Containers/ResourceReaper.cs`, `develop`):

```csharp
_resourceReaperContainer = new ContainerBuilder(resourceReaperImage)
    .WithDockerEndpoint(dockerEndpointAuthConfig)
    .WithName($"testcontainers-ryuk-{sessionId:D}")
    .WithPrivileged(requiresPrivilegedMode)
    .WithMount(dockerSocket)
```

and the mount it is handed is `new UnixSocketMount(dockerEndpointAuthConfig.Endpoint)`, whose
`src/Testcontainers/Configurations/Volumes/UnixSocketMount.cs` sets
`Target = "/var/run/docker.sock"`.

**Ryuk bind-mounts the raw Docker socket into a container, and can run privileged.** It then issues
`DELETE` against containers, networks, volumes and images to clean up after a test run. That is not a
narrow allowlist; it is the whole socket, deliberately, by design — and it is a container created
with exactly the `HostConfig` shape §5.3 forbids.

### 7.3 The conclusion

**It is a different trust context, and the right answer is to name it as one rather than reconcile
it.**

- **It does not conflict with the deployment posture**, because it is not the deployment. PRD 27 and
  `trust-boundaries.md` §4 govern **ZWarden.Agent talking to a production host's Docker Engine**. The
  integration test suite is a developer workstation and a CI runner talking to a **disposable**
  daemon, with no tenant data, no production Server and no PZ runtime to protect.
- **It must not share the deployment proxy, and must not be given one.** Putting Testcontainers
  behind the §3.5 allowlist would fail at `POST /images/create` and again at exec — measured — and
  widening the allowlist to accommodate it would mean permitting exec, image pull and container
  delete in the *reference deployment*, which is the exact inversion to avoid. **The test harness
  must never be a reason to widen the production allowlist**, and that sentence is the finding.
- **What should be written down**: that CI runners and developer machines are inside the trust
  boundary of their own disposable daemon; that the integration suite requires unrestricted socket
  access and that this is intended; that CI runners are therefore treated as **build secrets-bearing,
  compromise-relevant infrastructure** in their own right (PRD 54, PRD 55); and that
  **no ZWarden.Agent production code path may depend on anything Testcontainers needs and the §3
  allowlist denies** — otherwise the tests would pass against a surface the deployment does not have.

That last point is the one with teeth, and it is a testable claim rather than a note: the Agent's
Docker abstraction should be exercised in at least one integration test **through a proxy carrying
the §3.5 allowlist**, so that any drift between what the Agent calls and what the deployment permits
fails the build. Testcontainers can start that proxy as just another container; the daemon it uses to
do so stays unrestricted. This gives the allowlist a test without pretending the harness lives under
it.

---

## 8. Cross-check against the PRD and the trust boundaries

**Nothing here contradicts [`../trust-boundaries.md`](../trust-boundaries.md).** §1 and §4 anticipated
this outcome; the measurements sharpen §4's "coarse proxy (endpoint and method allowlist) in front of
the socket as defence in depth" into a named component and a concrete list, and supply §4's
"Consequence to accept" with a second consequence — §5.3's create-body invariants — that deserves the
same test rigour.

Two things are worth recording in the PRD, neither a contradiction:

- **PRD 27's diagram is achievable, but its implied component is not the obvious one.** The
  "restricted Docker API/socket proxy" box is realisable only by wollomatic among maintained options;
  Tecnativa, the field's default choice, measurably cannot fill it (§4.2). If PRD 27 is ever
  annotated with a component name, it must not be Tecnativa.
- **PRD 27's "preferred *long-term* architecture" framing is right, and should not be upgraded.**
  `trust-boundaries.md` already reads it as defence in depth rather than a boundary. §5.2's
  measurement — a privileged container created straight through the best proxy in the field —
  is the evidence for keeping that framing exactly as it is.

One tension the PRD does not currently address, surfaced in §3.4: **PRD 22's canonical PZ image must
be pre-provisioned** if `/images/*` is denied, because container creation cannot pull it. That is a
reference-deployment requirement (PRD 58) and an operator-facing diagnostic requirement for
Feature 13, not a contradiction — but it is currently unwritten.

Consistent with PRD 24, 25 and 26 throughout: the create-body invariants of §5.3 are PRD 24's
container-security list restated as assertions; §3.3 denies `/networks/*` precisely so PRD 26's
isolation cannot be rewritten by the Agent; the allowlist reaches `GET /containers/{id}/json` because
PRD 25's labels are read from inspect.

---

## 9. Ranked list of what was not verified

1. **That wollomatic's regex allowlist has no bypass.** Nine traversal and encoding vectors were
   tested and all returned 403 (§4.4). That is a spot check by one person in one session, not an
   audit. The property the whole recommendation rests on — default-deny with anchored patterns — is
   argued from its documented design plus those nine results.
2. **Whether `-allowbindmountfrom` resists symlink and relative-path tricks.** Only literal `/`,
   `/etc` and an allowed subdirectory were tested. Paths like `/srv/zwarden/../..`, a symlink at
   `/srv/zwarden/escape`, or unusual `Mounts` shapes were not. Since §5.3 requires the Agent to
   assert mount paths anyway, this is not load-bearing — but the filter should not be credited with
   more than was measured.
3. **Everything was measured against Engine 29.2.1 / API 1.53**, not the v1.56 target (§2.3). No
   daemon serving 1.56 was available. The endpoint paths come from the v1.56 spec and the proxies do
   not parse versions, so the allowlist should hold — but "should hold" is inference.
4. **The Testcontainers surface in §7.1 is a floor, not a ceiling.** One container, one trivial wait
   strategy, Ryuk disabled, no networks or volumes. A real ZWarden integration fixture will call
   more. The conclusion (six denied endpoints, structural socket mount) only strengthens with scope.
5. **Ryuk's own Docker API calls were not captured** — the probe that would have shown them failed at
   Ryuk's readiness check. Its socket mount and privileged capability are read from Testcontainers'
   source; the specific `DELETE` calls it issues are inferred from its documented purpose.
6. **linuxserver 3.4.4 was tested in one configuration only** (§4.3). Notably `POST=1` was not
   exercised on it; the claim that `POST=1` restores Tecnativa's blast radius there is **inferred**
   from the shared HAProxy design, not measured.
7. **sockguard and mikesir87 were not run at all.** Both were excluded on maturity before probing, so
   their body-filtering claims remain README-level, exactly as `deployment-security-standards.md`
   §3.6 left them. If either is ever reconsidered, it must be probed.
8. **`GET /events` behaviour through an allowlist was not tested**, including whether streaming
   endpoints behave correctly through wollomatic over long-lived connections. §3.2 defers `/events`,
   so this was not on the critical path, but Feature 16 would need it.
9. **Log streaming was tested only as a single non-streaming request** (`?stdout=1` on a container
   with no output). Whether hijacked, multiplexed, long-lived log streams (PRD 38) work correctly
   through wollomatic — including its `-shutdowngracetime` behaviour mid-stream — is **untested and
   is the most likely place for an unpleasant surprise** in the recommendation.
10. **No proxy was tested under failure** — daemon socket disappearing, proxy restart mid-operation,
    or the `-stoponwatchdog` path. Objection 3 in §6.1 is therefore reasoned, not measured.
11. **Whether a maintained successor to `twistlock/authz` exists.** Forks were not enumerated; carried
    forward unresolved from `deployment-security-standards.md` §3.6.
12. **The two-port-stride allocation of Feature 13** was not examined for allowlist implications. It
    is expressed in the create body's `HostConfig.PortBindings`, so it falls under §5.3's
    Agent-side assertions, but no port-specific proxy constraint was investigated.
