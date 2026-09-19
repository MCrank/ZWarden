# Feature 110 Mini-Plan — Workshop Metadata Enrichment & Browse

**Status:** PR-A + PR-B merged; **PR-C delivered** (this branch, closes
[#110](https://github.com/MCrank/ZWarden/issues/110)). Branch `feat/f110-workshop-browse`,
one commit per slice across **3 PRs** plus a **research spike up front** (PR-A keyless enrichment
core; PR-B the optional publisher-key setting + Workshop search; PR-C the adaptive Mod-Browser UI). Split out of F21
([#43](https://github.com/MCrank/ZWarden/issues/43), closed) and pairs with F22
([#44](https://github.com/MCrank/ZWarden/issues/44), closed) — this is the *browse-and-preview*
layer that feeds F22's install verb.

**Format:** PRD 60. **Written against:** PRD 34 (mod management — the discovery/browse UX half),
PRD 2.2 (TDD mandatory), PRD 2.3 (supportability), PRD 10 / [ADR 0015](../adr/0015-application-layer-authenticated-encryption.md)
(application-layer authenticated encryption for the optional stored key), PRD 30/38 (untrusted-data
posture); Feature 110 and the F21/F22 scope lines in
[`scope-and-sequencing.md`](../scope-and-sequencing.md) §6/§7; the
[F21 discovery mini-plan](./F21-workshop-and-mod-discovery.md) (whose observed inventory and
`mod.info` reader this extends) and the [F22 mod-management mini-plan](./F22-mod-management.md)
(whose `IServerModManager.AddWorkshopItemAsync` this "Install" button calls); the runtime research
[`project-zomboid-runtime.md`](../research/project-zomboid-runtime.md) §4 (the verified keyless vs.
key-required Steam Web API split) and §6 (`mod.info` keys — `require`, `incompatible`, `pzversion`);
[ADR 0018](../adr/0018-zwarden-owned-rbac.md) (authorization), [ADR 0019](../adr/0019-audit-is-append-only-tenant-owned-and-binds-the-auth-sink.md)
(audit); [`trust-boundaries.md`](../trust-boundaries.md) §3/§8 (observed-never-inferred; Workshop
JSON, ids, and names stay untrusted, bounded, escaped at render).

## Objective

Give an operator a **browse-and-preview** experience on top of F21's local-disk discovery: see
human-friendly Workshop **names and preview images** instead of bare numeric ids, **paste a Workshop
item id or collection URL** to preview a mod before installing it, and surface the four things an
operator actually weighs — **server-version compatibility, dependencies, load ordering, and a
best-effort multiplayer hint** — then hand off to F22's existing install/enable/reorder verbs.

The load-bearing constraint, verified live in the research doc and re-confirmed against the code
before writing this plan:

> **Steam's Workshop API is keyless for lookup-by-id, but real search needs a stored secret.**
> `ISteamRemoteStorage/GetPublishedFileDetails` and `GetCollectionDetails` take **no API key** but
> only resolve ids you already have. `IPublishedFileService/QueryFiles` (search all of Workshop)
> requires a confidential key. So "browse" splits cleanly into a **keyless floor** everyone gets and
> a **key-gated search** an operator opts into.

**The design that resolves the fork (maintainer-decided, see below): the key is an optional input.**
Blank ⇒ keyless mode (the default; no secret stored; the F21 "no Steam credentials" posture is
unchanged). Supplied ⇒ full Workshop search lights up and the Mod-Browser UI adapts. This is
capability-by-secret-presence, so the security posture only changes for an operator who deliberately
opts in — a far smaller blast radius than reversing the stance for everyone.

### Where the four operator concerns actually come from

Confirmed against `mod.info` (research §6) and the existing seams — **most of it is on-disk, not
from Steam**:

| Concern | Source | Mechanism |
|---|---|---|
| **Load ordering** | `Mods=` line order | Already shipped — F22 `ReorderModsAsync`. Browse just surfaces it. |
| **Dependencies** | `mod.info` `require=` (real, repeatable) | Extend F21's `ModInfoReader` + `ModCompatAnalyzer` to read and reconcile it. |
| **Server version** | `mod.info` `pzversion`/`versionMin`/`version` + the B42 `42/mod.info` split | Extend the reader; flag "B41 mod on a B42 server". |
| **Multiplayer support** | *No reliable machine-readable field* | **Best-effort only** — surface `tags` + version-compat; never assert an MP-safety we cannot verify (trust-boundaries §3, observed-never-inferred). |
| **Names / previews / size / updated** | Steam Web API (keyless `GetPublishedFileDetails`) | New control-plane metadata client (this feature). |

## The settled decisions

Put to the maintainer before writing; each took the agreed option.

1. **Optional operator-supplied key, keyless by default.** Not "keyless XOR key-required" — one
   codebase where the key's *presence* is a capability flag. Keyless enrichment + paste-id/collection
   browse + one-click install work with **no secret**. A stored key adds only the free-text search
   grid on top. Chosen over deferring search to a separate issue (we're in here already, and v1.1
   SaaS wants it) and over reversing the posture for everyone.

2. **The key is stored encrypted, write-only, redacted, control-plane-only — this is the highest
   priority.**
   - **Encrypted at rest** via the existing `ISecretProtector` (F3 / ADR 0015 — the same AEAD envelope
     F9 enrollment and MFA secrets already use). Never a plaintext column.
   - **Write-only in the UI** — the Settings field accepts a new value or "clear"; it never echoes the
     stored key back. It shows only `configured ✓ / not configured`.
   - **Redacted everywhere** — added explicitly to the F30 support-package redaction denylist and held
     in F6's secret-aware logging types; never in logs, audit detail, or a support package.
   - **Control-plane only** — matches the decided egress point: the key and every `api.steampowered.com`
     call live in `ZWarden.Web`/Infrastructure and are **never** sent down to an Agent.
   - **High-privilege, audited** — configuring/clearing it is gated on `Tenant.Manage` (Owner-level)
     and written to the audit trail (the value is never in the audit detail, only the act).

3. **Per-tenant storage now, install-wide in practice — SaaS-shaped from day one.** The permission
   model has only `Server`- and `Tenant`-wide scopes, so the key is a **tenant-scoped setting**
   (`WorkshopIntegrationSettings`, one row per tenant). In a single-tenant self-hosted deployment
   that *is* "install-wide" (one tenant, one key); in v1.1 SaaS it becomes per-customer with **no
   schema change**. Chosen over a global/system row that a later feature would have to migrate to
   per-tenant.

4. **Control-plane egress to `api.steampowered.com`** (maintainer-decided). Enrichment and search are
   pure display metadata that happen *before* a server is chosen, so the Web app calls Steam directly
   with §8 untrusted-JSON handling + response caching + graceful offline/air-gapped fallback (numeric
   ids still render). The Agent's Steam egress stays limited to SteamCMD **content** downloads —
   unchanged.

5. **No new persisted mod state; browse feeds F22, it does not replace config-as-truth.** Enriched
   metadata is ephemeral/cached (like F16 metrics, F21 inventory). "Install" from a preview card calls
   F22's existing `IServerModManager.AddWorkshopItemAsync` — no new install path, no new entity beyond
   the single settings row in decision 3.

## PR slices (TDD throughout — red before green, PRD 2.2)

### Spike (pre-PR-B, timeboxed) — confirm the key type for `QueryFiles`
Research §4 verified keyless `GetPublishedFileDetails` works and that `IPublishedFileService` is
*documented* as needing a "publisher API key… called from a secure server", but did **not** verify
live whether a plain Steam Web API key (`steamcommunity.com/dev/apikey`) suffices for `QueryFiles`
on public Workshop items, or whether a Steamworks *publisher* key is truly required. Those are
different credentials to obtain. Resolve this before building the Settings field so it asks for the
right thing; record the finding as a research-doc §4 addendum. Gates PR-B only — PR-A is independent.

### PR-A — Keyless enrichment core + extended `mod.info` (no UI, no secret) — **DELIVERED**
- **Extend `ModInfoReader` / `ModInfo`** (F21, `src/ZWarden.Agent/Mods/`) to also read `pzversion`,
  `versionMin`, `version`, `require` (**repeatable + comma-split** → list), `incompatible`, `tags` —
  same defensive posture (size-cap, bounded fields, malformed-line skip, per-list count cap). Read the
  **version-appropriate** file (prefer the B42 `42/mod.info` when present, per research §6). ✓
- **Extend `ModCompatAnalyzer`** with the pure `RequiresMissing` (an enabled mod's `require=` dep no
  installed item provides) and `IncompatiblePresent` (two enabled mutually-incompatible mods; PZ's
  `\`/`+`/`-` markers normalized) findings, and carry the version/dep/tag fields onto `DiscoveredMod`
  (wire) and `InstalledMod` (Application), mapped at `AgentHub`. **Additive-optional → `ProtocolVersion`
  stays 1** (matches F21). ✓
  - **Deferred:** a `WrongPzVersion` *finding* needs the server's build (B41 vs B42), which isn't
    threaded to the Agent yet — so PR-A carries `pzversion`/`versionMin`/`version` as **display
    metadata** for the UI to show, and the version *finding* is a follow-up once the build is available.
- **`IWorkshopMetadataClient`** (`src/ZWarden.Application/Workshop/`) + **`WorkshopMetadataClient`**
  (`src/ZWarden.Infrastructure/Workshop/`) — keyless `GetPublishedFileDetails` (`GetItemsAsync`) +
  `GetCollectionDetails` (`GetCollectionItemIdsAsync`) over a typed `HttpClient` to
  `api.steampowered.com`; §8 bounded/tolerant `JsonDocument` parsing, per-id `IMemoryCache`, numeric-id
  guard, https-only preview urls, **never throws** (any failure → not-found/empty). DI in
  `WorkshopServiceCollectionExtensions` (`AddZWardenWorkshop`), wired in `Program.cs`. ✓
  - **Refinement vs. plan:** the *authorized, server-scoped* `IWorkshopMetadataService` (Mod.View gate,
    resolve-a-pasted-id/collection) is built in **PR-C** alongside its UI consumer, to avoid a dead
    surface — PR-A ships the client seam + impl the service and UI compose over.
- Test-floor bumps: Contracts 131→134, Agent 411→426, Infrastructure 369→380. Synthetic fixtures only
  (F12 rule) — captured sample JSON via a stub handler, never a live call. ✓

### PR-B — Optional publisher key + encrypted storage + Workshop search (behind capability check)
- **`WorkshopIntegrationSettings`** entity (per-tenant, decision 3) + EF migration — the key stored as
  an `ISecretProtector` envelope string, plus a non-secret `KeyConfigured` flag for the UI.
- **`IWorkshopSettingsService`** — set (encrypt + persist), clear, and `IsSearchAvailableAsync`
  (capability check); `Tenant.Manage` gate, audited (act only, never the value). Settings-page field
  (write-only) in `Components/Pages/Settings/Settings.razor`, static-SSR EditForm + page handler.
- **`IWorkshopSearchService`** + `WorkshopSearchClient` — `QueryFiles` using the decrypted key
  (loaded per-request, never cached in plaintext); returns bounded, §8-treated results
  (title/preview/subs/updated). No-op / typed "search unavailable" when no key.
- **Redaction + logging**: add the key to the F30 redaction denylist; assert via test it never appears
  in a support package or log; secret-aware type so it can't be `ToString()`'d into a log.
- **ADR 0044** — "Workshop search is an optional, per-tenant, operator-supplied key; keyless by
  default; the key is AEAD-encrypted, write-only, redacted, and control-plane-only." Add one CONTEXT.md
  sentence.
- Test-floor bumps: Infrastructure, Web.Tests (**both** the csproj `--minimum-expected-tests` **and**
  ci.yml silent-drop-guard, per the discovery-floor gotcha).

### PR-C — Adaptive Mod-Browser UI (the mockup section) — **DELIVERED**
- **`IWorkshopMetadataService`** (`ZWarden.Application/Workshop`) + **`WorkshopMetadataService`**
  (`ZWarden.Infrastructure/Workshop`, registered in `AddZWardenWorkshop`) — the PR-A deferral: the
  authorized, server-scoped preview surface. Fail-closed (ADR 0018): resolves the Server through the
  tenant filter and requires **`Mod.View`** before it composes the keyless `IWorkshopMetadataClient`, so
  an unauthorized viewer never drives the control-plane Steam egress. `WorkshopReference` (pure) parses a
  bare id or a Steam URL's `id=` parameter without dereferencing it; a collection reference expands to its
  members, a single reference resolves to one item, and an id Steam cannot resolve degrades to a not-found
  item rendered as a bare id. Never throws. ✓
- New **`?section=modbrowser`** on the Server Detail vertical rail (Configure group, gated on `Mod.View`),
  replacing the reserved "soon" placeholder — matches the #162 rail + one-island pattern. ✓
- **Keyless mode (always):** a "Paste a Workshop item id or collection URL" input → preview card(s) with
  name / thumbnail / size / updated → **"Install"** button calling F22 `AddWorkshopItemAsync` (then the
  existing two-step enable flow). Compat is **honest and self-contained** — shown only for content already
  on disk (before install there is no `mod.info` to read): PZ version, dependency count, unsatisfied deps
  and declared conflicts reconciled against the observed inventory, and a best-effort multiplayer-**tag**
  hint (never an assertion, trust-boundaries §3). The full `WrongPzVersion` finding still waits on the
  server build being threaded to the Agent. ✓
- **Keyed mode (when `IsSearchAvailable`):** additionally renders the "Search the Workshop…" box + result
  grid, each card with the same compat chips + Install; a rejected key surfaces as "search unavailable". ✓
- **Enrich the installed panel:** `LiveModInventoryPanel` shows resolved names/previews next to ids. The
  enrichment runs in `OnAfterRenderAsync` — which never fires during a static server prerender — so the
  Steam egress happens only in a live interactive circuit, never on the initial render; bare ids paint at
  once and names/previews fill in when Steam answers, re-enriching when the installed-id set changes. ✓
- Bb* cards over `zw-mb-*` shell.css (`prefer-blueprint-over-raw-html`); all Workshop names/ids escaped
  and preview urls http(s)-constrained + lazy-loaded at render (§8). `npm run build:css` + committed
  `wwwroot/app.css`; Web.Tests floor bumped 301→311 (csproj + ci.yml), Infrastructure 397→411. ✓
- **Deferred (per plan):** the live `run-web`/`playwright-cli` screenshot of the section — the preview and
  search paths depend on live Steam egress, which the offline test tiers cannot exercise; the rendering
  and behaviour are covered comprehensively by real-host `ServerDetailModBrowserTests` (rail item, keyless
  form, search-box-when-keyed, preview cards + escaping via faked seams, install-enqueues-ConfigApply,
  search results, compat chips) and the bUnit `LiveModInventoryPanel` enrichment test.

## Risks & notes
- **Key-type uncertainty** is the one real unknown — the spike de-risks it before we build the field.
- **MP-support honesty:** resist badging multiplayer-safety we can't verify; surface `tags` + version
  and let the operator judge. Overclaiming here would violate observed-never-inferred (§3).
- **Offline/air-gapped:** every enrichment path degrades to bare numeric ids; browse/search simply
  say "metadata unavailable", never error the page.
- **Rate limits:** keyless calls have no documented per-IP quota (research §4) — cache responses and
  batch id lookups to stay well clear of the 403-triggered IP throttle.
- **Toolchain:** `zwarden-local-toolchain` for the build; `blazorblueprint` MCP + llms.txt for any new
  Bb component; check the component API before use (ADR 0003).
