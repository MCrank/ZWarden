# Issue #313 Mini-Plan — Live* panels take typed ids

**Status:** one PR (branch `feat/313-live-panels-typed-ids`, closes #313). Follow-up from #299.

**Written against:** issue #313; `docs/feature-plans/F299-interactive-server-detail.md` ("Not done here");
[ADR 0046](../adr/0046-pages-may-opt-into-interactive-rendering-the-shell-stays-static.md).

## Objective

The `Live*` panels were interactive islands inside a static page, so their parameters crossed the static→interactive
boundary as strings and each panel parsed them (and guarded against a malformed id). Since #299 the whole Server Detail
page is interactive and the panels are plain children of a section: take typed values instead and drop the string
round-trip.

## Facts found (2026-10-04)

- Five panels remain: `LiveServerPanel`, `LivePlayerRosterPanel`, `LiveServerLogPanel`, `LiveConsoleOutputPanel`,
  `LiveServerDiagnosticsPanel`. `LiveModInventoryPanel` (named in the issue) was removed by #292.
- Each call site is in `Components/Pages/Servers/Sections/*` and already holds a typed `Server.Id` / `Server.AgentId`;
  `ConsoleSection` holds the enqueued `OperationId` and stringifies it; logs/console pass `OperatorZone.Id` (the
  cascaded `TimeZoneInfo`), which the panel resolves back.
- No test exercises a malformed id, so dropping the guard removes no test.

## Decisions

- **D1 — parameters.** `ServerId ServerId` and `AgentId AgentId` (`[EditorRequired]`), `OperationId? OperationId`
  (console), and `TimeZoneInfo Zone = TimeZoneInfo.Utc` (logs, console) — the same name and default as `ModsTable.Zone`.
- **D2 — the "unavailable" states go.** The console and diagnostics panels' "unavailable for this server" text was only
  reachable with a malformed id; it goes with the guard. The logs panel keeps its subscribe-based empty text (it is about
  the subscription, not the parse).
- **D3 — tests.** The existing bUnit tests switch to typed values; behaviour is unchanged, so the test count and the
  Web.Tests floor stay as they are.

## Slices

1. `LiveServerPanel` + `OverviewSection`.
2. `LivePlayerRosterPanel` + `PlayersSection`.
3. `LiveServerDiagnosticsPanel` + `DiagnosticsSection`.
4. `LiveServerLogPanel` + `LogsSection` (zone).
5. `LiveConsoleOutputPanel` + `ConsoleSection` (operation id, zone).
