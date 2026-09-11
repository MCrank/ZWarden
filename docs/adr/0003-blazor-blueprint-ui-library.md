# 3. Blazor Blueprint 3.16.0 as the UI component library

PRD 4 named **Blazor Blueprint** before anyone had built anything with it. It stands — but on
evidence now, at **BlazorBlueprint.Components / .Primitives 3.16.0** exactly, with three
conditions and a named revisit trigger. Runner-up was **MudBlazor 9.9.0**; **Microsoft Fluent UI
Blazor** is third and would have to be **v5**, still a release candidate at
`5.0.0-rc.5-26219.1`.

- Status: accepted
- Decided in: [#10](https://github.com/MCrank/ZWarden/issues/10), on the throwaway prototype [`prototype/blazor-ui`](https://github.com/MCrank/ZWarden/tree/prototype/blazor-ui/prototype); verified documentarily first in [#2](https://github.com/MCrank/ZWarden/issues/2)

## Context

[#2](https://github.com/MCrank/ZWarden/issues/2) found Blueprint real but alarming: repo created
2025-11-14, first release 2026-02-02, **648 of ~690 commits from a single maintainer**, 2.x→3.x
inside a few months, and a `nuspec` declaring `net8.0` only (it runs on `net10.0` by
compatibility). For a product with a security-review release gate, that is a bet, and PRD 4
could not be treated as settled on the PRD's say-so.

So the same `/servers` screen was built three times, once per candidate, each generated from its
own vendor template so the shell was each library's out-of-the-box opinion, and each driven by a
one-second server-side timer pushing over a live Blazor Server circuit — the same path Feature 16
telemetry will use. Then the two leaders were **themed to one brief** (squared corners, cool
slate ground, one shared status ramp, operational density) and re-judged, because comparing
vendor defaults only flatters whichever taste you happen to share. Then the winner was stressed
at **500 servers under a 1 Hz push**.

All three compiled first try, needed zero custom JavaScript, and none fought Interactive Server.

## Why Blueprint won

Four reasons, in order of weight.

1. **Aesthetic ceiling, judged themed-vs-themed.** Themed to the same brief, Blueprint reads as
   a purpose-built ops console and MudBlazor reads as a well-themed Material app — uppercase
   buttons, Material chip weight, and `MudDataGrid` wrapping server names onto two lines where
   Blueprint's table did not. The reason is structural: with Blueprint you write Tailwind against
   CSS variables, so there is no house style underneath to fight. For a product whose pitch is
   operational polish, that ceiling is an asset. It is also the same property that makes it more
   work: MudBlazor reached the brief through parameters (a 32-line `PaletteDark`,
   `DefaultBorderRadius`), Blueprint through a 91-line theme file plus two components of our own.
2. **Client surface.** Zero eager library JS on that page, against MudBlazor's 70 KB and Fluent
   v5's 442 KB (`lib.module.js`, loaded as a Blazor JS initializer and invisible in the markup).
   That is the smallest thing to defend at the Feature 40 security gate.
3. **Agent-facing documentation.** Blueprint's `llms.txt` and 124 per-component docs are far the
   best of the three; every component on the prototype screen was written from them with zero
   compile errors. Across fifteen features of agent-built UI, that compounds.
4. **The grid is real.** The strongest structural argument against Blueprint was that its grid
   could not carry the fleet view. It does not survive contact — but only because the first round
   used the wrong component. **`BbDataTable`** paginates to 5 rows even with
   `ShowPagination="false"` and has no density control. **`BbDataGrid`** has neither problem: at
   500 servers under a 1 Hz push it virtualizes to **23 rows in the DOM**, renders **exactly once
   per second with no backlog**, and **re-applies sorting on every push**. Column pinning, sticky
   header and global search all work.

## Alternatives considered

- **MudBlazor 9.9.0** (MIT, 10,591 stars, `net10.0` declared). Genuinely close, and better on
  every cost axis: no npm, semantic status colours out of the box, a squared chip from one
  parameter, honours `prefers-color-scheme`. Lost on the aesthetic ceiling and on 70 KB of eager
  JS. **It remains the fallback behind the seam.**
- **Microsoft.FluentUI.AspNetCore.Components.** Evaluated at **v5**, not the v4.14.4 that
  [#2](https://github.com/MCrank/ZWarden/issues/2) verified, because v4→v5 is breaking and
  starting on v4 buys a migration. v5's newest published build is `5.0.0-rc.5-26219.1`, with no
  rc.6 or rc.7 on NuGet or as a tag. Shipping v1.0 on a release candidate, or on a v4 that owes
  a migration, is a worse risk than Blueprint's. Its 442 KB of library JS also loses on point 2.
- **Radzen** was dropped from the candidate set before the prototype.

## Consequences, including the ones we dislike

- **Bus factor of one**, knowingly accepted. ZWarden now carries three such dependencies — TUnit
  (ADR 2), Loretta (ADR 10), Blueprint — mitigated identically: pin, seam, keep the fork option
  open.
- **Node and a Tailwind step enter a .NET-only build**, which PRD 2.4 does not anticipate.
  `npm install` is roughly 20 MB of `node_modules`. This is a real cost against a stated
  principle and it is paid deliberately.
- **Failing to run the Tailwind step is silent.** `blazorblueprint.css` covers the components;
  the utilities *you* write around them are generated by scanning your own markup. Write
  `grid-cols-4` without regenerating and there is no error, no warning — the class is simply
  absent and the layout does not apply. bUnit asserts markup, not CSS, so nothing in the test
  suite catches it either. (Note this contradicts Blueprint's own `llms/setup.txt`, which says
  "No separate Tailwind setup required".) **This is the nastiest failure mode found in the whole
  evaluation**, and it is the reason for condition 2 below.
- **ZWarden owns some components outright.** `BbBadge` hard-codes `rounded-full` — no `--radius`
  token squares it — and its `BadgeVariant` has no success/warning/info. On an ops dashboard
  where status colour *is* the signal, the status badge is ours (32 lines in the prototype). That
  is the seam working as intended, not a surprise.
- **Two upstream defects found in a day**, both small and both worth reporting: the global search
  is bound to `change`, not `input`, so the documented `SearchDebounceMs = 300` never fires as you
  type (overridable via `@bind-SearchText`); and `ShowFilterBar="true"` rendered no filter bar.
- **Virtualization removes prerendered rows.** With `Virtualize="true"` the fleet table's SSR HTML
  contains no rows and the table paints empty until the circuit connects. Feature 36 decides
  deliberately: accept blank-then-fill, or drop virtualization below a row threshold.
- Blueprint defaults to light with a manual JS class toggle, where Mud and Fluent honour
  `prefers-color-scheme` out of the box.

## Conditions

1. **The wrapper seam is a Feature 0 rule, not a habit.** ZWarden components over theme tokens,
   library primitives underneath. Held consistently, "expensive to reverse once fifteen features
   of UI exist" stops being true — and that, rather than optimism about the maintainer, is the
   real answer to the bus factor.
2. **CI must fail on a stale `app.css`.** Regenerate and diff against the committed file. This
   belongs in Feature 0's pipeline alongside `--minimum-expected-tests` (ADR 2).
3. **Pin exact versions.** `3.16.0`, never `3.*`. A 4.x landing mid-feature is the likeliest way
   this hurts.

## Revisit trigger

Fork at the pinned version — Apache-2.0 permits it, and the library is pure C# and CSS with no
native binaries, so a fork keeps working — if **any** of: upstream publishes no release for six
months; a major version lands with breaking changes the seam cannot absorb; or `llms.txt` stops
being maintained, since that is a load-bearing reason for the choice. Switching libraries behind
the seam stays available and is the fallback if a fork proves too expensive.
