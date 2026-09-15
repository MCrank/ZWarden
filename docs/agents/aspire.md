# Aspire dev orchestration (agent skills)

ZWarden uses **Aspire 13.5 for the local inner loop and integration testing only** — see
[ADR 0031](../adr/0031-aspire-is-dev-test-orchestration-only.md) and the AppHost
[`src/ZWarden.AppHost/README.md`](../../src/ZWarden.AppHost/README.md). The one-command dev loop is
`aspire run` (after a one-time `dotnet dev-certs https --trust`); it boots Postgres + Web + Agent +
the wollomatic socket-proxy and self-enrolls the Agent.

## The committed Aspire skills

`aspire agent init` installs skills under `.claude/skills/`. ZWarden commits only the ones that fit its
dev/test-only posture:

- **`aspire-orchestration`** — AppHost lifecycle: `aspire run`/`start`/`stop`/`wait`/`ps`, resource
  management, recovering from file locks / port conflicts.
- **`aspire-monitoring`** — local observability: `aspire logs`/`otel`/`describe`, the dashboard. (Its
  Azure/AKS branches don't apply — ZWarden isn't deployed via Aspire.)
- **`dotnet-inspect`** — evidence for .NET packages/APIs/assemblies.
- **`playwright-cli`** — browser automation / screenshots against the running graph (the real-UI check).
  The skill is committed; the local Playwright binary + `.playwright/` config are gitignored.

## Deliberately NOT committed (they conflict with ZWarden's decisions)

- **`aspire-deployment`** — deploying via Aspire to Azure/K8s/AWS/compose. **ADR 0031** keeps Aspire out
  of production; deployment is **F34 (#53)**, self-hosted. Do not reintroduce it.
- **`aspire-init` / `aspireify`** — scaffolding an AppHost and wiring `Aspire.ServiceDefaults`. The
  AppHost already exists, and **D-SVCDEFAULTS** (ADR 0031) forbids stock ServiceDefaults — Serilog
  (ADR 0021) and `AddZWardenTelemetry` (ADR 0024) stay the owners; the AppHost only injects
  `OTEL_EXPORTER_OTLP_ENDPOINT`.
- The top-level **`aspire`** router — it routes into the three above.

## Other tooling

- **MCP server** (`aspire agent mcp`) is a per-developer opt-in (runtime access to the running app); it
  is **not** committed. Configure it locally if you want it.
- Aspire CLI telemetry can be disabled with `ASPIRE_CLI_TELEMETRY_OPTOUT=true`.

If a future need genuinely calls for a dropped skill, add it back deliberately and reconcile it with
ADR 0031 first — don't let `aspire agent init` re-add the deployment/ServiceDefaults skills wholesale.
