# ZWarden visual identity & style guide

**Identity: "Signal" — a control-room wall you can read from across the room.**
Decided on [issue #18](https://github.com/MCrank/ZWarden/issues/18) by building four candidate
identities and reacting to them themed-vs-themed. ZWarden is an operations console: legibility
at a glance under stress beats atmosphere. The character lives in the chrome; the status ramp
stays boringly semantic and high-contrast so `UNHEALTHY` is never the thing that got prettier
and harder to spot.

- **Token file:** [`docs/style-guide/zwarden.css`](style-guide/zwarden.css) — the authoritative artifact. Drop-in for the BlazorBlueprint `:root` / `.dark` theme slot.
- **Live reference:** the [ZWarden Identity Explorer](https://claude.ai/code/artifact/bc01a7ed-7882-4786-bbc2-0408eb8ad3e4) renders these tokens against the real surfaces, with a live WCAG contrast readout on the status ramp. (Prototype captured at `prototype/identity-explorer.html`; all four candidates preserved there.)

Everything below describes what that token file means and the rules for consuming it. When the
two disagree, the token file wins.

---

## How it's consumed

The tokens are the whole identity. Components never hard-code a colour, radius, or font — they
read `var(--token)`. Reskinning ZWarden is editing this one file, nothing else.

- `zwarden.css` replaces BlazorBlueprint's default theme. `:root` is **light**, `.dark` is **dark** — the Blueprint contract, so its theme toggle works unchanged.
- **Dark-first is an application default, not a token-structure change.** ZWarden boots with `.dark` applied. Light is fully defined and first-class — never a degraded afterthought.
- The `StatusBadge` and `MeterBar` wrappers (Feature 0) are the only consumers of the `--status-*` and `--meter-*` tokens. Feature 0 makes the wrapper seam a rule; no view reaches for a status colour directly.

---

## Palette

Neutrals are biased cool toward the cyan accent (hue ~220–250), never a dead grey. One accent —
phosphor cyan — chosen **specifically so it never collides with the green/amber/red status ramp**.

| Role | Token | Light | Dark |
|------|-------|-------|------|
| Ground | `--background` | `oklch(0.965 0.006 220)` | `oklch(0.155 0.012 248)` |
| Surface | `--card` / `--popover` | `oklch(0.995 0.003 220)` | `oklch(0.195 0.014 250)` |
| Ink | `--foreground` | `oklch(0.20 0.014 245)` | `oklch(0.94 0.006 220)` |
| Muted ink | `--muted-foreground` | `oklch(0.46 0.018 235)` | `oklch(0.70 0.020 215)` |
| Primary (accent) | `--primary` | `oklch(0.50 0.12 205)` | `oklch(0.82 0.13 200)` |
| Border / input | `--border` `--input` | `oklch(0.87 0.008 225)` | `oklch(0.30 0.015 245)` |
| Focus ring | `--ring` | = primary | = primary |

Primary is the **only** brand accent. It marks the active nav item, the primary button, focus
rings, and the wordmark. It is **not** a status colour — never use it to mean "good".

---

## Typography

Two families, both OFL-licensed and **self-hosted** (Feature 32 ships behind Caddy with no
assumption of outbound internet — do not `@import` from a font CDN in production).

- **Chivo** — UI. A grotesque with enough spine to read as an instrument, not a Material app.
- **Chivo Mono** — all tabular data: player counts, CPU/mem, uptime, tick, versions. Uses `font-variant-numeric: tabular-nums lining-nums` so digits stay in their columns.

| Role | Size | Weight | Notes |
|------|------|--------|-------|
| Display | 26px | 700 | Server name, login tagline. `text-wrap: balance`. |
| Title | 19px | 700 | Page headings ("Fleet"). |
| Subtitle | 15px | 600 | Section leads. |
| Body | 13px | 400 | Base size. Keep prose near 65ch. |
| Small | 12px | 400 | Secondary / crumbs. |
| Label | 10px | 600 | Uppercase, `letter-spacing: 0.1em`. Column headers, eyebrows. |
| Data | 13px | 400 | **Chivo Mono**, tabular-nums. |

Weights loaded: Chivo 400/600/800/900, Chivo Mono 400/500/700.

---

## Iconography

**Lucide, and only Lucide** (`BlazorBlueprint.Icons.Lucide`). Mixing packs is how a UI starts
looking assembled rather than designed. Blueprint also ships Heroicons, Feather and Font Awesome —
do not reach into them. Default 16px in tables and nav; stroke width as shipped.

---

## Status & meter ramps

The load-bearing part. Both ramps are semantic and tuned per theme for high contrast. Values in
`zwarden.css`; contrast is verified live in the Explorer.

**Server state — `--status-*`:**

| State | Token | Hue | Meaning |
|-------|-------|-----|---------|
| Running | `--status-running` | green 148 | healthy and serving |
| Starting | `--status-busy` | amber 72–80 | transitional / busy |
| Unhealthy | `--status-unhealthy` | red 27 | serving but failing checks |
| Stopped | `--status-stopped` | grey 235 | intentionally down |
| Unknown | `--status-unknown` | violet 292 | agent unreachable — state not observed, **not** "down" |

**Meter thresholds — `--meter-*`:** `nominal` (green) < 60%, `watch` (amber) 60–85%, `hot` (red) > 85%.

**Contrast rule — this is what keeps the ramp legible, and it is a `StatusBadge` requirement:**
the badge **dot** carries the pure `--status-*` hue (the glanceable signal); the badge **label
ink** is mixed toward `--foreground` until it clears **4.5:1** against the surface behind it. On
dark grounds the pure colour already passes and the ink stays vivid; on light grounds amber and
green as ink fall below AA, so the ink darkens while the dot stays saturated. Decoupling "the
colour you spot" from "the text you read" is what makes `UNHEALTHY` both loud and legible. A
solid-fill pill (fill = status colour, label = computed on-colour) is the alternative rendering;
the tinted/dot form is the default in the fleet table.

Accessibility scope for this ticket is exactly these contrast ratios; a full WCAG audit is not.

---

## Component shape

- **Radius: `--radius` = 3px.** Squared with a hairline round. Cards, buttons, inputs, badges and menus all key off it. No 999px ovals anywhere in the operational UI.
- **Status badges are squared and fixed-width** (~108px), so the status column reads as one clean edge instead of jittering with the label length. Same rule for the meter/threshold pills. (This was the explicit call on #18: ragged ovals fight a squared UI.)
- **Round is reserved** for things that are genuinely round: avatars, the state dot inside a badge, the connection-status dot.
- Meter bars: thin track in `--meter-track`, fill in the threshold colour, mono percentage right-aligned.
- Tables lead the fleet view; big-number KPI tiles sit above them for the fleet-health summary (running / needs-attention / players / hosts). Tiles are appropriate here because those figures are the point of the page.

---

## Identity surfaces — where the Project Zomboid character lives

The console stays operational; personality is carried in the surrounding surfaces, in restraint.

- **Wordmark & favicon** — typographic, no illustration needed to ship: the `Z` mark (a `--primary` tile) plus `ZWARDEN` set in Chivo 800, uppercase, `letter-spacing: 0.2em`. The favicon is the `Z` mark.
- **Login** — the wordmark, an operational tagline, and a phosphor-cyan glow/scanline treatment in the panel. A PZ-toned tagline (e.g. *"This is how you deploy."*, a nod to the game's death screen) is welcome in the identity surfaces; the console chrome itself stays plain.
- **Empty / disconnected states** — a pulsing `--status-unknown` indicator plus muted, specific copy ("`ZWarden.Agent on host-04` unreachable — last seen 41s ago. Reconnecting over WSS…"). This is where the PZ survival tone can show in microcopy without costing legibility.

**Deferred (not part of this decision):** any *bespoke* Project Zomboid illustration for the login
or empty states. This guide specifies where such art goes and its tone; producing it is a
just-in-time art task for whoever builds those screens, not a gate on Feature 0.

---

## What was decided vs. left open

- **Decided:** the "Signal" palette (light + dark), Chivo / Chivo Mono, dark-first default, Lucide, 3px squared radius, squared fixed-width status badges, the two semantic ramps, and the dot-vs-ink contrast rule.
- **Rejected candidates** (preserved in `prototype/identity-explorer.html`): *Bunker* (cool instrument), *Knox* (warm survival chrome), *Warden* (clean SaaS baseline).
- **Not in scope here:** bespoke illustration (above); component-level UX beyond shape and colour (Feature 0 and the feature specs own that).
