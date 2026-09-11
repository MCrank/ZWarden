# Feature 12 Mini-Plan — Canonical PZ Container Foundation

**Status:** ready for implementation. Roadmap issue: [F12 (#21)](https://github.com/MCrank/ZWarden/issues/21). Track B — starts day one, parallel to Track A; the deliberate early risk-burndown of the sequencing spec.

**Format:** PRD 60. **Written against:** [`scope-and-sequencing.md`](../scope-and-sequencing.md) §6 (Track B), ADR [0009](../adr/0009-steamcmd-at-runtime-is-mandatory.md), the six trust boundaries ([`trust-boundaries.md`](../trust-boundaries.md) §5), the PZ runtime research ([`research/project-zomboid-runtime.md`](../research/project-zomboid-runtime.md), verified against **Build 42.20.4**), and PRD 22–28. The seven decisions below were settled in the F12 mini-plan grilling.

## Objective

Build **ZWarden.PZServer** — the canonical, self-owned Docker image and its startup/supervision tooling — such that **a clean container installs and launches a vanilla Project Zomboid dedicated server reproducibly**, always via a network SteamCMD install on first run (ADR 0009). This is the substrate the Agent (F13) later orchestrates; F12 must be correct on its own, driven by hand and by container-level tests, because no Agent exists yet.

## Dependencies

**F0** only (the repo, CI skeleton and the two CI tiers). It consumes settled facts, not other features:

- ADR 0009 — SteamCMD at runtime is mandatory; app **380870** (anonymous, free-to-download); no PZ artefact committed; two CI tiers.
- Research — two UDP ports (16261/16262) + private RCON (27015/TCP); bundled **Java 25** JRE (no system Java); install-dir vs `$HOME/Zomboid` user-data split, relocatable via `-cachedir`; config generated on first run; the FIFO `save`→`quit` stop path; `steam_appid.txt` must contain only `108600`; SteamCMD is 32-bit (i386 multiarch).
- PRD 22 (canonical container), 23 (filesystem), 24 (container security), 25 (labels), 26 (networks), 28 (ports).

## Scope

1. **Dockerfile (Q1)** — `debian:bookworm-slim` base; `dpkg --add-architecture i386` and the minimal i386 runtime libs SteamCMD needs; SteamCMD installed from **Valve's pinned CDN tarball** (`steamcmd_linux.tar.gz`), not apt and not a third-party image; a **non-root** user (uid/gid 10000, `pzserver`) owning `/pz`; `no-new-privileges`-friendly; `tini` as PID 1; the canonical filesystem; labels; `HEALTHCHECK`.
2. **Canonical filesystem (Q2, PRD 23)** — `/pz/server` (SteamCMD install of 380870), `/pz/runtime` (SteamCMD + Steam root + the `zomboid.control` FIFO), `/pz/data` (PZ user data via `-cachedir=/pz/data`; PZ creates `Server/`, `Saves/`, `Logs/`, `db/` under it), with the Workshop cache symlinked to `data/workshop`. `VOLUME /pz/data`. The PZ-name ↔ logical-name map is documented; the Agent presents the logical contract (PRD 23).
3. **First-run install tooling** — an idempotent entrypoint that detects a missing/incomplete install (sentinel: the server launcher + a completion marker) and runs SteamCMD `app_update 380870 [-beta $ZW_PZ_BETA] validate` via a runscript (`@ShutdownOnFailedCommand 1`, `@NoPromptForPassword 1`, `login anonymous`), **parsing stdout** for `Success! App '380870' fully installed` (exit codes are undocumented — ADR 0009); writes `steam_appid.txt` = `108600`.
4. **Launch tooling** — assemble the JVM/server command: bundled `jre64` (Java 25), ZGC + `-XX:+AlwaysPreTouch`, heap from env (Q4), `-cachedir=/pz/data`, `-servername servertest` (default), `ListenFIFO=/pz/runtime/zomboid.control` on stdin, bound to the two UDP ports.
5. **Version / branch (Q3)** — default `public` (42.20.x); `ZW_PZ_BETA` selects `legacy41` / `42.19`.
6. **Heap (Q4)** — `ZW_PZ_XMS` / `ZW_PZ_XMX` (or `ZW_PZ_HEAP`), default `4g`/`4g`.
7. **Base health check (Q5)** — `HEALTHCHECK` verifies the PZ `zombie.network.GameServer` Java process is alive and not crash-looping. Deliberately shallow; F16 layers the hierarchical model.
8. **Stop orchestration (Q6)** — the supervisor traps SIGTERM → `echo save > FIFO`, waits `ZW_PZ_STOP_GRACE` (default 30 s), `echo quit > FIFO`, waits for the JVM to exit; `tini` handles reaping/forwarding. Never a bare SIGTERM to the JVM.
9. **Container labels (PRD 25)** — `io.zwarden.managed=true`, `io.zwarden.runtime=project-zomboid`, `io.zwarden.schema-version=1` baked at build; `io.zwarden.server-id` / `io.zwarden.agent-id` are set by the Agent at **create** time (F13), not baked.
10. **Ports (PRD 28)** — `EXPOSE 16261/udp 16262/udp`; RCON 27015/tcp is **not** exposed (private; F18 owns it).
11. **Two CI tiers (Q7, ADR 0009)** — offline `bats` unit tests of the shell functions against synthetic fixtures + a stub `steamcmd`; a scheduled job doing the real install + boot.

## Non-scope

- **Agent-side orchestration** (F13): the Docker API, container discovery, label-scoped authorization, two-port-stride allocation. F12 is correct with the socket unproxied.
- **RCON** (F18) — no RCON password is set by the base image; the port does not listen by default.
- **SteamCMD lifecycle state machine** (F17: install/update/validate as durable Operations) — F12 does a one-shot first-run install only.
- **Config reading/editing** (F20a/b), **Workshop/mod management** (F21/F22), **backups** (F24) — `/pz/data/{config,workshop,backups}` exist as the layout; nothing manages them yet.
- **The hierarchical health model** (F16), **live logs** (F27), **lifecycle Operations** (F15).
- **Native (non-container) runtime** (post-1.1). **Windows** server image — Linux only in v1.0.
- **Multi-server port-stride allocation** — that is the Agent's (F13); one server per container here.
- **No PZ-derived artefact is committed** (ADR 0009) — synthetic fixtures only.

## Domain changes

**None.** F12 adds no entities, value objects or services to the .NET domain — it is the container build context under `src/ZWarden.PZServer`. It does fix one piece of **vocabulary already in `CONTEXT.md`**: ZWarden.PZServer is "the canonical managed Project Zomboid runtime container", and this feature is its first concrete form. No glossary change is needed.

## Contract changes

**None** in the API/SignalR/persistence sense. F12 establishes two contracts other features consume:

- **The filesystem contract** (PRD 23): `/pz/server`, `/pz/runtime`, `/pz/data/{Server,Saves,Logs,db,workshop,backups}`. Stable logical shape; F13/F15/F17/F20/F24 build on it.
- **The label contract** (PRD 25): the `io.zwarden.*` label set F13 validates against for canonical-container recognition, and the `steam_appid.txt=108600` invariant.

## Security considerations

- **Non-root by construction** (PRD 24): the server runs as uid 10000; `/pz` is owned by it; the image sets `USER pzserver`. Reference deployment adds `no-new-privileges`, dropped capabilities, read-only root FS with `/pz/data` (and the FIFO dir) the only writable mounts, and resource limits (F34 wires these; the image must not *require* extra capabilities).
- **Untrusted runtime** (trust-boundaries §5, §8): everything the PZ process emits — logs, later RCON output, config files once mods write them — is attacker-influenced. F12 introduces no code that trusts it; it only launches and supervises.
- **No credentials anywhere**: SteamCMD is `login anonymous` (ADR 0009 proved server + Workshop both work anonymously). No Steam account, no RCON password (F18), no DB reachability (trust-boundaries §7 — PZServer is off the `data` network, PRD 24).
- **No network at rest except the deliberate install**: first-run SteamCMD reaches Steam; after that the container needs only its game ports. Air-gapped is unsupported in v1.0 (ADR 0009).
- **`steam_appid.txt` = `108600` exactly** — a wrong/multi-line value causes "Illegal termination of worker thread" (research §2).

## Test plan

Tests are written before the scripts they cover (PRD 2.2). Because this is shell/container, the "unit" is a shell function and several tests are `bats` cases; the tier split is by **what they need** (ADR 0009).

**Offline tier (`bats`, synthetic fixtures, no network):**

1. **First-run detection** — with an empty `/pz/server`, the entrypoint decides to install; with a seeded fake install + completion marker, it skips. (stub `steamcmd` records its args.)
2. **SteamCMD invocation** — the generated runscript has `login anonymous`, `force_install_dir /pz/server`, `app_update 380870 validate`, and `-beta $ZW_PZ_BETA` only when set.
3. **Install success is read from stdout**, not the exit code — a stub emitting `Success! App '380870' fully installed` ⇒ proceed; one emitting `Error! App '380870' state is 0x…` ⇒ fail, whatever the exit code.
4. **Launch command assembly** — bundled `jre64`, ZGC + AlwaysPreTouch, heap from env (default `4g`), `-cachedir=/pz/data`, FIFO on stdin; overridden heap/servername/branch honoured.
5. **`steam_appid.txt`** is written containing exactly `108600`.
6. **Canonical filesystem** is created with the right owner and the Workshop symlink.
7. **Stop orchestration** — a SIGTERM to the supervisor writes `save`, waits, writes `quit` to the FIFO (a fake JVM reading the FIFO records the sequence and ordering); grace honoured.
**Image tier (PR; general network for apt + the SteamCMD tarball, but NO Steam/PZ install):**

8. **Labels / env / contract** — the image builds and carries the baked `io.zwarden.*` labels, a non-root user, a `HEALTHCHECK`, `VOLUME /pz/data`, and `EXPOSE`s only the two UDP ports (assert via `docker image inspect`). The build pulls Debian packages and Valve's SteamCMD tarball — general network — but never reaches Steam for PZ's files, so it runs on every PR.

**Scheduled tier (real Steam install + Docker):**

9. **Real install + boot** — run the image; SteamCMD installs 380870 from `public`; the server generates `servertest.*` and reaches a running `GameServer` process; a clean stop via SIGTERM produces a `save` then `quit` and a zero-ish exit. Validated against **files generated from a local install at test time** — nothing committed.

The distinction ADR 0009 draws is **Steam/PZ**, not all network: the always-on offline tier (the `bats` unit tests) needs no network at all; the image build needs general network but no Steam; only the real 6.72 GiB PZ install needs Steam, and that is the scheduled tier.

## Implementation slices

Each is independently verifiable and inside one agent context.

- **S1 — Shell entrypoint skeleton + `bats` harness.** The entrypoint factored into sourced functions (`pz-lib.sh`); `bats` wired into the offline CI tier; synthetic fixtures + a stub `steamcmd`. *Verify:* first-run detection tests (T1) green.
- **S2 — SteamCMD install path.** Runscript generation, stdout-parsed success/failure, `steam_appid.txt`, `-beta` handling. *Verify:* T2, T3, T5.
- **S3 — Canonical filesystem + launch command.** `/pz` creation and ownership, Workshop symlink, `-cachedir`, JVM/ZGC/heap/FIFO assembly from env. *Verify:* T4, T6.
- **S4 — Supervisor + stop orchestration.** `tini` PID 1, SIGTERM→save→grace→quit→wait against a fake JVM. *Verify:* T7.
- **S5 — Dockerfile.** Base, i386, SteamCMD tarball (pinned), non-root, labels, `EXPOSE`, `HEALTHCHECK`, `VOLUME`. *Verify:* image builds offline; T8 (labels/ports/health) green.
- **S6 — CI wiring.** Offline `bats` job (tier 1); the scheduled real-install job (tier 2). *Verify:* offline job green in CI; scheduled job authored.

## Diagnostics

- **Install failure** is legible: the runscript's stdout is surfaced, and the stdout-parse names *why* (the `Error! App … state 0x…` line) rather than an opaque undocumented exit code.
- **Boot failure** — the base `HEALTHCHECK` flips the container unhealthy when the `GameServer` process dies; the console log (`/pz/data/server-console.txt`) is the operator artefact (F27 streams it later).
- **Wrong `steam_appid.txt`** — documented failure mode ("Illegal termination of worker thread").
- **A torn/again install** — the completion marker makes first-run detection idempotent, so a restart after a partial install re-runs `validate` rather than assuming success.

## Documentation

- `src/ZWarden.PZServer/README.md` — replace the F0 placeholder: what the image is, the `/pz` layout and the PZ-name↔logical-name map, the env vars (`ZW_PZ_BETA`, `ZW_PZ_XMS/XMX`, `ZW_PZ_STOP_GRACE`), the ports, how to build and run it by hand, and the cold-start (network install) expectation.
- `CONTRIBUTING.md` — a short note on the two PZ CI tiers and running the `bats` suite locally.
- An ADR is **not** warranted: the load-bearing decisions (SteamCMD-at-runtime, no committed artefact, the two tiers) are already ADR 0009; F12's choices are implementation within it. If the base-image/SteamCMD-provisioning choice proves contentious later, it graduates to an ADR then.

## Acceptance criteria

1. `docker build` of `src/ZWarden.PZServer` succeeds (pulling apt packages + Valve's SteamCMD tarball, but never Steam/PZ) and produces a non-root image with the `io.zwarden.*` labels, `EXPOSE 16261/udp 16262/udp` (and not 27015), a `VOLUME /pz/data`, and a `HEALTHCHECK`.
2. The offline `bats` suite (T1–T7) is green with **no network** and no real PZ files; the image-contract check (T8) is green on PR.
3. First-run detection is idempotent: install when absent, skip when the completion marker is present.
4. Install success/failure is determined by **stdout parsing**, not the exit code.
5. The launch command uses the bundled Java 25, ZGC, env-driven heap (default `4g`), `-cachedir=/pz/data`, and the stdin FIFO.
6. A SIGTERM to the supervisor produces `save`, then (after the grace) `quit`, in that order, on the FIFO — never a bare SIGTERM to the JVM.
7. The scheduled tier installs 380870 from `public` and boots a vanilla server to a live `GameServer` process, against files generated at test time; **no PZ artefact is committed**.

## Definition of Done

Per PRD 61, the applicable subset: acceptance criteria met; the `bats` tests authored first as executable specifications; offline tier green; **no secrets** (anonymous SteamCMD, no RCON password, no DB reachability); diagnostic behaviour (stdout-parsed install, process `HEALTHCHECK`, idempotent re-install) exists; error/failure modes modelled (install error, wrong appid, torn install); documentation updated (`ZWarden.PZServer/README.md`, `CONTRIBUTING.md`); CI green; no committed PZ artefact; threat considerations reviewed against trust-boundaries §5/§8. (Migrations, authorization, audit, DB tests are N/A — F12 has no domain, no persistence, and runs no privileged Agent path.)
