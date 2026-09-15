# ZWarden.AppHost — dev/test orchestration

> **DEV/TEST ONLY.** This project is the local inner-loop orchestrator and the host for integration
> tests. It is **not** a production artifact and does **not** govern deployment or packaging — that
> stays **F34 (#53)**. See [ADR 0031](../../docs/adr/0031-aspire-is-dev-test-orchestration-only.md) and
> the mini-plan [`docs/feature-plans/aspire-dev-orchestration.md`](../../docs/feature-plans/aspire-dev-orchestration.md).

## What it is

An [Aspire](https://aspire.dev) AppHost (13.5) that boots the whole ZWarden graph with one command and
gives you a dashboard with correlated logs, traces, and metrics across every resource.

**PR-1 (this spike) models the minimum:** a Postgres resource named `ZWarden` + the `ZWarden.Web`
project, with the connection string, the `postgres` provider switch, and the dev-only secrets Web fails
closed without (ADR 0015) all **injected** — so the inner loop needs no hand-set config. PR-2 adds the
`ZWarden.Agent` and the wollomatic socket-proxy; PR-3 adds the `Aspire.Hosting.Testing` integration
boot wired into CI's Docker tier.

## Run it

**One-time machine setup — trust the dev HTTPS cert:**

```sh
dotnet dev-certs https --trust    # click "Yes" on the Windows prompt
```

The dashboard talks to the AppHost's resource service over TLS. Without a *trusted* dev cert that gRPC
connection fails with `UntrustedRoot`, and the symptom is confusing: **the dashboard won't load and
every resource shows unhealthy in `aspire describe`, even though Postgres and Web are actually running
fine.** `aspire run` tries to do this trust step for you ("Trusting certificates…"); `aspire start`
(headless) does not, so trust it once by hand.

**Then boot the graph:**

```sh
aspire run            # from the repo root — interactive; prints the dashboard URL + login token
# or:
dotnet run --project src/ZWarden.AppHost
```

- `aspire run` is the inner-loop command: it launches the graph **and** the dashboard and prints the
  dashboard URL (with a one-time login token) plus the Web endpoint.
- `aspire start` runs the AppHost **headless in the background** — it does **not** open a dashboard.
  With one running, `aspire describe` lists the resources and `aspire dashboard` manages the dashboard.
- `AspireUseCliBundle=false` means DCP + the dashboard restore from NuGet, so a machine without the
  `aspire` CLI can still `dotnet run` / build the AppHost (see the lockfile note in the `.csproj`).

**Requirements:** a running Docker daemon (for the Postgres container), the .NET 10 SDK, and the
trusted dev cert above. The dev database persists in an Aspire data volume across runs, so migrations
only run once.

## Dev-only conveniences (never production paths)

- **Secret key ring** — a fresh 32-byte `ZW_SECRET_KEYS` is generated *per run* in `AppHost.cs` and
  injected. It is throwaway, never committed, and never a production key (ADR 0015 still fails closed;
  the AppHost simply supplies dev values, exactly as the `run-web` skill does).
- **Admin** — a confirmed Tenant-Owner (`admin@zwarden.test` / `Sup3r-Str0ng-P@ss!`) is injected so
  authenticated pages are reachable in the loop. Dev throwaway credentials only.

## Boundaries this project must hold

- **No Aspire dependency leaks into production `src/`.** Every Aspire package lives here (and, from
  PR-3, the Aspire test project); an architecture test enforces it. The `ProjectReference` to Web is
  outside-in source-generator plumbing and adds no code to Web.
- **The wollomatic allowlist is not relaxed for dev** (PR-2, ADR 0008).
- **No stock `AddServiceDefaults()`** — Serilog (ADR 0021) and `AddZWardenTelemetry` (ADR 0024) stay
  the owners; the AppHost only injects `OTEL_EXPORTER_OTLP_ENDPOINT` so telemetry lights up in the
  dashboard with no code change.
- **Build-policy relaxations stay scoped to this project** (ADR 0013) — see the `NoWarn` /
  `AspireUseCliBundle` comments in `ZWarden.AppHost.csproj`.
