# Feature 29 Mini-Plan — Diagnostics Engine

**Status:** PLANNED (2026-09-15) — the three load-bearing decisions locked as-recommended (2026-09-15) with the
maintainer. Roadmap issue: [F29 (#49)](https://github.com/MCrank/ZWarden/issues/49), **Track E — Operator
surface**. The feature that lets an operator **diagnose common failure modes from inside the application, without
SSH** (§11 criterion 12): run a read-only, authorized sweep across ten diagnostic domains and read back a single
report of per-domain **Pass / Warn / Fail / Skipped** checks with legible, untrusted-safe detail. This is the
deterministic health-and-diagnostics layer PRD 50 requires **before** any AI assistance (F31), and the structured
report **F30** collects, redacts, and packages.

**Depends on F3, F14, F16, F18, F20a, F22, F25, F27 — all merged and CLOSED (#24, #35, #37, #39, #40, #44, #46,
#47).** Blocks [F30 (#50)](https://github.com/MCrank/ZWarden/issues/50) per the issue's native dependency edge.

**Format:** PRD 60. **TDD is mandatory** (PRD 2.2). **Written against:**
[`scope-and-sequencing.md`](../scope-and-sequencing.md) §6 (F29) and §11 criterion 12 ("diagnose common failures
without SSH"), plus §7's explicit instruction that **F29 splits by domain group and the split does not change the
DAG** (every group has the same entry condition); PRD **50** (the twelve deterministic tests) and **53**
(privacy-aware diagnostics); [`trust-boundaries.md`](../trust-boundaries.md) **§8** (everything a Server, its
mods, its config, and its RCON emit is untrusted end to end) and **§9** (the closed command vocabulary). ADRs it
builds on:
[0018](../adr/0018-zwarden-owned-rbac.md) (fail-closed, tenant/server-scoped authorization),
[0020](../adr/0020-agent-protocol-versioning-and-catalogue.md) (additive-vs-breaking contracts; the closed
vocabulary), [0022](../adr/0022-operation-lifecycle-and-per-server-locking.md) (operation lifecycle — **read-only
and host-level Operations never contend for the per-server lock**, which is exactly why a diagnostic sweep can run
during other activity), [0023](../adr/0023-server-health-model.md) (the five-state health model and the four-probe
`HealthBreakdown`/`ProbeStatus` the report reuses), [0026](../adr/0026-rcon-foundation-private-transport-and-agent-owned-credential.md)
(the Agent-owned RCON probe), and [0030](../adr/0030-live-logs-stream-on-demand-sanitized-at-source-over-a-non-operation-channel.md)
(the live-panel UI pattern the async run reuses). New **ADR 0033** lands with the feature (the diagnostics engine's
aggregate read-only posture and the untrusted-report contract).

## Objective

Give an operator a single, fail-closed, **read-only** diagnostics sweep across the ten domains named in scope —
**DB, Docker, Agent, RCON, filesystem, SteamCMD, mod, TLS, compatibility, and the base health checks** — surfaced
as one `DiagnosticReport` of per-domain `DiagnosticCheck` results (each a `ProbeStatus` + a summary + bounded,
untrusted-safe detail). It slots into the existing backbone: the Web-side domains (DB, TLS, Web-self, Agent
connectivity) evaluate in-process; the Agent-side domains gather over **two new read-only Operations**
(`GatherHostDiagnostics`, `GatherServerDiagnostics`) that reuse the F11 engine and the already-built per-domain
seams (`IContainerRuntime`, `IRconHealthProbe`, `INetworkReachabilityProbe`, `IServerDiskUsageReader`,
`IServerUpdateRunner`/`SteamAppManifest`, `IModDiscovery`/`ModCompatAnalyzer`, `IPzConfigDocument`/`IPzConfigValidator`).
F29 **diagnoses only — it never remediates** (out of scope). It reuses the **already-reserved** tenant-wide
`Diagnostics.View` / `Diagnostics.Export` permissions and the empty **`ZWarden.Diagnostics`** project (its intended
home). **No new entity, no migration** — a run is transient (F30 owns `DiagnosticId`, history, and the package).

## The load-bearing decisions (LOCKED as-recommended, 2026-09-15)

- **D-1 — F29 is an *aggregating* engine: one `DiagnosticReport` per run, gathered read-only, not a fan-out of N
  separate operations. `[LOCKED]`** A run produces a single report — a set of `DiagnosticCheck { Domain, Status,
  Summary, Detail }` where `Status` is the existing `ProbeStatus` (Pass / Warn / Fail / Skipped, ADR 0023). The
  Web-side domains (DB, TLS, Web-self, Agent connectivity from the registry) evaluate **in-process**; the
  Agent-side domains gather over **two new, additive, read-only Agent commands** — `GatherHostDiagnostics`
  (host-level: Docker daemon, SteamCMD availability + installed build, host disk/filesystem) and
  `GatherServerDiagnostics` (per-server: RCON, game port, per-server filesystem, mod validation, config
  validation, compatibility). Each gather runs **all** its domain checks in **one round-trip** and returns a
  **bundle** of check facts; the engine merges the bundles and the in-proc checks into the one report.
  *Rejected — fan-out per-check Operations* (fire `DiagnosticsPing`/`DiagnosticsDockerHealth`/`RconHealthProbe` +
  a new op per remaining domain, stitch Web-side): maximum reuse of existing dispatch, but it floods the operation
  log with N ops per run, gives no single report object for F30 to collect, and yields a non-atomic snapshot
  (checks scattered across time). The aggregate gather **is** the F30 collection seam. Both gathers are
  **non-mutating** (host-level / server-scoped read-only), so — per ADR 0022 — they never claim the per-server
  lock and a sweep can run **during** an in-flight lifecycle/backup Operation (exactly when you want to diagnose).
- **D-2 — A diagnostic run is transient and on-demand; no persisted run, no `DiagnosticId` yet, no migration.
  `[LOCKED]`** Running diagnostics is like the F16 health panel and the F28 console: run → surface the report live
  → gone. F29's exit condition is "common failure modes can be diagnosed from within the application," which a
  transient report satisfies. **F30 (Sanitized Support Package)** owns the persistence concern the PRD attaches to
  it — `DiagnosticId`, run history, and the collected/redacted/secret-scanned ZIP (PRD 51). The only durable
  records F29 writes are the ordinary `Operation.*` rows for the gather Operations (the F11 engine persists those
  already) and the audit event for a run. *Rejected — persist a `DiagnosticRun` entity now:* it adds a DB entity
  + Postgres/Sqlite migration and pulls F30's job forward for a report the operator reads once; a stable
  `DiagnosticId` is F30's deliverable, not F29's.
- **D-3 — The TLS domain is a read-only probe of the configured public endpoint's serving certificate; mTLS stays
  out (v1.1). `[LOCKED]`** No TLS seam exists today — Agent transport is a bearer credential and **mTLS is
  explicitly deferred to v1.1 (ADR 0007)**. F29's TLS check (PRD 50 "Check TLS", for the F32 Caddy reference
  deployment) is an **in-process, read-only** probe of the configured public/base URL's serving certificate:
  present? chain valid? hostname match? days-until-expiry (**Warn** inside a threshold, e.g. 14 days; **Fail** if
  expired/absent when HTTPS is expected). It is **Skipped** when the deployment is HTTP-only or no public URL is
  configured (a legitimate self-hosted mode, not a failure). *Rejected — probe the RCON/Agent transports or do
  cipher/protocol scanning:* RCON is a private, never-host-published listener with its own health probe (F18); the
  Agent transport's certificate story is ADR 0007's v1.1 work; cipher/protocol hardening is F40/F32's concern, not
  a per-run diagnostic.

## Domain map (the ten domains → where each runs → seam → check)

| # | Domain | Runs | Seam / source | Check(s) | PR |
| --- | --- | --- | --- | --- | --- |
| 1 | **Web (base health)** | in-proc | the Web host itself | app responding; assembly/build version present | A |
| 2 | **Database** | in-proc | `ZWardenDbContext` (via an Application probe port) | `CanConnect`; **no pending migrations**; provider (Sqlite/Postgres) | A |
| 3 | **Agent** | in-proc (+ gather round-trip) | agent registry last-seen + the host gather itself | connected; last-seen freshness; command round-trips | A / B |
| 4 | **Docker** | host gather | `IContainerRuntime.ProbeHealthAsync` → `DockerHealth` | daemon reachable; negotiated API version | B |
| 5 | **RCON** | server gather | `IRconHealthProbe` → `RconHealthResult` | reachable; authenticated | B |
| 6 | **Game port** | server gather | `INetworkReachabilityProbe` (F16 UDP probe) | server UDP port reachable | B |
| 7 | **Filesystem** | host + server gather | `IServerDiskUsageReader` + a mount-permission probe | disk space (Warn under a free-space threshold); `/pz` mounts + `BackupRoot` present/writable | B |
| 8 | **SteamCMD** | host gather | `IServerUpdateRunner` availability + `SteamAppManifest` | SteamCMD usable; installed build id present/parseable | B |
| 9 | **Mod (validate)** | server gather | `IModDiscovery` + `ModInfoReader` | mods on disk parse; enabled-but-missing Workshop items | C |
| 10 | **Config (validate)** | server gather | `IPzConfigDocument` + `IPzConfigValidator` | the four config files parse; schema-valid (ADR 0010/0011) | C |
| — | **TLS** | in-proc | new X509 probe of the configured public URL (D-3) | cert present/valid/hostname; expiry Warn/Fail; Skipped if HTTP-only | A |
| — | **Compatibility** | server gather | `ModCompatAnalyzer` (installed build vs mod requirements) | version compatibility of enabled mods against the installed PZ build | C |

Ten domains as named in scope = the DB / Docker / Agent / RCON / filesystem / SteamCMD / mod / TLS /
compatibility set **plus the base health checks** (Web + game port fold in here); the table's twelve rows are the
PRD 50 tests. **Every check is read-only**: a probe that fails is a **Fail check with a legible detail**, never a
thrown/hung Operation (each gather catches per-domain and records a check, so one failing domain never sinks the
sweep). **All `Detail` text is untrusted** (mod names, config values, cert subjects, RCON output — §8): carried
verbatim, **bounded**, escaped **only** at render (D-6 below).

## Scope, by PR (split **by domain group** — §7; the DAG is unchanged, every group has the same entry condition)

### PR-A — Engine core + report model + the Web-side domain group (branch `feat/f29-diagnostics-engine-core`)

The framework and the domains that need **no** new Agent round-trip — shipped as a working vertical slice (run
diagnostics, read back DB / TLS / Web / Agent-connectivity checks) before touching the Agent protocol.

1. **Report model (`ZWarden.Diagnostics`, the reserved stub):** `sealed record DiagnosticReport(IReadOnlyList<DiagnosticCheck> Checks, DateTimeOffset RanAt, …)`
   and `sealed record DiagnosticCheck(DiagnosticDomain Domain, ProbeStatus Status, string Summary, string Detail)`
   (`Detail` **untrusted**, bounded); `enum DiagnosticDomain` (Web, Database, Agent, Docker, Rcon, GamePort,
   Filesystem, SteamCmd, Mod, Config, Tls, Compatibility). Reuse the contracts' `ProbeStatus` (Pass/Warn/Fail/Skipped).
2. **Pure per-domain evaluators** (facts → `DiagnosticCheck`, the `ServerHealthEvaluator` pattern — exhaustively
   unit-testable with no I/O): `DatabaseDiagnostic` (connect-state + pending-migration count + provider),
   `TlsDiagnostic` (cert presence/validity/hostname/expiry thresholds, Skipped-when-http — D-3), `WebSelfDiagnostic`,
   `AgentConnectivityDiagnostic` (registry last-seen freshness thresholds).
3. **Application ports for the in-proc facts** (keeps `ZWarden.Diagnostics` dependency-light — it references
   `Application` only): `IDiagnosticsDbProbe` (`CanConnectAsync` + pending migrations), `IDiagnosticsTlsProbe`
   (fetch the serving cert for the configured URL), an agent-registry read seam. Impls in `Infrastructure`/`Web`.
4. **`IDiagnosticsService` / `DiagnosticsService`** (`Application` interface + engine impl in `ZWarden.Diagnostics`):
   **fail-closed authorize `Permissions.DiagnosticsView` tenant-wide** (ADR 0018; → `NotAuthorized`), run the
   in-proc evaluators, assemble a `DiagnosticReport`. In PR-A the Agent-side domains report **`Skipped`
   ("gathered by the Agent — pending")**; PR-B fills them. **Audit** a `Diagnostics.RunStarted` event.
5. **Endpoint + DI + slnx:** `POST /api/diagnostics/run` (tenant-wide) and, in PR-C, `.../servers/{id}/diagnostics`;
   `AddZWardenDiagnostics()` in `Program.cs`. **Add `ZWarden.Diagnostics` to `ZWarden.slnx`'s build** and wire the
   project reference from `ZWarden.Web`. Unauthorized → 403; the run returns the report.
6. **ADR 0033** (aggregate read-only engine + untrusted-report contract) lands here.
7. **Tests (`ZWarden.Diagnostics.Tests` new tier-1 project + `Infrastructure.Tests`/`Web.Tests`):** evaluator
   units first (DB: connected / cannot-connect / pending-migrations / each provider; TLS: valid / expired /
   near-expiry-Warn / hostname-mismatch / no-cert / Skipped-when-http; agent freshness bands); service authz
   (fail-closed, tenant-scoped, unknown-tenant); endpoint status codes. Register the new test project's
   `--minimum-expected-tests` floor (and the `silent-drop-guard`/`offline` job wiring).

### PR-B — The Agent infrastructure domain group (branch `feat/f29-agent-infra-diagnostics`)

The read-only gather transport + the infra domains (Docker, Agent, RCON, game port, filesystem, SteamCMD),
red-green against the existing Agent fakes.

1. **Contracts (additive — ADR 0020, `ProtocolVersion.Current` stays 1):** `sealed record GatherHostDiagnostics : AgentCommand`
   `[ProtocolMessage("diagnostics.gather-host")]` (no payload, host-level) and `sealed record GatherServerDiagnostics : AgentCommand`
   `[ProtocolMessage("diagnostics.gather-server")]` (target rides the envelope `ServerId`). Bundle result records
   on `OperationCompleted` (nullable ⇒ additive, mirroring `RconHealthResult`): `HostDiagnosticsResult(IReadOnlyList<DiagnosticCheckFact> Checks)`
   and `ServerDiagnosticsResult(IReadOnlyList<DiagnosticCheckFact> Checks)`, where `DiagnosticCheckFact(DiagnosticDomainWire Domain, ProbeStatus Status, string Summary, string Detail)`
   carries untrusted `Summary`/`Detail` verbatim and bounded. Assert `ClosedCommandVocabularyTests` and the
   additivity/version guards stay green.
2. **`OperationKind`** — append `GatherHostDiagnostics = 20`, `GatherServerDiagnostics = 21` (stored by name; both
   **non-mutating** — host-level / server-scoped read-only; doc-comment: never claim the per-server lock, ADR
   0022). **No migration** (existing string column).
3. **Agent gatherers (`ZWarden.Agent/Diagnostics/`):** `HostDiagnosticsGatherer` (Docker via
   `IContainerRuntime.ProbeHealthAsync`; SteamCMD via `IServerUpdateRunner` availability + `SteamAppManifest`
   installed-build; host disk/filesystem via `IServerDiskUsageReader` + `BackupRoot` writability) and
   `ServerDiagnosticsGatherer` (RCON via `IRconHealthProbe`; game port via `INetworkReachabilityProbe`; per-server
   `/pz` mount filesystem/disk). Each domain check is wrapped so a probe failure/timeout becomes a **Fail/Warn
   check with a legible detail** — one bad domain never fails the whole gather. Never interpret untrusted output.
4. **Agent dispatch + reply:** `case GatherHostDiagnostics` / `case GatherServerDiagnostics` arms in
   `AgentCommandProcessor.ProcessAsync` (dedupe by `OperationId`), carrying the bundle on the `Completed(...)`
   builder; `OperationDispatcher.CommandFor` arms (`GatherHostDiagnostics → GatherHostDiagnostics`, etc.);
   `AgentHub.OperationCompleted` records the bundle into a new **ownership-guarded** `IDiagnosticsResultCache`
   (the `IPlayerRosterCache`/`IServerHealthCache` pattern — transient, keyed by server/host, no persistence).
5. **Engine integration:** `DiagnosticsService` enqueues `GatherHostDiagnostics` (and `GatherServerDiagnostics`
   for a targeted server), awaits/polls the Operation, reads the bundle from the cache, maps each
   `DiagnosticCheckFact` → `DiagnosticCheck`, and merges into the report (replacing PR-A's `Skipped` placeholders).
6. **Tests (`ZWarden.Agent.Tests` / `ZWarden.Contracts.Tests` / `ZWarden.Diagnostics.Tests`):** gatherer units
   against `FakeRconServer`, a fake container runtime, a fake disk reader, a fake update runner (Docker
   reachable/unreachable; RCON authed/refused/unreachable; port reachable/closed; disk healthy/low; SteamCMD
   present/absent; **one failing domain still yields a complete bundle**); contract additivity +
   `ClosedCommandVocabularyTests` green; `OperationDispatcherMapTests` for the two arms; `IDiagnosticsResultCache`
   ownership guard (a foreign agent can't shadow the owner's bundle); dispatch dedupe on redelivery. Bump floors.

### PR-C — The content domain group + UI (closes #49; branch `feat/f29-diagnostics-ui`)

The domains that read Server *content* (mods, config, compatibility) — additive fields on PR-B's server bundle,
no new contract — plus the operator-facing surface.

1. **Content checks folded into `ServerDiagnosticsGatherer`** (they ride the existing `ServerDiagnosticsResult`
   as additional `DiagnosticCheckFact` entries — no new command/contract): **mod validation** (`IModDiscovery` +
   `ModInfoReader` — parse failures, enabled-but-missing Workshop items), **config validation**
   (`IPzConfigDocument` + `IPzConfigValidator` — the four files parse + schema-valid, ADR 0010/0011),
   **compatibility** (`ModCompatAnalyzer` — enabled mods vs the installed PZ build). Same untrusted-safe, bounded,
   fail-soft posture.
2. **UI (Blazor, Bb components, SSR + one interactive island — [[blueprint-seam-on-ssr-forms]]):** a **Diagnostics
   card** on server-detail (`/servers/{id}`) gated `Diagnostics.View`, with a **Run diagnostics** action; results
   render as a per-domain list of `StatusBadge` (Pass/Warn/Fail/Skipped) + summary + **escaped** detail (data,
   never `MarkupString` — §8). The async run polls the gather Operation + `IDiagnosticsResultCache` via the F27/F28
   live-island pattern (`@rendermode InteractiveServer`, `PeriodicTimer`). A **tenant/host** diagnostics view
   surfaces the host-level report (Docker/SteamCMD/DB/TLS/Agent). Reuse `MeterBar`/`StatusBadge`.
3. **Docs:** ADR 0033 (finalized); a `docs/pzserver-architecture.md` "Diagnostics engine" row; `CONTEXT.md` terms
   (diagnostic **report** / **domain** / **check**); cross-reference F30 as the report's consumer.
4. **Tests:** content-check gatherer units; bUnit render/gating (card hidden without `Diagnostics.View`; untrusted
   mod/config/cert detail renders **escaped**; Skipped/Warn/Fail styling); loose JSInterop; real-host page +
   endpoint integration. Per-PR chore: bump the Web.Tests floor in **both** the csproj and `ci.yml`'s
   `tier1-silent-drop-guard` ([[web-tests-discovery-floor-bump]]); `npm run build:css` + commit `wwwroot/app.css`
   if a new utility is introduced ([[tailwind-app-css-rebuild]]).

## Non-scope

- **Remediation of any kind** — F29 diagnoses; it never restarts, reinstalls, re-permissions, renews a cert, or
  edits config. Every domain check is **read-only** (scope: "Not: remediation actions").
- **Persisted runs, run history, and `DiagnosticId`** — D-2; a run is transient. **F30** owns persistence,
  `DiagnosticId`, redaction, the secret scanner, and the ZIP package (PRD 51). F29 emits the untrusted report F30
  collects.
- **The AI troubleshooting context / vendor-neutral export** — F31 (post-1.1); F29 only marks collected text as
  untrusted so F30/F31 can carry that guarantee (PRD 52).
- **mTLS, the Agent-transport certificate, cipher/protocol scanning** — D-3; mTLS is v1.1 (ADR 0007); transport
  hardening is F32/F40. The TLS domain probes only the public serving cert.
- **A new permission** — `Diagnostics.View` / `Diagnostics.Export` already exist (tenant-wide). No catalogue or
  built-in-role change; `PermissionCatalogueTests`/`BuiltInRolesTests` stay untouched and green. (`Diagnostics.Export`
  is reserved for F30's package; F29 gates the run on `Diagnostics.View`.)
- **Any mutating Operation / new per-server-lock contention** — both gathers are non-mutating (ADR 0022), so a
  sweep runs during other activity.

## Domain / contract / persistence changes

- **Domain:** two `OperationKind` values (`GatherHostDiagnostics`, `GatherServerDiagnostics`; append, stored by
  name, **non-mutating**). New `DiagnosticReport` / `DiagnosticCheck` / `DiagnosticDomain` model in
  `ZWarden.Diagnostics`. **No permission change.**
- **Contracts (additive, no version bump — `ProtocolVersion.Current == 1`):** two `AgentCommand` leaves
  (`GatherHostDiagnostics`, `GatherServerDiagnostics`, no free-form string fields → closed-vocabulary guard stays
  green); two optional bundle result records (`HostDiagnosticsResult`, `ServerDiagnosticsResult`) with a
  `DiagnosticCheckFact` element carrying untrusted `Summary`/`Detail`. Assert additivity + closed-vocabulary +
  `ProtocolVersion.Current == 1`.
- **Persistence:** **none.** No new entity, **no migration** (the string `OperationKind` column already stores the
  two new kinds; the gather Operations use the existing `Operation` rows; the report is transient — D-2).
- **Solution:** `ZWarden.Diagnostics` (currently an empty stub already in `slnx`) gets its first source; a new
  `ZWarden.Diagnostics.Tests` tier-1 project joins the `offline`/`silent-drop-guard` CI wiring.

## Test plan (TDD, per PR)

- **PR-A (in-proc, offline):** evaluator units first (DB connect/migration/provider; TLS valid/expired/near-expiry-Warn/
  hostname-mismatch/no-cert/http-Skipped; agent freshness bands; web-self); `DiagnosticsService` authz (fail-closed,
  tenant-scoped) + report assembly with Agent domains `Skipped`; endpoint 403/200. New test project floor registered.
- **PR-B (Agent fakes, offline):** gatherer units (Docker/RCON/game-port/disk/SteamCMD each pass/warn/fail; **a
  failing domain still returns a complete bundle**); contract additivity + `ClosedCommandVocabularyTests`;
  `OperationDispatcherMapTests` (two new arms); `IDiagnosticsResultCache` ownership guard; dispatch dedupe; engine
  merge (bundle → report, placeholders replaced).
- **PR-C:** content-check gatherer units (mod parse/missing; config parse/schema; compatibility); bUnit
  render/gating (hidden without `Diagnostics.View`; untrusted detail escaped; Pass/Warn/Fail/Skipped styling),
  loose JSInterop; real-host page + endpoint integration.
- **Integration tier (`[Category("Networked")]`, deferrable to the opt-in tier as in F16/F18):** against a real PZ
  container over the `zwarden` network — run a full server gather, confirm each domain returns a check with
  untrusted detail rendered as text, and confirm a deliberately-broken domain (e.g. RCON disabled) surfaces a
  legible **Fail** without sinking the sweep.

## Diagnostics (security posture)

- **Read-only, fail-closed, tenant/server-scoped:** a run authorizes `Diagnostics.View` (tenant-wide) before any
  probe; a targeted server gather resolves the Server through the tenant filter (foreign/unknown ⇒ `ServerNotFound`).
  Nothing in F29 mutates a Server or its world.
- **No new lock contention (ADR 0022):** both gathers are non-mutating, so a sweep never blocks — and is never
  blocked by — a lifecycle/backup/restore Operation; you can diagnose *while* something is wrong.
- **All collected text is untrusted (§8 / PRD 53):** mod names, config values, cert subjects, RCON output, and
  probe details are carried verbatim, **bounded**, and escaped **only** at render (data, never `MarkupString`).
  This is the guarantee F30/F31 inherit ("mark logs and other runtime data as untrusted", PRD 52).
- **The RCON credential never leaves the Agent (§5, ADR 0026):** the RCON domain reuses `IRconHealthProbe`
  (reachable/authenticated only — never the password); Web never sees the secret.
- **No secret in a check:** the report carries statuses + bounded detail, never credentials, connection strings,
  or the serving cert's private key (the TLS probe reads the public cert only). Redaction/secret-scanning of the
  packaged export is **F30**'s gate (PRD 51 — package generation fails rather than emit prohibited material).
- **One failing domain never hangs a run:** each domain check is caught and mapped to a Fail/Warn check with a
  legible reason; a gather always completes with a full bundle (no partial hang — the F16/F18 "no response is a
  result, not a hang" posture).

## Documentation

- **ADR 0033** — "The diagnostics engine is an aggregating, read-only, transient sweep across ten domains
  producing one untrusted report": why aggregate over fan-out (D-1), why transient with persistence deferred to
  F30 (D-2), why TLS is a public-serving-cert probe with mTLS out (D-3), the read-only/no-remediation posture, and
  the untrusted-report contract F30/F31 inherit.
- `docs/pzserver-architecture.md` — add the "Diagnostics engine" row (ten domains; read-only; in-proc DB/TLS/Web +
  `GatherHost`/`GatherServer` Operations; tenant-wide `Diagnostics.View`; report untrusted).
- `CONTEXT.md` — add diagnostic **report** / **domain** / **check** terms.
- Cross-reference F29 from F30's plan as the report producer; note the F16 `ProbeStatus`/`HealthBreakdown` reuse.

## Acceptance criteria

1. An operator with `Diagnostics.View` can run a diagnostics sweep and read back a single `DiagnosticReport` of
   per-domain `DiagnosticCheck`s (Pass/Warn/Fail/Skipped) across the ten domains; the base health, DB, TLS, and
   Agent-connectivity checks evaluate in-process and the Agent infra + content domains gather over the two new
   **additive, non-mutating** Operations (`ProtocolVersion.Current == 1`; closed-vocabulary + additivity guards green).
2. **Every domain check is read-only** — no probe mutates a Server, its world, its config, or its container; the
   sweep runs even during an in-flight lifecycle/backup Operation (no per-server-lock contention, ADR 0022).
3. **One failing domain never sinks the sweep** — a failed/timed-out probe yields a legible Fail/Warn check and a
   complete report; RCON-disabled / Docker-unreachable / port-closed / low-disk / no-cert each surface a distinct,
   legible status.
4. **All collected detail is untrusted** (§8) — carried verbatim, bounded, escaped at render; a hostile mod name,
   config value, or cert subject renders as text, not markup.
5. **The TLS check** probes the configured public serving cert (validity/hostname/expiry with a Warn threshold)
   and is cleanly **Skipped** for an HTTP-only deployment; mTLS/Agent-transport TLS is out (D-3).
6. Authorization is **fail-closed and tenant-scoped** (`Diagnostics.View`; a targeted foreign server is
   `ServerNotFound`); **`Diagnostics.View`/`Diagnostics.Export` are used as-seeded** with **no catalogue or
   built-in-role change** (`PermissionCatalogueTests`/`BuiltInRolesTests` untouched and green).
7. **No new entity and no migration** — a run is transient; only the gather `Operation` rows and a run audit event
   are durable. F30 owns `DiagnosticId`, history, redaction, and packaging.
8. Offline unit tier green (evaluators + gatherers + service + cache + render); the networked integration test
   passes on demand; ADR 0033 written; docs updated; CI green (new `ZWarden.Diagnostics.Tests` floor wired into
   `offline`/`silent-drop-guard`; Web.Tests floor bumped).

## Definition of Done

Per PRD 61: acceptance criteria met; tests authored first (TUnit units for every evaluator and gatherer incl. the
fail-soft "one bad domain, full bundle" cases; bUnit UI with escaped untrusted detail; a networked integration
test for a live server gather, deferrable to the opt-in tier as in F16/F18); the engine diagnoses across all ten
domains and **remediates nothing** (scope); every probe read-only and every gather non-mutating (ADR 0022); all
collected text untrusted and escaped at render (§8); fail-closed tenant/server-scoped authorization on every run;
`Diagnostics.View`/`Diagnostics.Export` used as-seeded with no catalogue/role change; protocol changes additive
(`ProtocolVersion.Current == 1`, closed-vocabulary guard green); a run is transient with **no new entity and no
migration**; ADR 0033 written; docs updated; CI green.
