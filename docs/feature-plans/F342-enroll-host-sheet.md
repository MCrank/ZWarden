# Issue #342 Mini-Plan — "Enroll host" sheet replaces the /enrollment page

**Status:** one PR (branch `feat/342-enroll-host-sheet`, closes #342). Part of epic #349 (Hosts polish).

**Written against:** issue #342; #338's mini-plan (`StepSheet`, the island-on-a-static-page pattern); #343 (the
"Enroll host" wording); ADR 0040/0046 (static shell, interactive islands, circuit-safe tenant via
`ActionScopeRunner`); F9 (`IEnrollmentService` authorizes `Tenant.Enrollment.Manage` and audits).

## Objective

The Hosts page header's **Enroll host** button opens a side sheet in the Deploy server / Add mods language. It
mints the single-use token, shows it with copy and expiry plus the Agent `.env` lines to paste on the host, and
says when the new host connects. `/enrollment` goes away (it redirects to `/hosts?enroll=1`).

## Facts found (2026-10-04)

- `/hosts` is static (`Agent.View`); its "Enroll host" is a plain link to `/enrollment`, shown to every viewer
  (a non-owner follows it into an access-denied page).
- `/enrollment` and `/setup/enroll` duplicate the same markup: a success alert with the token, expiry, a label form
  and a table of past enrollments. Only `/enrollment` has the copy button (shell.js, delegated on `document`, so it
  works inside a portalled sheet too). Neither shows what to do with the token.
- The Agent takes the token as `ZWARDEN_ENROLLMENT_SECRET` in the compose `.env` (`deploy/compose/.env` for the
  co-located Agent, `deploy/compose/remote-agent/.env` plus `ZWARDEN_DOMAIN` for a remote one), then
  `docker compose up -d`.
- `EnrollmentSummary.ConsumedByAgent` names the Agent that used a token; `IAgentConnectionRegistry.IsConnected` says
  whether it is live on this process.
- `StepSheet` hides the step strip for one step and lets the owner swap the finish label.

## Decisions

- **D1 — island.** `Components/Hosts/EnrollHostSheet.razor`: the header button, a one-step `StepSheet`, its own
  `BbPortalHost`. The Hosts page renders it `InteractiveServer` only when the caller holds
  `Tenant.Enrollment.Manage` (checked with `IPermissionChecker`); otherwise there's no button. `IEnrollmentService`
  re-checks on every call. Service calls go through `ActionScopeRunner`; the list loads when the sheet opens.
  `?enroll=1` sets `OpenOnLoad`.
- **D2 — one step, two states.** Before a token: an optional Label and the finish button **Generate token**. After:
  the token panel, and the finish button becomes **Done** (closes the sheet and refreshes the page so a connected
  host's card shows). Below either state, a compact "Recent tokens" list (label, status, expiry) — the old page's
  table.
- **D3 — shared token panel.** `Components/Hosts/EnrollmentTokenPanel.razor` (no interactivity, so it works on the
  static setup page too): the "shown only once" alert with the token + copy, expiry, and the `.env` lines
  (`ZWARDEN_DOMAIN=<this site's host>` and `ZWARDEN_ENROLLMENT_SECRET=<token>`) with their own copy button and a
  `docker compose up -d` line. The domain comes from the request's base URI (`EnrollmentSnippet`, a pure helper:
  host, plus the port when it isn't the default). `/setup/enroll` stays a page and uses the panel.
- **D4 — "connected ✓" (yes).** While a fresh token is shown, the sheet checks every 3 s (a `PeriodicTimer` on the
  injected `TimeProvider`, stopped on close, Done and dispose): the token's enrollment is consumed and its Agent is
  connected → "*host* connected ✓" (`HostNames.Display` from `IAgentInventory`; the Owner holds `Agent.View`).
  Before that it says "Waiting for the Agent to connect…". Cheap: one tenant-scoped list per tick, only while open.
- **D5 — old URL.** The `/enrollment` Razor page is deleted; a minimal endpoint redirects `/enrollment` to
  `/hosts?enroll=1` (the target does the auth). The Settings "Host enrollment" link points at `/hosts?enroll=1`.

## Tests

- bUnit (`InteractivePageHarness`): the button opens the sheet; `OpenOnLoad`; Generate shows the token panel with
  copy, expiry and the `.env` lines, and the token row in Recent tokens; the label is saved; Done closes; a consumed
  token whose Agent is connected shows "connected ✓".
- `EnrollmentSnippet` unit tests (default port dropped, custom port kept).
- Page (HTTP): an Owner gets the island on `/hosts`; an Administrator (Agent.View, no manage) gets no button;
  `/enrollment` redirects to `/hosts?enroll=1`; Settings links there; the setup page still issues a token and now
  shows the `.env` lines.
- Browser smoke via Aspire: open, generate, copy, close; screenshot.

## Not done here

- Revoking a pending token from the sheet (the API exists; not asked for).
