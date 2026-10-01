# 46. Pages may opt into interactive rendering; the shell stays static

**A feature page may declare `@rendermode InteractiveServer` for the whole page. The app shell
(`MainLayout`) stays static-SSR chrome, so ADR 0040 still holds for it. In v1.0 only Server Detail and
Settings opt in. Account, Setup, Enrollment, Fleet, Audit and Hosts stay static.** Tenant-scoped work in
a circuit fails closed. The tenant is captured from the user's claims when the circuit starts, never
defaulted. Each unit of work gets its own `DbContext` from `IDbContextFactory`. Open circuits revalidate
authentication about once a minute. This ADR **amends** ADR 0040; it does not supersede it.

- Status: accepted
- Decided in: [#294](https://github.com/MCrank/ZWarden/issues/294) (design grilling with the maintainer,
  2026-10-01, decisions Q1–Q13); recorded in [#296](https://github.com/MCrank/ZWarden/issues/296)
- Bears on: [ADR 0040](./0040-the-app-shell-is-static-ssr-chrome.md) (amends its "interactive only in
  `Live*` islands" posture), [ADR 0003](./0003-blazor-blueprint-ui-library.md) (the "no SSR-safe fit"
  condition), [ADR 0016](./0016-tenant-isolation-query-filter-and-default-tenant.md) (the ambient tenant
  context), [ADR 0018](./0018-zwarden-owned-rbac.md) (server-side permission checks), #154 (authorization in
  a circuit), F10A (multi-instance live data), PRD 12

## Context

ADR 0040 made the shell static and left interactivity to opt-in `Live*` islands inside otherwise static
pages. Its scale reasoning still holds: a circuit for every authenticated page, just to draw chrome, is the
expensive shape for a hosted SaaS.

But "static page with islands" has started to cost more than it saves on the two pages operators actually
work in:

- `BbNativeSelect` doesn't mark the bound option selected under static SSR, so every select needs a
  hand-written `selected` per option (#258).
- `BbCheckbox` can't be ticked on a static page, so native checkboxes stand in for it.
- There's no `BbSheet`, `BbDialog`, `BbAlertDialog` or `BbTabs`. Confirmations are a native `<dialog>`
  driven by `dialog.js` (#271).
- An island can't load its own tenant-scoped data, because the tenant lives on `HttpContext`. The static
  parent loads everything and passes ids and lists down.
- Behaviour leaks into static-only JavaScript (`dialog.js`, `config-editor.js`, `live-status.js`) and a
  form-limit middleware.
- `ServerDetail.razor` is 3,300+ lines that switch sections with `?section=` round-trips.

The Mods redesign (#289 / #292) needs real interactive components, and building it on these workarounds
would add more of them.

Two latent hazards stand in the way of putting tenant-scoped work in a circuit:

1. **The tenant silently defaults.** `ClaimsPrincipalTenantContext` reads the tenant claim from
   `IHttpContextAccessor`. A circuit has no `HttpContext` after its first render, so the read returns
   `Tenant.DefaultId`. In single-tenant v1.0 that happens to be correct. In a hosted deployment it's a
   cross-tenant leak waiting to happen.
2. **A scoped `DbContext` lives as long as the circuit.** A circuit's DI scope lasts as long as the tab, so
   one `DbContext` serves every event handler, timer and render on the page, possibly at the same time.
   That is the bug class behind #154. Fixing only the authorization path left the rest of the class open.

## Decision

**Render posture.**

- A page opts in with `@rendermode InteractiveServer` at the page level. There is no global interactive
  mode, and `MainLayout`/`Routes`/`HeadOutlet` stay static (ADR 0040's render-mode boundary facts are
  unchanged). (Q5)
- In v1.0 the interactive pages are **Server Detail and Settings**. **Account, Setup and Enrollment** stay
  static because the cookie sign-in/out flow needs a real HTTP response (#63). **Fleet, Audit and Hosts**
  stay static because they are read-only and shouldn't hold a circuit. Making another page interactive
  needs an amendment to this ADR. (Q9)
- The overlay providers (`BbPortalHost` and friends) go inside the interactive page, not the shell. Shell
  overlays still follow ADR 0040's last consequence.

**Circuit-safe infrastructure** (#297 lands it before any page opts in).

- **The tenant fails closed.** It is captured from the authenticated user's claims when the circuit starts
  and held for the circuit's lifetime. If there is no tenant, there is no fallback to `Tenant.DefaultId`:
  tenant-scoped access throws. This applies on every path (HTTP and circuit), and an architecture test
  enforces it. (Q6)
- **One `DbContext` per unit of work.** Services take `IDbContextFactory<ZWardenDbContext>` and create a
  context for each operation. Components never hold a context across awaits or events. (Q7)
- **Authorization is re-checked where it matters.** Every mutating action re-checks its permission inside
  its Application service, as it does today. A UI that hides a button is never the guard (PRD 12, ADR 0018).
  Open circuits also revalidate authentication about **every minute** (a
  `RevalidatingServerAuthenticationStateProvider`), so a disabled user or revoked session loses the page
  within a minute, not when the tab closes. (Q3)

**Hosting and deploys.**

- The hosted SaaS is planned on **Azure SignalR Service**. It handles sticky routing between a browser and
  the instance that owns its circuit. Self-hosted runs one instance and needs nothing extra. (Q1)
- Rolling deploys drain open circuits with Azure SignalR's **graceful shutdown** (`WaitForClientsClose`).
  Only state marked **`[PersistentState]`** survives a circuit moving to another pod, stored through
  `HybridCache` with a Redis backend. Adopt .NET 11's `Circuit.RequestCircuitPauseAsync` when it ships. (Q2)
- `[PersistentState]` is used **only for drafts worth keeping**, starting with unsaved config-editor edits.
  Everything else is reloaded from the database when a circuit is rebuilt.
- **Interactive pages don't make ZWarden scale out.** Running more than one web instance stays out of
  scope and on the F10A track. The in-process live caches (health, roster, mods, diagnostics) and the
  log-subscription coordinator are per-instance singletons that need a backplane first. (Q8)

**Migration and verification.**

- Server Detail is **split into per-section components first** with no behaviour change (#298). Then the
  page goes interactive (#299). Then each section is upgraded to real Bb components, starting with Mods
  via #292. (Q10)
- `dialog.js`, `config-editor.js` and the Server Detail use of `live-status.js` are replaced **inside each
  section's PR**. `live-status.js` stays for Fleet only. (Q12)
- **One Playwright smoke test per interactive section** runs in CI and fails on any browser-console error.
  Circuit-only failures are invisible to bUnit and to the real-host page tests (the `BbDataGrid`
  `List<T>` crash was one). (Q4)
- The live panels keep their one-second polling for now. Push updates are later work.
- The Blazor reconnect overlay gets Signal styling.
- All of this lands in v1.0, in the epic's sub-issue order, before #292. (Q11)

## Alternatives considered

- **Keep static-first and keep adding islands.** This needs no new infrastructure, but every new feature
  surface pays the workaround list in Context again, and an island still can't load its own tenant data.
  Rejected because the cost grows with each feature, and Mods would be the most expensive yet.
- **Global interactivity** (`@rendermode` on `Routes`/`HeadOutlet`). Rejected for the same reasons ADR 0040
  gave: it breaks the static auth flow (#63) and puts a circuit on every read-only page.
- **Interactive pages with `InteractiveAuto` or WebAssembly.** This would move work to the browser, but
  it would also put the tenant and permission logic on the client and need an API for every read. Rejected:
  ZWarden's trust model is server-side (PRD 12, `trust-boundaries.md`).
- **Fix the tenant fallback only in the hosted build (F3B).** That's what the current remarks on
  `ClaimsPrincipalTenantContext` say. Rejected: once circuits do tenant-scoped work, the self-hosted build
  is where the fallback would first hide a bug. Fail closed everywhere and test it.
- **Keep a scoped `DbContext` and serialize access in the component.** Rejected: it fixes one page at a
  time and leaves the #154 class open for the next author.

## Consequences

- Server Detail and Settings each hold a circuit while open: server RAM per tab, a WebSocket, and sticky
  routing in SaaS. That is the cost ADR 0040 avoided for the shell, now paid knowingly for the two pages
  that need it. Read-only pages still cost nothing.
- Rolling deploys in SaaS depend on Azure SignalR graceful shutdown. An operator whose circuit is dropped
  mid-edit gets a reconnect and a reloaded page, and keeps only `[PersistentState]` drafts.
- The tenant fallback goes away. Code that relied on it, including tests that never set a principal, has
  to supply a tenant. That's the point, but it touches a lot of tests (#297).
- `IDbContextFactory` changes how services are written. That's a broad mechanical change, and a lasting
  rule for new code.
- The static-page gotchas in `docs/agents/ui-components.md` (`BbNativeSelect` `selected`, native checkbox,
  explicit `Name`, `dialog.js`) still apply to the static pages and to Server Detail until #299 lands.
- CI grows a browser tier. Playwright smoke tests are slower and flakier than bUnit, and they're the only
  thing that catches circuit-only failures.
- Interactive pages add no scale-out. Until F10A provides a backplane, ZWarden runs one web instance.

## Sources

- [Blazor server-side state management (.NET 10)](https://learn.microsoft.com/en-us/aspnet/core/blazor/state-management/server?view=aspnetcore-10.0)
- [Azure SignalR Service graceful shutdown](https://learn.microsoft.com/en-us/azure/azure-signalr/server-graceful-shutdown)
- [What's new in ASP.NET Core in .NET 11](https://learn.microsoft.com/en-us/aspnet/core/release-notes/aspnetcore-11?view=aspnetcore-10.0)
- [Blazor render modes](https://learn.microsoft.com/en-us/aspnet/core/blazor/components/render-modes?view=aspnetcore-10.0)
