# Issue #343 Mini-Plan — Naming sweep: "Enroll host" / "Host enrollment"

**Status:** one PR (branch `feat/343-enroll-host-naming`, closes #343). Part of epic #349 (Hosts polish).

**Written against:** issue #343 (decision 2026-10-04); `CONTEXT.md` (Host = machine, Agent = software,
Enrollment = trusting an Agent — unchanged).

## Objective

Operators see one name for bringing a machine under management: **Enroll host** for the action, **Host
enrollment** for the section. "Agent" stays where the text is technical ("run the ZWarden Agent on the host").

## Facts found (2026-10-04)

| Where | Today | Target |
|---|---|---|
| `/enrollment` page title / h1 | "Enrollment — ZWarden" / "Agent enrollment" | "Enroll host — ZWarden" / "Enroll host" (the target of the Hosts "Enroll host" button; #342 turns it into a sheet) |
| Settings card title / link | "Agent enrollment" / "Manage enrollment" | "Host enrollment" / "Enroll host" |
| Hosts empty state | "No Hosts enrolled yet. Enrol an Agent…" | "No hosts yet — enroll a host to get started." |
| Setup page title / h1 / step | "Enroll an Agent" / "Enroll your first Agent" / "Enroll Agent" | "Enroll host" / "Enroll your first host" / "Enroll host" |
| Setup TLS step button | "Set up an Agent & servers →" | "Enroll a host & add servers →" |
| Setup servers empty state | "Enroll an Agent and start it on a host…" | "Enroll a host and start its Agent…" |
| 2FA authenticator panel | "finish enrolment" (British) | "finish setup" (not host enrollment; just drops the stray spelling) |
| `docs/deployment/getting-started.md`, `compose-reference.md` | "Enroll your first Agent", "Enrolling the Agent" | "Enroll your first host", "Enrolling a host" |

Out of the sweep: route paths (`/enrollment`, `/setup/enroll`), form names, `data-*` hooks, type names
(`EnrollAgent.razor`), the PRD / scope docs and ADRs (historical, technical — "Agent enrollment" is the
domain term there), and the screenshot file `03-enroll-agent.png`.

## Decisions

- **D1 — text only.** No route, hook or type renames: they are not operator-facing and renaming them churns
  tests and links for no reader.
- **D2 — tests.** Update the expectation in `SetupStepIndicatorTests` and extend the existing page tests (`/enrollment`,
  Settings, setup, Hosts-empty) to assert the new strings and the absence of the old ones. No source-scanning guard:
  the suite has no repo-root helper and the rendered assertions cover each surface.

## Not done here

- The Enroll host sheet itself — #342.
