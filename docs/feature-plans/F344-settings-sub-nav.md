# Issue #344 Mini-Plan — Settings: left sub-nav with deep-linkable sections and Signal icons

**Status:** one PR (branch `feat/344-settings-sub-nav`, closes #344). First sub-issue of epic #350.

**Written against:** issue #344, epic #350 (decisions: left vertical sub-nav, the seven sections, editability
deferred to #345–#347), the Signal prototype's Settings view (setting cards with a primary-tinted icon header and
label/hint rows), [ADR 0046](../adr/0046-pages-may-opt-into-interactive-rendering-the-shell-stays-static.md)
(interactive pages, static router), `F312-in-place-section-switch.md` (in-place section switch).

## Objective

`/settings` is one long column of cards today. Make it a left sub-nav (the Server Detail rail's look) with one section
on screen at a time, each at its own URL, and switch sections in the circuit without a prerender. Move the existing
content into the issue's sections. Nothing becomes editable here except what already is (the Workshop key).

## Decisions

- **D1: URL shape `/settings/{section}`, as the issue asks.** `/settings` is General. The section ids are `general`,
  `security`, `enrollment`, `integrations`, `backups`, `users` and `about`. An unknown section, or one the operator
  can't see, falls back to General without failing (Server Detail does the same with `?section=`). The page keeps
  its own location (`SettingsLocation`, like `ServerDetailLocation`), because an in-place switch is a `pushState` the
  circuit never sees.
- **D2: reuse `in-place-nav.js`.** `attach` takes an optional `{ subpaths: true }`, so a page can claim its path
  *and* the paths below it (`/settings` and `/settings/*`). Server Detail keeps matching on the exact path. The
  page's `[JSInvokable] NavigatedInPlace` and its enhanced-nav fallback (`LocationChanged`) work as on Server Detail.
- **D3: layout.** Reuse `.zw-srvlayout` / `.zw-rail` / `.zw-railitem` (176px rail with the active item tinted
  primary), so it matches Server Detail. Each section is one card with a primary-tinted Lucide icon in its header,
  then label/hint rows (the prototype's `setrow`), via a small `SettingsCard` and `SettingRow` pair. Read-only values
  keep a "set in config" hint. New `ShellIcon` glyphs: `shield`, `key-round`, `plug`, `info`.
- **D4: gating.** The sub-nav shows the sections the operator can open and hides the rest, like the cards today:
  Host enrollment needs `Tenant.Enrollment.Manage`, Integrations (Workshop key) needs `Tenant.Manage`, and Users &
  roles needs `Role.Manage`. Enforcement stays in the services (PRD 12); the page-level `User.Manage` policy is
  unchanged.
- **D5: section contents.**
  - General: instance name, the default deploy host ("chosen per deploy" until #347), and time display (per user).
  - Security: TLS mode, forwarded-header trust, session timeout.
  - Host enrollment: the Enroll host button (opens the sheet on Hosts, `/hosts?enroll=1`) and the enrolled host
    count, "N enrolled · M online". The count is new: `SettingsQuery` reads `IAgentInventory` when the operator
    also holds `Agent.View`.
  - Integrations: the Steam Workshop key, Save/Clear unchanged.
  - Backups: per-host backup root, read-only.
  - Users & roles: "Coming in v1.1" placeholder (#171).
  - About: version (the Web assembly's informational version, as `DiagnosticsOptions` already reads it), the BSL 1.1
    licence, and links to the docs and repository.
- **D6: phone width.** Below 960px the rail becomes one horizontal scrolling row of chips (`.zw-rail-scroll`) above
  the content, not the wrapping strip Server Detail uses. Seven items wrap badly, and a scroller keeps the active one
  in view.

## Tests (TDD)

- Unit: `SettingsLocation` parsing (bare `/settings`, a section, trailing slash, another page).
- HTTP (`SettingsPageTests`): each section renders at its deep link; `/settings` is General; an unknown or hidden
  section falls back to General; the sub-nav lists only the visible sections (Owner vs Administrator); the existing
  value assertions move to their sections.
- bUnit: an in-place move switches the section without a navigation; `attach` is called for `/settings` with
  sub-paths; a URL that isn't Settings is refused; the Workshop save/clear still works on `/settings/integrations`.
- Browser: the Workshop smoke test opens `/settings/integrations`; a new test clicks through the sub-nav and asserts
  the right section with no document request for the page.

## Slices

1. `SettingsLocation` and the routes, so each section renders at its URL (HTTP tests).
2. Sub-nav, cards and rows, icons, CSS, section contents (host count, About).
3. In-place switch (`in-place-nav.js` sub-paths, `NavigatedInPlace`) with bUnit and browser tests.
4. Chores: `app.css`, test floors, docs (`ui-components.md`), this plan's result.

## Not in scope

- Making anything editable (#345 instance name and settings store, #346 session timeout, #347 default deploy host).
- Users & roles management (#171, v1.1).
