# 21. Serilog is ZWarden's logging stack

**ZWarden uses Serilog for application logging**, introduced by the ZWarden.Agent runtime (F8) via
`Serilog.Extensions.Hosting` with the **two-stage bootstrap** pattern, **Console + rolling File**
sinks, `FromLogContext` enrichment, and levels read from configuration. This is the standard the
other hosts (ZWarden.Web) migrate to; Serilog is introduced here, not forked here.

- Status: accepted
- Decided in: #30 (F8 mini-plan), maintainer decision on the logging stack
- Bears on: PRD §38 (operational logging); F8 (Agent runtime skeleton); every later feature that logs

## Context

F8 is the first feature whose real deliverable is host diagnostics — a structured startup banner,
health-transition records, configuration- and identity-failure reporting. At the point F8 began the
repository had **no logging framework**: every host, ZWarden.Web included, ran on the default
`Microsoft.Extensions.Logging` (MEL) console provider. Logging is cross-cutting, so whatever the
Agent adopts sets a precedent the Web side will eventually match — the choice is not really
"Agent-only".

Two forces made this a decision rather than a default: the maintainer's stated preference for
Serilog, and F8's diagnostics being exactly the kind of structured, sink-configurable output
(rolling files, JSON-capable sinks, log-context enrichment) that Serilog does better than the
default console provider.

## Decision

- **Library:** Serilog, wired through `Serilog.Extensions.Hosting`. The Agent host calls
  `AddSerilog((services, cfg) => cfg.ReadFrom.Configuration(...).ReadFrom.Services(...).Enrich.FromLogContext())`.
- **Two-stage bootstrap:** `Program.cs` creates a `CreateBootstrapLogger()` **before** host
  construction, wraps host build/run in `try/catch/finally`, logs a fatal on an unhandled startup
  exception, and calls `Log.CloseAndFlush()` in `finally`. This guarantees a diagnosable record even
  when the host fails to build (for example, when `ValidateOnStart` rejects configuration).
- **Sinks:** Console and rolling File (`Serilog.Sinks.Console`, `Serilog.Sinks.File`), configured in
  `appsettings.json` under `Serilog` so operators tune levels and paths without a rebuild.
- **Pins (ADR 0002 discipline, central management):** `Serilog.Extensions.Hosting` 10.0.0,
  `Serilog.Settings.Configuration` 10.0.1, `Serilog.Sinks.Console` 6.1.1, `Serilog.Sinks.File`
  7.0.0.
- **Redaction stance:** secret material is carried in secret-aware types (F3) that do not stringify,
  and Serilog's destructuring must never be configured to serialise such a type. F8 has no secret to
  log; it establishes the discipline before F9's enrollment credential arrives.

## Alternatives considered

- **Default `Microsoft.Extensions.Logging` only.** Rejected: it is present and adequate for
  unstructured console output, but F8's job is structured, sink-configurable diagnostics, and the
  maintainer's preference is Serilog. Choosing MEL now would only defer the same decision to the
  first feature that needs a file sink or structured enrichment.
- **Serilog in the Agent, tactically, with no ADR.** Rejected: it would fork the logging stack
  across components with no recorded rationale — precisely the kind of surprise an ADR exists to
  prevent.

## Consequences

- ZWarden gains a structured, configuration-driven logging stack from F8 onward, and later features
  inherit it for free.
- **Cost knowingly accepted:** ZWarden.Web still runs on MEL. Its migration to Serilog is a tracked
  follow-up rather than shipping atomically with F8 — so for a window the two hosts log through
  different frameworks. This is deliberate: F8's scope is the Agent runtime, and a Web logging
  migration does not belong inside it.
- The two-stage bootstrap adds a small amount of ceremony to every Serilog-hosted entry point; it is
  the price of a diagnosable startup failure and is documented here so it is copied, not reinvented.
- The redaction stance is now a standing rule: introducing a Serilog destructuring policy that
  serialises a secret-aware type would violate this ADR.
