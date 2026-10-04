# Issue #335 Mini-Plan — Tinted (filled) status chips, shared server + host variants

**Status:** one PR (branch `feat/335-tinted-status-chips`, closes #335). Part of epic #348 (Fleet polish); unblocks
#341 (Hosts icon + chips).

**Written against:** issue #335; the Signal prototype's `.stat` chip; `Components/Ui/StatusBadge.razor`;
`wwwroot/js/live-status.js`; ADR 0003 (owned status badge).

## Objective

The status chip is filled in its status colour, as in the prototype: the whole chip is tinted (Running green,
Unhealthy red, Starting/busy amber, Stopped grey, Unknown/Unreachable violet). The same component gains a Host
variant so the Hosts page stops hand-rolling chip markup.

## Facts found (2026-10-04)

- `StatusBadge` renders on the Fleet board (inside `[data-live-row]`, re-toned by `live-status.js`) and in the Server
  Detail header (interactive since #299; its circuit re-renders it, no JS).
- `live-status.js` re-tones the badge by swapping `border-status-<tone>` on `[data-status-badge]` and
  `bg-status-<tone>` on `[data-status-dot]`; tones are `running|stopped|busy|unhealthy|unknown`.
- `HostInventory.razor` hand-rolls an Online / Unreachable chip and a Revoked / Disabled trust chip
  (`data-host-trust`).
- Prototype chip: `background: color-mix(in oklab, var(--sc) 15%, var(--card))`, border at 35% over transparent,
  label in `--sc`, mono 11px semibold uppercase, dot with a `0 0 7px -1px` glow; server chips are 112px wide, host
  chips auto width.
- Contrast (computed): a pure status-colour label on the 15% tint misses 4.5:1 in places — light running 3.96,
  light busy 3.35, light stopped 4.39, dark stopped 4.29.

## Decisions

- **D1 — one tone class, CSS-owned.** The chip's look lives in a small unlayered stylesheet (`Styles/status.css`,
  imported like `shell.css`): `.zw-status` reads a `--zw-sc` custom property that a tone class
  (`zw-status-running|stopped|busy|unhealthy|unknown`) sets; the dot (`.zw-status-dot`) inherits it. The tint,
  border, label ink and glow follow from the one variable, so a flip is a single class swap and works in both themes.
- **D2 — label ink stays AA.** The label is `color-mix(in oklab, var(--zw-sc) 80%, var(--foreground))`: it reads as
  the status colour but clears 4.5:1 in every state and theme (worst: light busy 4.66). The dot carries the pure hue.
- **D3 — Host variant.** `StatusBadge` takes either `State` (ServerRunState) or `Host` (new `HostStatus`: Online,
  Unreachable, Revoked, Disabled). Online→running, Unreachable→unknown, Revoked→unhealthy, Disabled→stopped (an
  operator choice, not a fault). Host chips size to their label; server chips keep a fixed width (112px) so the
  fleet column lines up. Unmatched attributes pass through, so `data-host-trust` stays on the trust chip.
- **D4 — live-status.js.** `applyBadge` swaps `zw-status-<tone>` on the badge; the dot is no longer toned (it
  inherits). `data-status-*` hooks are unchanged.
- **D5 — tests.** bUnit covers each server state and each host status → tone class + label, the width rule, and the
  pass-through attribute; HostInventory keeps its text assertions.

## Not done here

- The Hosts page's icon, card header and hostname layout — #341 (Hosts epic) adopts the rest of the prototype.
- Mod / diagnostics / audit chips keep their own outline style (not in scope).
