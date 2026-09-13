# 24. The OpenTelemetry observability baseline: SDK, opt-in OTLP, no Prometheus

ZWarden's observability baseline is **OpenTelemetry, pinned at 1.18.0**, wired into both hosts
(ZWarden.Web and ZWarden.Agent): the SDK with a service-identifying resource, ASP.NET Core + HttpClient
auto-instrumentation, a ZWarden-owned `Meter` and `ActivitySource` per host, and exporters. **The OTLP
exporter is opt-in** — added only when an endpoint is configured, so a default install emits nothing off-box —
and the **Console exporter is added in Development** for local visibility. **No Prometheus exporter ships**:
OpenTelemetry core is stable at 1.18.0 but its Prometheus exporter has never stabilised, so it is not a v1.0
promise (scope-and-sequencing §6). This is a *baseline* — the pipeline and a small set of custom instruments —
not a full metrics surface.

- Status: accepted
- Decided in: [#37](https://github.com/MCrank/ZWarden/issues/37) (Feature 16, PR-B)
- Bears on: PRD 40 (operational visibility), [ADR-0021](./0021-serilog-is-the-logging-stack.md) (logging stack — logs stay Serilog/MEL; this ADR is metrics + traces), [ADR-0013](./0013-feature-0-build-and-ci-policy.md) (NuGet advisories are build failures — see Consequences), [ADR-0002](./0002-test-stack-and-ci-tiering.md) (exact version pins)

## Context

F16's scope asks for an "OpenTelemetry baseline" while explicitly excluding "Prometheus export as a supported
surface." Nothing observability-related existed before this feature — no OTel packages, no `Meter`, no
`ActivitySource`. Three questions had to be settled: how far the baseline goes, how signals leave the process,
and which package versions.

The self-hosted posture matters: a default install must not phone home. Telemetry that ships enabled, pointing
at some endpoint, would be a surprise egress from an operator's own box.

## Decision

- **Scope: SDK + auto-instrumentation + a ZWarden-owned Meter/ActivitySource per host.** ZWarden.Web registers
  ASP.NET Core and HttpClient instrumentation, a `ZWarden.Web` meter (`ControlPlaneMetrics`, with a
  health-transition counter) and a `ZWarden.Web` activity source. ZWarden.Agent registers HttpClient
  instrumentation (it hosts no inbound server, so no ASP.NET Core instrumentation), a `ZWarden.Agent` meter
  (`AgentMetrics`, with a metrics-sweep counter) and a `ZWarden.Agent` activity source. The custom instruments
  are deliberately minimal — a baseline others can extend, not an exhaustive surface.
- **Delivery: opt-in OTLP, Console in Development.** The OTLP exporter is wired only when an endpoint is set
  (`ZWarden:Observability:OtlpEndpoint` / `Agent:Observability:OtlpEndpoint`, or the standard
  `OTEL_EXPORTER_OTLP_ENDPOINT`). With no endpoint, no exporter is added and nothing leaves the process. The
  Console exporter is added under the Development environment only.
- **No Prometheus exporter.** The scope's explicit non-goal; its OTel exporter has never reached stable.
- **Version: 1.18.0**, co-versioned across core, exporters and instrumentation, pinned in
  `Directory.Packages.props` (ADR-0002).

## Alternatives considered

- **OTel 1.9.0** (the first version tried, co-released across the packages). **Rejected on measurement:** it
  trips `NU1902` advisories (GHSA-g94r-2vxg-569j on `OpenTelemetry.Api`, GHSA-4625-4j76-fww9 on the OTLP
  exporter), and ADR-0013 promotes NuGet advisories to build failures — so 1.9.0 reddens the build. 1.18.0 is
  past both, and is the version the scope doc already names as stable.
- **Prometheus scrape endpoint.** Excluded by scope; unstable exporter.
- **OTLP on by default (to a conventional local endpoint).** Rejected: silent egress from a self-hosted box is
  a surprise. Opt-in keeps the default install quiet.
- **A shared telemetry project across both hosts.** Rejected as premature: the two hosts instrument different
  things (a hub vs. a sampler) and each owns its own meter/source; a shared abstraction would be indirection
  without a second consumer. Each host has a small `Observability/` folder instead.

## Consequences

- **A new dependency tree in both hosts**, pinned and advisory-clean at 1.18.0. Future advisories on these
  packages become red builds (ADR-0013) — intended, and the reason the version is centrally pinned.
- **Logs are not moved.** ADR-0021's Serilog (Agent) / MEL (Web) logging is untouched; this ADR adds metrics
  and traces. Bridging logs into OTel is a later, optional step.
- **The baseline is thin by design.** Two custom counters and auto-instrumentation. Richer spans and metrics
  (e.g. per-operation traces) are follow-on work; the `ActivitySource`/`Meter` seams are in place for them.
- **Streaming stats stay out.** The F16 metrics sampler reads `stats?stream=false` (ADR-0008 amendment); no
  long-lived OTel or Docker stream is introduced, consistent with the untested-streaming caveat in ADR-0008.
