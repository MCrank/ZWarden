# ZWarden

ZWarden is a secure, self-hosted control plane for deploying, operating,
monitoring, and troubleshooting Project Zomboid dedicated servers.

## Agent skills

### Issue tracker

Issues and specs live as GitHub issues in `MCrank/ZWarden`, managed with the `gh` CLI. See `docs/agents/issue-tracker.md`.

### Triage labels

The five canonical triage labels, used verbatim: `needs-triage`, `needs-info`, `ready-for-agent`, `ready-for-human`, `wontfix`. See `docs/agents/triage-labels.md`.

### Domain docs

Single-context: one `CONTEXT.md` and `docs/adr/` at the repo root. See `docs/agents/domain.md`.

### UI components

ZWarden.Web UI uses **BlazorBlueprint** components (`Bb*`) by default (ADR 0003) — prefer a component
over raw HTML controls unless there's a security reason or no SSR-safe fit. Check the current API via
the `blazorblueprint` MCP and <https://blazorblueprintui.com/llms/index.txt> before using one. See
`docs/agents/ui-components.md`.

### Aspire dev orchestration

Aspire is **dev/test only** (ADR 0031): `aspire run` boots the whole graph (Web + Agent + Postgres +
wollomatic) for the local loop and integration tests — it never deploys or governs production (that's
F34). Committed skills: `aspire-orchestration`, `aspire-monitoring`, `dotnet-inspect`, `playwright-cli`.
The `aspire-deployment` / `aspire-init` / `aspireify` skills are deliberately not committed (they
conflict with ADR 0031 / no-ServiceDefaults). See `docs/agents/aspire.md`.
