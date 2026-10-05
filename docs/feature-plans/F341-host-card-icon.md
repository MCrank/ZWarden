# Issue #341 Mini-Plan — Host cards: server icon tile, tinted chips, online/unreachable subtitle

**Status:** one PR (branch `feat/341-host-card-icon`, closes #341). Part of epic #349 (Hosts polish).

**Written against:** issue #341; the Signal prototype's Hosts view (`.hostcard`, `.hc-hd`, `.mark`);
`Components/Pages/Hosts/HostInventory.razor`; #335's plan (`F335-tinted-status-chips.md`).

## Objective

The Host cards match the prototype's header: a server-rack icon tile beside the host name, the tinted status chips,
and a page subtitle that counts hosts online and unreachable.

## Facts found (2026-10-04)

- The tinted chips already shipped with #335: `HostInventory.razor` renders `StatusBadge Host=...` for the
  connection chip and the `data-host-trust` chip. Nothing hand-rolled remains, so that bullet needs no change.
- The unreachable card already has a violet border (`border-status-unknown`, full strength); the prototype softens
  it to 40% (`color-mix(... var(--status-unknown) 40%, var(--border))`).
- Prototype tile: 30×30, `--radius`, `--secondary` fill with `--secondary-foreground` ink; unreachable →
  `color-mix(in oklab, var(--status-unknown) 20%, var(--card))` fill with `--status-unknown` ink. Lucide `server`
  glyph (already in `ShellIcon`).
- Subtitle today: `N hosts · N connected`; prototype: `N hosts · N online · N unreachable`.

## Decisions

- **D1 — tile.** A `data-host-icon` span (`h-[30px] w-[30px] rounded grid place-items-center`) holding
  `<ShellIcon Name="server" />`: `bg-secondary text-secondary-foreground` normally, `bg-status-unknown/20
  text-status-unknown` when unreachable (`/20` over the card is the prototype's 20% mix with `--card`). It carries
  `data-host-icon-tone="unknown"` when unreachable so tests can assert the tint without matching utility classes.
- **D2 — border.** The unreachable card border drops to `border-status-unknown/40`, matching the prototype.
- **D3 — header layout.** Tile, then the name block (`flex-1 min-w-0`), then the chips column — every existing
  `data-host-*` hook is kept.
- **D4 — subtitle.** `N host(s) · N online · N unreachable` (`data-hosts-summary`); unreachable = enrolled hosts with
  no live connection. Still render-once (a connect/disconnect needs a reload; live refresh is #357's territory).
- **D5 — tests.** WebApplicationFactory page tests: the subtitle counts, the tile renders with the server glyph and
  its unreachable tone, the online tile is untinted. Then a light + dark screenshot check against the prototype.

## Not done here

- Card body layout (kv rows) and naming sweep — #343; Enroll host sheet — #342; live subtitle refresh — #357.
