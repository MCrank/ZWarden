# 33. Diagnostics is an aggregating, read-only, transient sweep over ten domains

The diagnostics engine (F29) is an **aggregating** engine: one run produces one **`DiagnosticReport`** — a set of
per-domain `DiagnosticCheck`s (`Pass`/`Warn`/`Fail`/`Skipped`) across the ten domains — **not** a fan-out of
independent Operations. Every check is **read-only**: the engine diagnoses and **never remediates**. In-process
domains (Web, Database, TLS, Agent connectivity) evaluate on the Web host; the Agent-side domains are gathered
read-only from the owning Agent (F29 PR-B/PR-C). A run is **transient** — the report is surfaced and not persisted;
**F30** owns the durable, redacted, packaged form (its `DiagnosticId`, redaction, secret scan, ZIP). Every
`DiagnosticCheck.Detail` is **untrusted** and is carried verbatim, bounded, and escaped only at render.

- Status: accepted
- Decided in: #49 (F29 — Diagnostics Engine); mini-plan `docs/feature-plans/F29-diagnostics-engine.md`
- Bears on: PRD 50 (the twelve deterministic tests), PRD 53 (privacy-aware diagnostics), scope-and-sequencing §6/§7
  (F29 splits by domain group; the split does not change the DAG), trust-boundaries §8 (untrusted runtime data);
  builds on ADR 0018 (fail-closed authorization), ADR 0022 (read-only/host-level Operations never contend for the
  per-server lock), ADR 0023 (the `ProbeStatus` verdict vocabulary), ADR 0026 (the Agent-owned RCON probe); feeds
  ADR-to-come for F30. Relates to ADR 0007 (mTLS deferred to v1.1 — see the TLS decision below).

## Context

PRD 50 requires deterministic health and diagnostic tests *before* relying on AI assistance, listing twelve
concrete checks across ten domains (DB, Docker, Agent, RCON, filesystem, SteamCMD, mod, TLS, compatibility, plus
base health). Prior features already built the per-domain seams a diagnostic would call — `IContainerRuntime`
(F13), `IRconHealthProbe` (F18), `INetworkReachabilityProbe`/`IServerDiskUsageReader` (F16),
`IServerUpdateRunner`/`SteamAppManifest` (F17), `IModDiscovery`/`ModCompatAnalyzer` (F21/F22),
`IPzConfigDocument`/`IPzConfigValidator` (F20a) — and the F16 health model already defines a four-value probe
verdict (`ProbeStatus`). What did not exist was an *aggregation*: a single report shape a run produces and an
operator (and later F30) reads. The forces: F30 needs one structured artifact to collect, redact, and package;
operators need one screen that answers "what is wrong?" without SSH (criterion 12); and the domains are
heterogeneous — some answerable on the Web host, most only on the Agent — so the engine must compose in-process
evaluation with read-only Agent round-trips.

## Decision

1. **Aggregate, not fan-out.** A run yields one `DiagnosticReport { Checks, RanAt }` where each `DiagnosticCheck`
   is `{ Domain, Status, Summary, Detail }` and `Status` is the domain-level `DiagnosticStatus`
   (`Pass`/`Warn`/`Fail`/`Skipped`), a twin of the wire `ProbeStatus` (the Application layer references no wire
   types). In-process domains evaluate on the Web host through Application probe ports; the Agent-side domains are
   gathered over **two additive, non-mutating Operations** — `GatherHostDiagnostics` and `GatherServerDiagnostics`
   (F29 PR-B) — each returning a bundle the engine merges into the one report.
2. **Read-only, never remediate.** No diagnostic mutates a Server, its world, its configuration, or its container.
   Both gather Operations are non-mutating (host-level / server-scoped read-only), so per ADR 0022 they never claim
   the per-server lock and a sweep can run *during* an in-flight lifecycle/backup Operation.
3. **Fail-closed and tenant-scoped.** A run authorizes the tenant-wide `Diagnostics.View` before any probe runs
   (ADR 0018) and is audited (`Diagnostics.Run`). `Diagnostics.Export` is reserved for F30's package. No permission
   catalogue or built-in-role change.
4. **Transient.** The report is not persisted. Only the ordinary `Operation.*` rows for the gathers and the run's
   audit event are durable. `DiagnosticId`, history, redaction, and the ZIP are F30's deliverables (PRD 51).
5. **All detail is untrusted.** `DiagnosticCheck.Detail` (a mod name, a config value, a certificate subject, a
   probe error, RCON output) is carried verbatim, **bounded** (`MaxDetailLength`), and escaped only at render —
   data, never markup (trust-boundaries §8). `Summary` is ZWarden-authored and safe.
6. **TLS is the public serving certificate only.** The TLS domain is a read-only outbound probe of the configured
   public HTTPS URL (`DiagnosticsOptions.PublicHttpsUrl`): presence, chain validity, hostname match, and expiry
   (Warn inside a window, Fail if expired/absent). It is **Skipped** when no public HTTPS URL is configured (an
   HTTP-only self-hosted deployment). It reads the public certificate only, transmits nothing over the connection,
   and closes immediately. **mTLS and the Agent transport are out of scope** (deferred to v1.1, ADR 0007).
7. **One failing domain never sinks the sweep.** Each domain check is evaluated in isolation; a probe fault/timeout
   becomes a `Fail`/`Warn` check with a legible detail, never a thrown or hung run.

## Alternatives considered

- **Fan-out of independent per-check Operations** (reuse F11 as-is — fire `DiagnosticsPing`,
  `DiagnosticsDockerHealth`, `RconHealthProbe`, and a new Operation per remaining domain, then stitch the results
  Web-side). Maximum reuse of the existing dispatch, but it floods the operation log with N Operations per run,
  yields a non-atomic snapshot scattered across time, and produces no single report object for F30 to collect. The
  aggregate gather *is* the F30 collection seam. Not taken.
- **Persist a `DiagnosticRun` entity now** (id, timestamp, per-domain results). Gives history and a stable
  `DiagnosticId` immediately, but adds a DB entity and a Postgres/Sqlite migration and pulls F30's persistence
  concern forward for a report an operator reads once. `DiagnosticId` is F30's deliverable. Not taken; the report
  is transient.
- **A broader TLS domain** (probe the RCON/Agent transports, or scan ciphers/protocols). RCON is a private,
  never-host-published listener with its own health probe (F18); the Agent transport's certificate story is ADR
  0007's v1.1 work; cipher/protocol hardening is F32/F40's concern. Not taken.

## Consequences

- There is now one canonical, machine-readable diagnostic report shape (`DiagnosticReport`) that F30 collects and
  redacts, and F31 turns into AI context — with the untrusted-data guarantee (PRD 52) inherited from this layer.
- Adding a diagnostic domain later means an evaluator + a fact source (in-process port or a field on a gather
  bundle) — not a new Operation kind per check, and not a schema change.
- The engine's own I/O correctness (DB connectivity, the TLS handshake, the Agent gather) is not unit-testable in
  isolation; the **pure evaluators and the assembly logic are** (the F16 `ServerHealthEvaluator` pattern), and the
  probe impls are covered by integration-tier tests. We accept that split deliberately.
- A run makes an outbound TLS connection from the Web host to its configured public URL. In a Caddy reference
  deployment (F32) that is egress to the public endpoint; when no public URL is configured the check is simply
  Skipped, so an HTTP-only self-hosted install pays nothing.
- The `CA5359` analyzer is suppressed at exactly one call site (the TLS probe's inspection callback), with a
  justification, because inspecting an invalid certificate to report *why* it is invalid is the point of the check
  and no data is sent over the connection. This is the documented targeted-suppression path (ADR 0013), not a
  widening of the warnings policy.
