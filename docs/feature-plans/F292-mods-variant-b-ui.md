# Feature #292 Mini-Plan — Mods: Variant B UI (one table, Add-mods sheet, pending-changes bar)

**Status:** planned — D1–D4 decided 2026-10-03 (all recommendations taken); next slice 1. v1.0, epic [#289](https://github.com/MCrank/ZWarden/issues/289).
Needs #290 + #291 (both done). #293 (delete files) and #275 (update ready) build on this.

**Written against:** issue #292; epic #289 (Variant B, prototype `prototype/mods-ux` `VariantB.razor`, local only);
[F290](./F290-mod-installed-data.md) (`ServerModOverview` / `ModChangeSet`); [F291](./F291-one-click-install.md)
(`ModInstallControl`, Install / SetItemParts / Undo / Remove verbs, dependencies); [F110](./F110-workshop-metadata-enrichment-and-browse.md)
(preview + search); ADR 0003 / 0046 (Blueprint everywhere on interactive pages); ADR 0044 (search key optional);
#114 / #213 (graceful restart presets); trust-boundaries §8 (Steam + mod.info text is untrusted, rendered as data).

## Objective

The maintainer opens **Mods**, sees **one list** of their mods with a plain-English status each, adds mods from a
sheet, and applies everything with **one restart** from a sticky bar — without reading docs or ever seeing
`WorkshopItems=`/`Mods=`. Today's page is "badly broken": every item appears twice (the F21 inventory issues list
*and* the F22 manage lists), and leftovers have no actions. This replaces both F21/F22 Mods UI and the Mod Browser
section.

## Facts found (2026-10-03)

- **ServerDetail is already interactive** (#299) and already hosts `<BbPortalHost />` (`ServerDetail.razor:291`), so
  Sheet/Dialog work there today. The "add the portal host to MainLayout" bullet is only needed if a Sheet is used on
  another page — **not needed for #292**; I'll leave MainLayout alone and note it on the issue.
- **Everything the table needs is in `ServerModOverview`**: items in config order, then removals, then leftovers;
  per-item title / preview / size / Steam updated / tags / guessed + observed ids / `NeedsParts` / `MissingModIds`;
  mod ids in `Mods=` order with their own status; `PendingChanges`.
- **Load order is per mod id (`Mods=`), not per Workshop item.** An item can provide several ids, and an id may come
  from no tracked item (hand-edited / local mod). See D1.
- **Verbs exist for every row action:** `InstallWorkshopItemsAsync` (also Reinstall of a leftover),
  `SetItemPartsAsync`, `UndoPendingAsync`, `RemoveWorkshopItemsAsync`, `ReorderModsAsync`, `UpdateModsAsync`.
- **`UpdateModsAsync` takes no countdown** — it always uses the default player warning. The header's graceful
  restart has presets 15m/5m/1m/immediate (`ServerDetailFormat.CountdownLeads`). See D3.
- **No "Update ready" data yet** (#275) and **no delete-files verb yet** (#293).
- **No Build-41 detection exists.** Steam tags carry "Build 41"/"Build 42"; keyless preview returns tags, search
  (`QueryFiles`) currently does not request them.
- **F21 compat issues** (`ModCompatIssueKind`: ReferencedNotInstalled, EnabledButMissing, InstalledButInactive,
  DuplicateModId, RequiresMissing, IncompatiblePresent) are what `LiveModInventoryPanel` lists — the duplicate
  listing. See D2.
- Existing tests to rewrite: `ModsSectionTests`, `ServerDetailModBrowserTests`, `LiveModInventoryPanelTests`,
  rail tests, `ServerDetailSmokeTests` (browser).

## Decisions

- **D1 — what a row is / how load order moves.** **Decided:** **a row is a Workshop item**, ordered by the
  position of its first enabled mod id in `Mods=`; ▲/▼ moves the item's whole id block past its neighbour's block
  (one `ReorderModsAsync`). Ids no tracked item provides become their own "Other mod" rows. Multi-part items expand
  to show parts (checkbox = on/off, Save → `SetItemPartsAsync`). Per-part ordering inside an item is not offered.
- **D2 — F21 inventory panel, Refresh, Enable/Disable.** **Decided:** **delete `LiveModInventoryPanel` and
  the F22 manage lists from the page.** Issues fold into rows: EnabledButMissing/InstalledButInactive are already the
  Pick parts / Leftover states; RequiresMissing / IncompatiblePresent / DuplicateModId become a one-line warning under
  the row. Refresh becomes a small ghost "Re-scan" button in the header. Enable/Disable of a single id is the parts
  picker.
- **D3 — restart from the pending bar.** **Decided:** **add an optional countdown to `UpdateModsAsync`**
  (same presets as the header, default 5m) so the bar's select is real; it reuses the #114 graceful payload.
- **D4 — PR split.** **Decided:** **two PRs.** PR-A: table + row actions + pending bar + rail merge
  (the page stops being broken). PR-B (closes #292): Add-mods sheet, leftover footer, B41 blocking, copy pass.
- **Defaults (not contested):**
  - *Update ready* chip: not shown (no data until #275, which adds it).
  - Leftover *Delete / Delete all*: not rendered until #293 lands; Reinstall ships now.
  - *Build 41 only* = tags contain "Build 41" and not "Build 42"; search asks Steam for tags (`return_tags`). Unknown
    tags → allowed.
  - `?section=modbrowser` redirects to `?section=mods&add=1` (opens the sheet).
  - Status chip = a small `ModStatusChip` component in the StatusBadge idiom (dot + label, status tokens).
  - Table = `BbDataGrid` with `List<T>` rows (BbDataGrid gotcha), hidden "Updated" column below `md`, no horizontal
    scroll at 1280px.

## Slices (TDD, each its own commit)

**PR-A**
1. `ModRowsBuilder` (Application, pure): overview → ordered rows (D1), id-block reorder → new `Mods=` order. Unit tests.
2. `UpdateModsAsync` countdown (D3) — Infrastructure tests.
3. `ModStatusChip` + `ModsTable` (BbDataGrid, expandable parts, Remove/Undo, ▲/▼) — bUnit.
4. `PendingChangesBar` (count, +/− summary, countdown, Restart to apply) — bUnit.
5. New `ModsSection` composes them; delete `LiveModInventoryPanel` + F22 lists; rail: one "Mods" entry,
   `modbrowser` redirect — bUnit + rail tests.

**PR-B**
6. `return_tags` on search + `WorkshopBuildSupport` (B41-only rule) — unit tests.
7. `AddModsSheet` (paste/preview keyless, search key-gated, cards with Install via `ModInstallControl`, "Added ✓",
   multi-part hint, B41 blocked) — bUnit; delete `ModBrowserSection`.
8. Leftover footer row (count, expand, Reinstall) — bUnit.
9. Copy pass (no `WorkshopItems=`/`Mods=` outside a tooltip), app.css rebuild, floors bump, Playwright smoke +
   screenshot at 1280px, clean console; delete `prototype/mods-ux`.

## Acceptance

From the issue: the maintainer adds a mod and restarts with no docs (DMZ live pass); no horizontal scroll at 1280px;
clean Playwright console; Web.Tests floors bumped in csproj + ci.yml; app.css rebuilt.
