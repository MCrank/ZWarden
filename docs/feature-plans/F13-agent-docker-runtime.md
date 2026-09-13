# Feature 13 Mini-Plan — Agent Docker Runtime

**Status:** IMPLEMENTED (PR pending). Roadmap issue: [F13 (#34)](https://github.com/MCrank/ZWarden/issues/34). Track C — the last Agent-plane feature before Track D server operations; **unblocks F14, F15, F16, F17, F18, F20, F24**.

**Two refinements settled during TDD, both from ADR 0008's allowlist:**

- **The health probe reports daemon reachability + negotiated API version only — not image presence.** The ten-entry allowlist denies `/images/*`, so image presence cannot be probed under the proxy without drift. Image absence surfaces instead at create time as `ContainerCreateFailure.ImageNotProvisioned` (the ADR's own "deny images, pre-provision, surface the 404" pattern). `DockerHealth` therefore carries `{DaemonReachable, ApiVersion, Detail}`.
- **`Diagnostics.DockerHealth` reports over the wire as `OperationCompleted`** — Succeeded when reachable, Failed with an Agent-authored reason otherwise (no new event type). The API version is surfaced via structured logging, not the wire, keeping F13's wire surface to exactly one non-mutating command (the hierarchical health model is F16).

**Format:** PRD 60. **Written against:** [`scope-and-sequencing.md`](../scope-and-sequencing.md) §6 (F13), ADR [0008](../adr/0008-docker-socket-access-via-wollomatic-socket-proxy.md) (the proxy + ten-entry allowlist), ADR [0022](../adr/0022-operation-lifecycle-and-per-server-locking.md) (operations), the socket-proxy research ([`research/docker-socket-proxy.md`](../research/docker-socket-proxy.md), every claim measured against a live daemon), [`trust-boundaries.md`](../trust-boundaries.md) §1/§4/§7, and PRD 22–28.

## Objective

Build **ZWarden.Agent's Docker runtime** — the Agent-internal capability layer that discovers, recognises, creates, inspects and operates **canonical ZWarden.PZServer containers** on one host, and **nothing else**. F13 is the substrate F14/F15/F16 orchestrate; like F12 it must be **correct on its own, driven by tests**, because no server-registration or lifecycle Operation exists yet to drive it. Two properties are load-bearing and get authorization-grade test rigour:

1. **Allowed-container enforcement** (trust-boundaries §4) — every target-container call resolves the container and asserts it is canonical **and** assigned to *this* Agent before acting. A label-matching bug must refuse, not act on a foreign container.
2. **The `POST /containers/create` body is a correctness requirement, not hardening** (ADR 0008, research §5.3) — the create request is built from a **closed template**, never from caller-influenced input, and **no code path can set `Privileged`, host namespaces, `CapAdd`, `Devices`, an unpinned image, or a non-`/pz` mount**.

F13 must be correct **with the socket unproxied** (ADR 0008): the proxy is defence in depth against Agent *bugs*, never on the correctness path.

## Dependencies

All three blockers are **merged**:

- **F8** (#30, PR #74) — the Agent runtime skeleton: Worker Service, Serilog (ADR 0021), file identity, health, graceful shutdown, `HostingExtensions` DI seam.
- **F11** (#33, PR #78/#79) — the operations engine + `AgentCommandProcessor` dispatch seam + `Diagnostics.Ping` as the non-mutating command model, per-server locking (ADR 0022).
- **F12** (#21/#65, PR #71) — the **label contract** (`io.zwarden.managed=true`, `io.zwarden.runtime=project-zomboid`, `io.zwarden.schema-version=1` baked; `io.zwarden.server-id`/`io.zwarden.agent-id` set at create), the **filesystem contract** (`/pz/...`), the ports (`EXPOSE 16261/udp 16262/udp`; RCON 27015 private, never published), and the `steam_appid.txt=108600` invariant.

Consumes settled facts: ADR 0008's allowlist and create-body invariants; PRD 22 (canonical image), 24 (container security), 25 (labels), 26 (networks), 28 (ports).

## Decisions (settled in grilling)

- **D1 — Docker client library: `Docker.DotNet.Enhanced`.** The maintained fork the research already names (§2.3); it negotiates the API version via `GET/HEAD /_ping` by default rather than pinning a `/v1.xx` prefix — which ADR 0008 requires, given daemons in the field range API 1.48–1.56. The original `Docker.DotNet` is unmaintained since 2021; a hand-rolled `HttpClient` re-implements a solved problem over a security-critical body.
- **D2 — F13 delivers the abstraction + enforcement + one non-mutating diagnostic command; no mutating operator command.** The create/lifecycle *methods* exist and are tested, but the operator-facing commands that call them are **F14** (`RegisterServer`/provisioning) and **F15** (lifecycle Operations). F13's end-to-end slice is a **`Diagnostics.DockerHealth`** command mirroring F11's `Diagnostics.Ping` — demonstrable start-to-finish without stealing F14/F15's vocabulary.
- **D3 — the create body is a closed template.** A `PzContainerSpec` (domain-shaped inputs: server-id, pinned image ref, the two allocated ports, `/pz/data` mount, network name) maps to `CreateContainerParameters`; the invariants are structural — the factory has **no parameter** for privileged/host-namespace/device/cap, so they cannot be set, and an architecture test asserts it (PRD 15, research §5.3 item 1).
- **D4 — the allowlist drift test lands in F13.** ADR 0008's one adoptable claim: at least one integration test exercises the runtime **through a wollomatic proxy carrying the §3.5 allowlist**, so any drift between what the Agent calls and what the deployment permits fails the build.
- **D5 — the pinned image reference is configuration.** `AgentOptions` carries the canonical PZ image reference (a pinned `repo@sha256:…` in production, a local tag in dev); F13 owns the **image-absent diagnostic** (`404 No such image` → an actionable "pre-provision the image" message, not an opaque failure).

## Scope

1. **Docker abstraction seam** — `IContainerRuntime` in `ZWarden.Agent.Docker`, wrapping the `Docker.DotNet.Enhanced` client behind a domain-shaped interface: daemon ping + API-version negotiation, list managed containers, inspect, create, start, stop, restart. Transport-isolated so it is faked in unit tests and hit for real in the integration tier.
2. **Canonical-label validation** (PRD 25) — `CanonicalLabels` constants + a validator that recognises a ZWarden container by the baked `io.zwarden.*` set. A container missing the set is **not** ZWarden's.
3. **Allowed-container enforcement** (trust-boundaries §4) — every target-container operation resolves the container and asserts `io.zwarden.managed=true` **and** `io.zwarden.agent-id == this Agent's id` before acting; a foreign or unassigned container yields a typed refusal (`ForeignContainerException`), logged. This is the check that "deserves the same test rigour as an authorization check".
4. **Container creation** — `PzContainerSpec` → closed `CreateContainerParameters` factory enforcing the 12 invariants (research §5.3): `Privileged=false`, empty `CapAdd`, `no-new-privileges`, named ZWarden network (never `host`/`container:`/`data`), no host PID/IPC/userns, no `Devices`, `/pz` mounts only with explicit RO flags, `ReadonlyRootfs` where practical, **pinned image digest**, full PRD 25 labels incl. `io.zwarden.agent-id`, non-root `User`, resource limits, and the two-port-stride bindings.
5. **Two-port-stride allocation** (PRD 28) — `PortStrideAllocator`, pure: given the base pair (16261/16262 UDP) and the strides already in use (from discovery of existing canonical containers' bindings), return the next free stride's concrete UDP pair. RCON (27015/tcp) is **never** host-published (F12/F18).
6. **Docker health diagnostics** — a `DockerHealthProbe` (daemon reachable? negotiated API version? canonical PZ image present?) surfaced two ways: into the F8 diagnostics/health surface, and via the **`Diagnostics.DockerHealth`** non-mutating command (D2).
7. **Configuration** — `AgentOptions` additions: Docker endpoint (unix socket / npipe / proxy TCP), the pinned PZ image reference (D5), the bind-mount root, the ZWarden network name.
8. **Architecture-guard changes** — relax `ReferenceDirectionTests` so the Agent **may** reference a Docker client (Web still must not); add the PRD 15 test that `Privileged` cannot be set true on any create path.

## Non-scope

- **Server registration / inventory** (F14) — no `RegisterServer` command, no persistence of Servers. F13 discovers containers on the host; it does not own the Server entity.
- **Lifecycle Operations** (F15) — the start/stop/restart *methods* exist and are tested; wiring them to durable, operator-dispatched Operations (with the FIFO `save`→`quit` stop path) is F15. F13 adds **no mutating command**.
- **The socket proxy's deployment** (F34) — F13 is correct with the socket unproxied (ADR 0008).
- **Hierarchical health** (F16), **SteamCMD lifecycle Operations** (F17), **RCON** (F18), **config** (F20), **backups** (F24), **live log streaming** (F27, and its untested-through-wollomatic caveat, ADR 0008).
- **Image build** — F12 owns `src/ZWarden.PZServer`; F13 consumes its contracts. **No `/images/*` pull** (allowlist); the image is pre-provisioned (D5).
- **Multi-host** — one Agent, one host.

## Domain changes

**None to the .NET domain entities.** `AgentId` and `ServerId` typed IDs already exist (F1). The new types (`IContainerRuntime`, `PzContainerSpec`, `PortStrideAllocator`, `CanonicalLabels`) are Agent-internal capability types, not Domain model — the Agent references no Infrastructure/EF (arch guard, §9 rule 1). No `CONTEXT.md` change: F13 is the first concrete orchestration of the existing **ZWarden.PZServer** and **Host** terms.

## Contract changes

- **One new command**: `Diagnostics.DockerHealth : AgentCommand` in `ZWarden.Contracts`, a sealed record with a `ProtocolMessageAttribute`, non-mutating, host-level (acts on the Agent, not a Server — ADR 0022 read-only/host-level Operations never contend). Its result reports daemon reachability, negotiated API version, and canonical-image presence. Mirrors `Diagnostics.Ping` (F11); handled in `AgentCommandProcessor` with the same dedupe-by-`OperationId`.
- **No wire change** to create/lifecycle — those commands are F14/F15. The **label contract** and **filesystem contract** (F12) are consumed unchanged.

## Security considerations

- **The 12 create-body invariants are correctness, not hardening** (ADR 0008, research §5.3). Enforced *structurally* (the factory cannot express the unsafe fields) and asserted by an architecture test — because no proxy blocks a `Privileged:true` create body (measured), item 1 protects the **host**, not just other containers.
- **Allowed-container enforcement is authorization-grade** (trust-boundaries §4): a label bug must refuse, not act. Tested for both directions — canonical+assigned acts; foreign/unassigned/unlabeled refuses.
- **Correct with the socket unproxied** (ADR 0008): the enforcement lives in Agent code; the proxy is bug-containment only.
- **No image pull, no arbitrary image**: `/images/*` denied; the image is the pinned digest from config; a caller-supplied reference is impossible by construction (§5.3 item 9).
- **No secrets**: no registry credentials (image pre-provisioned), no RCON password (F18), no DB reachability (PZServer off the `data` network — PRD 24, trust-boundaries §7). The Docker endpoint config carries no secret material.
- **Untrusted daemon output** (trust-boundaries §5/§8): inspect/label data is attacker-influenceable if a container is compromised; F13 treats labels as data for the enforcement check and never as instructions.

## Test plan

Tests are written before the code (PRD 2.2). Tier split by what they need (ADR 0002).

**Offline unit tier (no Docker, no network):**

1. **Canonical-label validation** — the full `io.zwarden.*` set recognises; any missing/wrong label rejects.
2. **Allowed-container enforcement** — canonical + `agent-id==self` ⇒ act; foreign `agent-id`, missing `managed`, or unlabeled ⇒ `ForeignContainerException`; asserted on inspect/start/stop/restart alike.
3. **Create-body invariants** — the factory output has `Privileged=false`, empty `CapAdd`, `no-new-privileges`, named network (never host/`data`), no host namespaces, no devices, `/pz` RO mounts only, `ReadonlyRootfs`, the pinned digest, the full label set incl. `agent-id`, non-root user, resource limits, the allocated ports. One case per invariant.
4. **Port-stride allocation** — server 0 ⇒ 16261/16262; with strides {0,1} in use ⇒ 16265/16266; a freed middle stride is reused; RCON never appears in the host bindings.
5. **API-version negotiation** — the runtime reads `Api-Version` from `/_ping` and never emits a literal `/v1.xx` (mocked handler).
6. **Image-absent diagnostic** — a `404 No such image` maps to a specific, actionable "pre-provision the canonical PZ image" diagnostic, not a generic failure.
7. **`Diagnostics.DockerHealth` handling** — `AgentCommandProcessor` dispatches it, dedupes by `OperationId`, returns the health result; an unknown command still returns null (regression on F11 behaviour).

**Architecture tier:**

8. **Reference direction** — Agent **may** reference `Docker.DotNet(.Enhanced)`; **Web must not** (relaxes the current F10 guard). Persistence guard still holds (no EF/provider in the Agent).
9. **No privileged path** (PRD 15) — no create path can set `Privileged=true` (the factory has no such input).

**Integration / networked tier (Testcontainers, disposable daemon — a different trust context, ADR 0008 §7):**

10. **Discovery** — create a lightweight container carrying the `io.zwarden.*` labels + one without; the runtime lists only the canonical one, scoped to this Agent.
11. **Create→inspect→start→stop→restart round-trip** against a **stub labelled image** (not the full PZ image — that is F12's scheduled tier), asserting state transitions and that the created container really carries the invariant `HostConfig`.
12. **Enforcement against a real foreign container** — a canonical container labelled with a *different* `agent-id` is refused.
13. **Allowlist drift (ADR 0008's adoptable claim, D4)** — run the round-trip **through a wollomatic proxy carrying the §3.5 allowlist**; every F13 call succeeds and a denied verb (e.g. `kill`) is refused. Drift between what the Agent calls and what the deployment permits fails the build. The daemon Testcontainers uses to start the proxy stays unrestricted.

## Implementation slices

Each is independently verifiable and inside one agent context.

- **S1 — Docker client dependency + arch guard.** Add `Docker.DotNet.Enhanced` to the Agent; relax `ReferenceDirectionTests` (T8) and add the no-privileged test (T9). *Verify:* architecture tier green; solution builds.
- **S2 — Labels + enforcement.** `CanonicalLabels`, the validator, `ForeignContainerException`, the enforcement wrapper. *Verify:* T1, T2.
- **S3 — `IContainerRuntime` + negotiation + health probe.** The seam over the client, `/_ping` negotiation, `DockerHealthProbe`, the image-absent diagnostic mapping. *Verify:* T5, T6 (faked client).
- **S4 — Create template + port stride.** `PzContainerSpec` → closed factory (12 invariants), `PortStrideAllocator`. *Verify:* T3, T4.
- **S5 — `Diagnostics.DockerHealth` command.** Contract record + `ProtocolMessageAttribute` + `AgentCommandProcessor` dispatch + Web-side dispatch (mirror F11). *Verify:* T7; contract catalogue tests green.
- **S6 — Integration tier.** Testcontainers discovery/round-trip/enforcement, then the wollomatic allowlist drift test. *Verify:* T10–T13 green on the networked tier.

## Diagnostics

- **Daemon unreachable** — the health probe reports it distinctly from "reachable but no image".
- **Image not pre-provisioned** — `404 No such image` → the actionable message naming the pinned reference to `docker pull`/`compose pull` (D5, ADR 0008 consequence).
- **Denied verb** (proxy present) — a `403`/`405` from the allowlist surfaces as a specific "operation not permitted by the socket proxy" diagnostic, not an opaque transport error (research §3.5 notes 405=no method rule, 403=path miss).
- **Foreign-container refusal** — logged with the offending container's id and label mismatch, at the enforcement boundary.
- **API-version mismatch** — if the daemon is too old/new, the negotiated version and the daemon's max are both reported.

## Documentation

- `src/ZWarden.Agent/README.md` (or the Agent's docs) — the Docker runtime: the abstraction seam, the label/assignment enforcement, the create template and its invariants, the port-stride scheme, the `Diagnostics.DockerHealth` command, and the config keys (Docker endpoint, pinned image ref, network name, bind-mount root).
- `CONTRIBUTING.md` — a note that the F13 integration tier needs Docker (a disposable daemon) and how the wollomatic allowlist drift test runs.
- **No new ADR** — the load-bearing decisions are ADR 0008 (proxy, allowlist, create-body invariants) and ADR 0022 (operations). D1 (client library) is implementation within them; if it later proves contentious it graduates then.

## Acceptance criteria

1. The Agent references a Docker client; Web does not; no create path can set `Privileged=true` (architecture tier green).
2. Canonical-label validation and allowed-container enforcement refuse foreign/unassigned/unlabeled containers on every target-container operation (offline tier).
3. The create template satisfies all 12 research §5.3 invariants, structurally and by test.
4. Two-port-stride allocation is correct, reuses freed strides, and never publishes RCON.
5. The runtime negotiates the API version via `/_ping` and never pins `/v1.xx`.
6. The image-absent case yields an actionable, specific diagnostic.
7. `Diagnostics.DockerHealth` dispatches end-to-end (Web → Agent → result) with F11's dedupe semantics; F11's unknown-command behaviour is unregressed.
8. The integration tier proves discovery, the create→lifecycle round-trip, foreign-container refusal, and the wollomatic allowlist drift test — the last so deployment/agent drift fails the build.

## Definition of Done

Per PRD 61, the applicable subset: acceptance criteria met; tests authored first as executable specifications; offline + architecture tiers green in CI and the networked tier authored; the create-body invariants and allowed-container enforcement carry authorization-grade tests; **no secrets** (pre-provisioned image, no RCON password, no DB reachability); diagnostics exist (daemon/ image/ denied-verb/ foreign-container/ version); failure modes modelled; correct with the socket unproxied; documentation updated; threat considerations reviewed against trust-boundaries §1/§4/§5/§7/§8 and ADR 0008. (Migrations, DB, authorization persistence are N/A — F13 adds no domain entity and no persistence; it is Agent-internal capability.)
