# UI components: BlazorBlueprint is the default

All ZWarden.Web UI is built from **BlazorBlueprint** components (`Bb*`) through the wrapper seam
([ADR 0003](../adr/0003-blazor-blueprint-ui-library.md)), over the "Signal" design tokens
(`docs/style-guide/`). **Reach for a Blueprint component before writing a raw HTML control.** Raw
`<button>`, `<input>`, `<select>`, `<textarea>`, card/panel `<div>`s, and status-message `<p>`s on a
ZWarden page are a smell — replace them with `BbButton`, `BbInput`, `BbNativeSelect`, `BbTextarea`,
`BbCheckbox`, `BbCard`, `BbAlert`, etc., so the app keeps one cohesive look and feel and reskins from
the token file alone.

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
2. **No suitable component exists**, or the only fit is **circuit-only on a static page** (see
   below). Then use the closest SSR-safe primitive or plain semantic markup, and leave a one-line
   comment naming the deferral (e.g. "plain table until `BbDataGrid` graduates with F16/F36").
3. **Layout/typography** — headings, paragraphs of prose, and grid/flex wrappers stay plain HTML;
   Blueprint is for *controls and surfaces*, not for wrapping every `<div>`.

Any raw control that isn't one of these should become a Blueprint component.

## Static SSR vs. interactive circuit

Most ZWarden pages render on the **static server** (no `@rendermode`) because a tenant-scoped read
needs a live `HttpContext` (see `Pages/Audit/AuditLog.razor`, `Pages/Servers/ServerInventory.razor`).
Only a subset of Blueprint is safe there:

- **SSR-safe (use on static pages):** `BbLabel`, `BbInput`, `BbNativeSelect`, `BbTextarea`,
  `BbButton`, `BbCard` (+ `BbCardHeader/Title/Content/Footer`), `BbAlert`
  (+ `BbAlertDescription`) **when not `Dismissible`/`AutoDismissAfter`**.
- **Circuit-only (never on a static page):** `BbCheckbox`, `BbSelect`/Combobox, DatePicker, `BbDataGrid`,
  and any dismissible/auto-dismiss `BbAlert` or toast — these need JS/interactivity. Put them only on a
  page that opts into an interactive render mode, or defer them.
- **`BbCheckbox` renders but cannot be ticked on a static page.** It is a `<button role="checkbox">`
  plus a hidden mirror input, and the button only toggles with a circuit. A test that POSTs the field
  still passes, so the bug only shows in a real browser. On a static form use a native
  `<input type="checkbox" class="zw-checkbox" name="<full model path>" value="true" checked="@x" />`
  (styled like BbCheckbox in `Styles/shell.css`) and assert it with `SsrCheckbox.IsNative` in Web.Tests.

## Gotchas (load-bearing for static-SSR forms)

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
- **bUnit needs `ctx.JSInterop.Mode = JSRuntimeMode.Loose;`** for any test that renders Blueprint
  controls (they call JSInterop in `OnAfterRender`, which real static SSR never runs).
- **Blueprint's component CSS ships in `blazorblueprint.css`**, separate from ZWarden's Tailwind
  content scan — so a new `Bb*` component needs no `tailwind.config.js` change, but still run
  `npm run build:css` (removing raw utility classes shrinks the committed `wwwroot/app.css`, which CI
  diffs — ADR 0003 condition 2) and bump the `Web.Tests` `--minimum-expected-tests` floor when the
  test count changes.
