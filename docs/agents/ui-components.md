# UI components: BlazorBlueprint is the default

All ZWarden.Web UI is built from **BlazorBlueprint** components (`Bb*`) through the wrapper seam
([ADR 0003](../adr/0003-blazor-blueprint-ui-library.md)), over the "Signal" design tokens
(`docs/style-guide/`). **Reach for a Blueprint component before writing a raw HTML control.** Raw
`<button>`, `<input>`, `<select>`, `<textarea>`, card/panel `<div>`s, and status-message `<p>`s on a
ZWarden page are a smell — replace them with `BbButton`, `BbInput`, `BbNativeSelect`, `BbTextarea`,
`BbCheckbox`, `BbCard`, `BbAlert`, etc., so the app keeps one cohesive look and feel and reskins from
the token file alone.

## Versions: BlazorBlueprint 4.1 on Tailwind v4

We're on **BlazorBlueprint 4.1.0** (Components + Primitives, pinned exactly in `Directory.Packages.props`)
and ZWarden's own CSS build is **Tailwind v4** (`@tailwindcss/cli`, pinned in `src/ZWarden.Web/package.json`)
since #295. The MCP and llms.txt docs describe v4, so they match our code.

How the two stylesheets fit together (get this wrong and every Bb component silently loses its styling):

- `blazorblueprint.css` is the library's own prebuilt Tailwind v4 output. All of its utilities are
  **prefixed `bb:`** and live in their own `bb-utilities` cascade layer. Its reset lives in the `base` layer.
- `wwwroot/app.css` (built from `Styles/app.tailwind.css`) writes into the **same named layers**
  (`theme`/`base`/`components`/`utilities`). So our reset sits underneath the library's utilities and
  can't override them. **Never go back to an unlayered reset** (Tailwind v3's preflight). Its
  `*{border-width:0}` and `button{background:transparent;padding:0}` beat every layered `bb:` utility, which
  strips the borders, fills and padding from buttons, inputs and cards. On 3.16 this was hidden only
  because the library's unprefixed class names happened to match utilities our own build emitted.
- The Signal tokens (`zwarden.css`) and the shell chrome (`shell.css`) are **unlayered** on purpose, so they
  beat the library's defaults. A `zw-*` class can restyle a Bb component (e.g. `.zw-cfg-sections-btn`).
- A `Class="p-6"` on a Bb component still replaces the component's own `bb:p-4`: the library's `cn` merge
  strips the prefix to resolve conflicts. You write plain utilities; never write `bb:` yourself.
- Unset borders default to `var(--border)` (a `base`-layer rule in `app.tailwind.css`), not v3's gray-200.
  `dark:` follows the `.dark` class on `<html>` (`@custom-variant dark`), not the OS preference.
- Tailwind v4 scans only `Components/**/*.{razor,cs,html}` (`source(none)` + `@source`), like the old v3
  content glob.

## Always check the current API first

Component APIs evolve — **do not** work from memory. Before using or changing a component, pull its
current documentation:

- **MCP** (`blazorblueprint` server): `get_component` / `list_components` / `get_primitive` /
  `search_components` / `get_setup` / `get_changelog`. This is the primary, structured source.
- **llms.txt index**: <https://blazorblueprintui.com/llms/index.txt> (component files live at
  `components/<name>.txt`, primitives at `primitives/<name>.txt`). Use it to discover what exists.

Record any load-bearing component facts (SSR-safety, attribute splatting, required sub-components) in
the relevant mini-plan or the auto-memory so the next author doesn't re-derive them.

## When raw HTML is allowed (the only exceptions)

1. **Security.** Secret-display surfaces (e.g. authenticator setup / recovery-code panels) and
   auth-critical pages are rendered **markup-only** — never route a credential, secret, or the
   cookie-sign path through a component whose interactivity could change the trust behaviour, and
   never let a component treat Agent- or user-authored text as anything but data (escape at render,
   `trust-boundaries.md` §3/§8). If in doubt, keep it as reviewed static markup and say why.
2. **No suitable component exists**, or the only fit is **circuit-only and you're on a static page**
   (see below). Then use the closest SSR-safe primitive or plain semantic markup, and leave a one-line
   comment naming the deferral (e.g. "plain table until `BbDataGrid` graduates with F16/F36"). On an
   interactive page the circuit-only half of this exception doesn't apply: every component fits there.
3. **Layout/typography** — headings, paragraphs of prose, and grid/flex wrappers stay plain HTML;
   Blueprint is for *controls and surfaces*, not for wrapping every `<div>`.

Any raw control that isn't one of these should become a Blueprint component.

## Which kind of page are you on?

Since [ADR 0046](../adr/0046-pages-may-opt-into-interactive-rendering-the-shell-stays-static.md) a page is
one of two kinds, and the component rules differ:

- An **interactive page** declares `@rendermode InteractiveServer` on the page itself and runs in a
  Blazor Server circuit. In v1.0 that's **Server Detail and Settings** (#299). Making another page interactive
  needs an ADR 0046 amendment.
- A **static page** has no `@rendermode` and renders once per HTTP request: Account, Setup, Enrollment,
  Fleet, Audit and Hosts. The shell (`MainLayout`) is always static chrome (ADR 0040).
- An **island** is one interactive component (`@rendermode` on the component) inside a static page, e.g. the
  Fleet board and the audit table. On an interactive page, components such as the `Live*` panels are plain
  children with no `@rendermode` of their own.

### On an interactive page

Every Blueprint component is allowed, including the circuit-only ones (`BbCheckbox`, `BbSelect`/Combobox,
DatePicker, `BbDialog`/`BbAlertDialog`, `BbSheet`, `BbTabs`, `BbDataGrid`, dismissible alerts, toasts).
Use them and don't write static workarounds. The rules:

- **Tenant and data only through the circuit-safe infrastructure (#297).** The tenant is captured from
  the user's claims when the circuit starts and **fails closed**: never read `HttpContext` from code
  that runs in the circuit, and never fall back to `Tenant.DefaultId`. **Call every
  service through `ActionScopeRunner`**, never by injecting it into the component: the runner opens a fresh
  DI scope (with the circuit's tenant) per action, so the action gets its own `DbContext`, including the one
  behind `UserManager`. A service injected straight into a component lives in the circuit's scope and shares
  one context across every event and render (the #154 bug class).
  `await Actions.RunAsync<IServerLifecycle, ServerLifecycleResult>((s, ct) => s.StartAsync(user, id, ct), ct);`
  Return plain results, never a tracked entity or an `IQueryable`, since they die with the scope. Components
  never reference `ZWardenDbContext` or its factory (arch-tested). Singleton caches (the `Live*` panels') can be
  injected directly.
- **Permissions are checked in the service, every time.** Hiding or disabling a control is UX, not a
  guard. Every mutating action re-checks its permission in its Application service. Auth state also
  revalidates about every minute.
- **Overlays need a portal host on the page.** `BbPortalHost` (and the dialog/toast providers) go inside the
  interactive page's own markup, never in `MainLayout`, which is static and would leave them inert.
- **`[PersistentState]` for two things only:**
  - **The prerender's first load**, so the circuit doesn't load it again: one plain record from a page query
    (`ServerDetailQuery`, `SettingsQuery`), plus anything only the request knows, such as the operator's time zone
    from its cookie.
  - **Drafts worth keeping**, starting with unsaved config-editor edits (#299 D1). A draft lives in server memory,
    so it survives a dropped connection or an evicted circuit, not a process restart. The Config section persists
    only a `ConfigDraftSnapshot` (the changed settings), only from a live circuit, and re-reads the file on restore.
    While the circuit lives, `ConfigDraftStore` (scoped) keeps unsaved edits across rail-section and file-tab switches.
    `UnsavedConfigGuard` (#331) warns before a reload or a tab close (the browser's own prompt, the only one allowed)
    or a link to another page (a `BbAlertDialog`) would lose them.

  Everything else reloads from the database when a circuit is rebuilt.
- **`NavigationLock` does not see link clicks on these pages.** The router is static, so enhanced navigation
  handles a link before the circuit hears of it, and `OnBeforeInternalNavigation` never runs. A "leave this page?"
  guard has to check clicks in JS, in the capture phase, and hand the held link to .NET to ask: see
  `wwwroot/js/unsaved-guard.js`.
- **A link to another URL of the same interactive page switches in the circuit (#312).** Otherwise it is an enhanced
  navigation, and the server prerenders the whole page (its query, every permission check, the section's load, even a
  live Agent read) only to throw it away while the circuit loads it again. `NavigationManager.NavigateTo` is no way
  out: without an interactive router it is an enhanced navigation too. Server Detail does it with
  `wwwroot/js/in-place-nav.js`: a plain click on a link to the page, and back/forward between its entries,
  `pushState` the URL and hand it to the page's `[JSInvokable] NavigatedInPlace`. The page keeps its own URL in a
  `ServerDetailLocation` (the circuit's `NavigationManager` never sees a `pushState`) and still follows a real
  navigation through `LocationChanged`. Sections move with `GoToAsync` (the cascaded `ServerDetailNavigator`), not
  `NavigateTo`. The script loads before `blazor.web.js`: listeners on `window` run in the order they were added, so
  it has to come first to stop Blazor's `popstate` handler.
- **Keep persisted state small: well under 32 KB.** A prerender's state rides in the page, and every enhanced
  navigation (any link the circuit doesn't switch in place) posts it to the circuit as one hub message. Past the Blazor hub's 32 KB
  `MaximumReceiveMessageSize`, the hub drops the connection: the reconnect overlay flashes. That was #322: the
  whole config editor was persisted. Don't raise the limit. Persist ids or deltas, and let the circuit load the rest.
  `HubCloseReasonLogging` logs such a close as a Warning.
- **No `AuthorizeView` policies on an interactive page.** They evaluate in the circuit's own scope, and several
  can run at once against one `DbContext`. Load the gating flags through the page query instead. The service
  re-checks every action regardless.
- **The reconnect dialog is `Layout/ReconnectModal.razor`**, rendered by `App` with `wwwroot/js/reconnect.js`, and
  Signal-styled in `Styles/reconnect.css`. Pages don't add their own.
- **One Playwright smoke test per section.** It loads the section in a real browser and fails on any
  console error. Circuit-only failures (like the `BbDataGrid` `List<T>` crash below) don't show up in bUnit or
  the real-host page tests. The tests live in `tests/ZWarden.Web.BrowserTests`, where `BrowserHost` serves the real
  `Program` on Kestrel with a signed-in owner and no Agent, and `BrowserSession` records console and page errors. CI
  runs them in the `tier3-e2e-browser` job. To run them locally, build once, then run
  `pwsh tests/ZWarden.Web.BrowserTests/bin/Debug/net10.0/playwright.ps1 install chromium` and the built test exe.
- **No new static-only JS.** Since #299 `dialog.js` and `config-editor.js` are gone and `live-status.js` serves
  only Fleet. Browser-only conveniences on an interactive page are small ES modules imported through
  `IJSRuntime` (`dismissed-failures.js`, `local-prefs.js`), guarded so the page works without storage. The one
  exception is `in-place-nav.js`, a classic script because it must load before `blazor.web.js` (above).
- **Only the routed page reads the query string.** A child that the page renders when the query changes (a rail
  section) must take `?file=`-style values as `[Parameter]`s from the page, not `[SupplyParameterFromQuery]`: it
  would subscribe during the location-changed dispatch, which throws "Collection was modified" in the circuit
  (caught by the rail-navigation browser test). A page that switches in place reads its own `ServerDetailLocation`
  instead of `[SupplyParameterFromQuery]`, which would go stale after a `pushState`.
- **A `BbInput` that feeds an action uses `UpdateTiming="UpdateTiming.Immediate"`.** The default reports the value
  on blur, and a click can reach the circuit first, so the action would see the old value. Browser tests type
  through `BrowserSession.FillAsync` and wait for the network to settle after load, because a `BbInput` attaches its
  listener only once its JS module has loaded.
- An interactive grid's row parameter must be a real `List<T>`, not an array behind `IReadOnlyList<T>`, or
  the circuit throws "component operations is not valid".

### On a static page

There's no circuit, so only a subset of Blueprint works:

- **SSR-safe:** `BbLabel`, `BbInput`, `BbNativeSelect`, `BbTextarea`, `BbButton`, `BbCard`
  (+ `BbCardHeader/Title/Content/Footer`), `BbAlert` (+ `BbAlertDescription`) **when not
  `Dismissible`/`AutoDismissAfter`**.
- **Circuit-only (never on a static page):** `BbCheckbox`, `BbSelect`/Combobox, DatePicker, `BbDataGrid`,
  `BbDialog`/`BbAlertDialog`/`BbSheet`/`BbTabs`, and any dismissible/auto-dismiss `BbAlert` or toast. These
  need JS/interactivity. Use them inside an island, or on an interactive page, or defer them.
- **`BbCheckbox` renders but cannot be ticked on a static page.** It is a `<button role="checkbox">`
  plus a hidden mirror input, and the button only toggles with a circuit. A test that POSTs the field
  still passes, so the bug only shows in a real browser. On a static form use a native
  `<input type="checkbox" class="zw-checkbox" name="<full model path>" value="true" checked="@x" />`
  (styled like BbCheckbox in `Styles/shell.css`) and assert it with `SsrCheckbox.IsNative` in Web.Tests.

## Gotchas (static pages only)

These apply to static-SSR forms. On an interactive page, `@bind` and the component's own state do the work,
so none of them is needed.

- **`BbNativeSelect` needs an explicit `Name`** = the full model path (e.g. `_form.Target`); it
  auto-derives only the leaf name and silently fails to bind on a static POST. `BbInput` auto-derives
  the full path correctly.
- **`BbNativeSelect` doesn't mark the bound option selected under static SSR.** Blazor sets a select's
  value only through JS on an interactive page, so a static re-render (e.g. after a validation error or
  an overcommit warning) shows the **first** option, and the next submit silently sends it. Mark it on
  each option yourself: `<option value="@v" selected="@(v == _form.Field)">`, with the default also
  selected when the field is empty. Assert with `SsrSelect.SelectedValue` in page tests. (#258 live
  pass: the wizard's "Create it anyway" resubmit reverted a pinned branch to public.)
- **`BbButton` splats `name`/`value`/`data-*`** through `AdditionalAttributes` (use the dedicated
  `Disabled` bool, not a splat), which lets several submit buttons share one form and post
  `"{id}|{verb}"` as a single bound value (the `/servers` lifecycle actions).

## Gotchas (every page)

- **bUnit needs `ctx.JSInterop.Mode = JSRuntimeMode.Loose;`** for any test that renders Blueprint
  controls (they call JSInterop in `OnAfterRender`, which real static SSR never runs).
- **Blueprint's component CSS ships in `blazorblueprint.css`**, separate from ZWarden's Tailwind
  content scan — so a new `Bb*` component needs no `app.tailwind.css` change, but still run
  `npm run build:css` (removing raw utility classes shrinks the committed `wwwroot/app.css`, which CI
  diffs — ADR 0003 condition 2) and bump the `Web.Tests` `--minimum-expected-tests` floor when the
  test count changes.

## Confirming a destructive action (#271)

Confirmations run on an interactive page, as a `BbAlertDialog` rendered into the page's own `BbPortalHost`
(Server Detail's Delete, #299). The static `dialog.js` pattern is gone. A static page that needs a confirmation
first needs an ADR 0046 amendment to go interactive.

- Opener: a `BbButton` whose `OnClick` resets the confirmation and sets the dialog's `@bind-Open` flag.
- Content: `BbAlertDialogContent` (put `data-*` hooks there; the root renders no element). Say what goes, what is
  kept, and that it can't be undone.
- Typed confirmation (GitHub-style): a `BbInput` with `UpdateTiming="UpdateTiming.Immediate"`, so the confirm button
  enables on the keystroke that completes the name. The confirm button is a plain `BbButton` with
  `Disabled="@(!Confirmed)"` and an `OnClick` handler. Don't wrap it in `BbAlertDialogAction`: that closes the
  dialog before the async handler runs.
- Cancel: `BbAlertDialogCancel` wrapping a `BbButton`.
- **The server is the guard.** The typed value is passed through, and the service re-checks it (ordinal, exact).
  `Delete server` returns `ServerLifecycleFailure.ConfirmationMismatch` and audits the refusal.
- bUnit renders the dialog through the page's portal host, so a test can open it, type, and assert the button's
  `disabled` attribute (`OverviewSectionTests`).
