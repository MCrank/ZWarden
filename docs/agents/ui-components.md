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
  Blazor Server circuit. In v1.0 that's **Server Detail and Settings** (once #299 lands). Making another
  page interactive needs an ADR 0046 amendment.
- A **static page** has no `@rendermode` and renders once per HTTP request: Account, Setup, Enrollment,
  Fleet, Audit and Hosts. The shell (`MainLayout`) is always static chrome (ADR 0040).
- An **island** is one interactive component (`@rendermode` on the component, e.g. the `Live*` panels)
  inside a static page. Islands are how Server Detail works until #299. Don't add new ones to a page that
  is going interactive.

### On an interactive page

Every Blueprint component is allowed, including the circuit-only ones (`BbCheckbox`, `BbSelect`/Combobox,
DatePicker, `BbDialog`/`BbAlertDialog`, `BbSheet`, `BbTabs`, `BbDataGrid`, dismissible alerts, toasts).
Use them and don't write static workarounds. The rules:

- **Tenant and data only through the circuit-safe infrastructure (#297).** The tenant is captured from
  the user's claims when the circuit starts and **fails closed**: never read `HttpContext` from code
  that runs in the circuit, and never fall back to `Tenant.DefaultId`. Data access goes through
  `IDbContextFactory<ZWardenDbContext>`, with one context per operation, created and disposed inside the
  service call. A component never holds a `DbContext` across awaits or events (the #154 bug class).
- **Permissions are checked in the service, every time.** Hiding or disabling a control is UX, not a
  guard. Every mutating action re-checks its permission in its Application service. Auth state also
  revalidates about every minute.
- **Overlays need a portal host on the page.** `BbPortalHost` (and the dialog/toast providers) go inside the
  interactive page's own markup, never in `MainLayout`, which is static and would leave them inert.
- **`[PersistentState]` only for drafts worth keeping**, starting with unsaved config-editor edits. Everything
  else reloads from the database when a circuit is rebuilt after a reconnect or a deploy.
- **One Playwright smoke test per section.** It loads the section in a real browser and fails on any
  console error. Circuit-only failures (like the `BbDataGrid` `List<T>` crash below) don't show up in bUnit or
  the real-host page tests.
- **No new static-only JS.** `dialog.js`, `config-editor.js` and Server Detail's use of `live-status.js` are
  replaced by components in each section's PR (ADR 0046). `live-status.js` stays for Fleet.
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

## Confirming a destructive action on a static page (#271)

On an interactive page, use `BbAlertDialog` (with the portal host) instead. The "server is the guard" rule below
still applies there too.

Static pages have no circuit, so `BbAlertDialog`/`BbDialog` are out. The pattern for an "are you sure?"
confirmation is a **native `<dialog>` wrapping an ordinary static `EditForm`**, driven by
`wwwroot/js/dialog.js` (delegated on `document`, so it survives enhanced navigation):

- Opener: a `BbButton Type="ButtonType.Button"` with `data-zw-dialog-open="<name>"`.
- Dialog: `<dialog data-zw-dialog="<name>" aria-labelledby="…">`. **Give it `m-auto`**: Tailwind's preflight zeroes
  every margin, including the UA `margin: auto` that centres a modal dialog, so without it the dialog pins top-left.
  Style it with Tailwind (`backdrop:bg-black/60`
  for the scrim). Say what goes, what is kept, and that it can't be undone.
- Typed confirmation (GitHub-style): a `BbInput` with `data-zw-confirm-expected="@exact text"`, plus a submit
  `BbButton` with `data-zw-confirm-submit` and `Disabled="true"`. The script enables it only on an exact match.
  `BbButton` renders `Disabled` as `aria-disabled` + `tabindex=-1` with no native `disabled`, so the script
  keeps all three in step. In tests, assert `aria-disabled="true"`, not the word "disabled" (the class list
  contains `disabled:opacity-50`).
- Cancel: a `BbButton Type="ButtonType.Button"` with `data-zw-dialog-close`. Esc closes the dialog natively.
  Reopening clears the input.
- **The server is the guard.** The typed value is posted, and the service re-checks it (ordinal, exact).
  `Delete server` returns `ServerLifecycleFailure.ConfirmationMismatch` and audits the refusal. Without JS the
  dialog never opens, so nothing can be submitted (fail closed).
- Verify the JS in a real browser (playwright via `run-web`). Page tests only see the markup.
