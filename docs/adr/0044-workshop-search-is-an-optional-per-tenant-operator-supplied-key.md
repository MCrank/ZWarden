# 44. Workshop search is an optional, per-tenant, operator-supplied key; keyless by default; the key is AEAD-encrypted, write-only, redacted, and control-plane-only

Keyless Workshop enrichment (names, previews, paste-an-id, expand-a-collection) needs **no secret** and is
the default. Free-text **search** across all of Workshop hits `IPublishedFileService/QueryFiles`, which
requires a Steam Web API key, so search is a **capability an operator opts into by storing a key** — the
key's *presence* is the capability flag, not a separate mode. The key is stored **per tenant**, encrypted at
rest via the existing `ISecretProtector` AEAD envelope (ADR 0015), is **write-only** in the UI (never echoed —
only `configured ✓ / not`), is **redacted** everywhere (F30 denylist + `SecretString`), is gated on
`Tenant.Manage` and **audited** (the act, never the value), and — like all `api.steampowered.com` egress —
lives **only in the control plane**, never on an Agent.

- Status: accepted
- Decided in: #110 (F110 PR-B; the key-type fork resolved by the research spike — a standard
  `steamcommunity.com/dev/apikey` key suffices for `QueryFiles`, no publisher key)
- Bears on: PRD 34 (mod discovery/browse), PRD 30/38 (untrusted data), ADR
  [0015](./0015-application-layer-secret-encryption.md) (AEAD envelope reused for the key), ADR
  [0016](./0016-tenant-isolation-query-filter-and-default-tenant.md) (the settings row is tenant-owned), ADR
  [0018](./0018-zwarden-owned-rbac.md) (`Tenant.Manage`), ADR
  [0019](./0019-audit-is-append-only-tenant-owned-and-binds-the-auth-sink.md) (audit the act, not the value);
  builds on the keyless metadata client shipped in #110 PR-A; feeds the Mod-Browser UI in PR-C

## Context

`docs/research/project-zomboid-runtime.md` §4 (with the F110 PR-B spike addendum) establishes the load-bearing
split, verified live: `ISteamRemoteStorage/GetPublishedFileDetails` and `GetCollectionDetails` are **keyless**
but resolve only ids you already hold, while `IPublishedFileService/QueryFiles` (search) returns **HTTP 403
without a key** and is absent from the keyless `GetSupportedAPIList` surface. The spike closed the one open
question — which *kind* of key — against Valve's own docs: `QueryFiles` carries no "requires a publisher API
key" flag (only the six mutating methods do), so a **standard** Steam Web API key suffices.

That makes "browse" cleanly two layers: a keyless floor everyone gets (PR-A) and a key-gated search an operator
turns on. Storing a Steam Web API key raises the security bar sharply — it is a long-lived credential that must
never leak into logs, audit detail, a support package, or the browser — so the key's handling is the decision's
centre of gravity, above the search feature itself.

## Decision

- **Optional key, keyless by default — one codebase, capability by presence.** No key ⇒ the F21/PR-A posture
  is unchanged (no secret exists). A stored key adds only the free-text search grid. This is not "keyless XOR
  key-required"; `IWorkshopSettingsService.IsSearchAvailableAsync` is the single capability check the search
  path and the UI read.
- **Per-tenant from day one.** `WorkshopIntegrationSettings` (`wis-`) is one tenant-owned row per tenant
  (ADR 0016), enforced by a unique index on `TenantId`. In single-tenant self-host that *is* install-wide; in
  v1.1 SaaS it is per-customer with no schema change. Chosen over a global/system row a later feature would
  have to migrate.
- **The key is encrypted, write-only, redacted, control-plane-only — the highest-priority constraint.**
  - *Encrypted at rest* as an `ISecretProtector` envelope string (ADR 0015) — the same AEAD F9/MFA use. The
    application layer encrypts before persist and decrypts only for a single outbound `QueryFiles` call; the
    plaintext lives only inside a `SecretString` and is never cached.
  - *Write-only* in the UI: the field accepts a new value or "clear" and never echoes the stored key. The page
    reads only the non-secret `KeyConfigured` flag (derived from the envelope's presence, so it cannot drift).
  - *Redacted everywhere*: an explicit Steam-key token in the F30 `Redaction` denylist (auto-honoured by the
    support-package redactor) and held in `SecretString` so it cannot be `ToString()`'d into a log.
  - *Control-plane only*: the key and every `api.steampowered.com` call live in Web/Infrastructure and are
    never sent to an Agent (whose Steam egress stays SteamCMD content only).
  - *High-privilege, audited*: set/clear are gated on `Tenant.Manage` (fail-closed) and audited — the audit
    detail names the act (`Workshop.KeyConfigured` / `Workshop.KeyCleared`), never the value.
- **Search never throws and fails to a safe state.** No key ⇒ `Unavailable` with no outbound call. A rejected
  key (401/403) ⇒ `Unavailable` (the capability is not usable). Any other failure ⇒ an available-but-empty
  result. Results are untrusted JSON — bounded here, escaped at render (trust-boundaries §8).
- **No value converter on the settings column.** Because the application layer owns encryption explicitly, the
  key column stores a plain envelope string; the DbContext's model-cache-keyed protector coupling (used for
  Identity tokens) is not extended, keeping the model simple.

## Alternatives considered

- **Ask for a Steamworks *publisher* key.** Rejected once the spike proved a standard key works for
  `QueryFiles` — a publisher key is harder to obtain and unnecessary for read-only search.
- **A global/install-wide key row.** Rejected: the permission model has only Server and Tenant scopes, and a
  global row would force a migration to per-tenant when SaaS lands. Per-tenant is install-wide in practice for
  self-host at no cost.
- **An EF value converter on the key column (mirroring the Identity-token encryption).** Rejected: it couples
  the model-cache key to a protector and hides crypto in the mapping. Encrypting in the service is explicit,
  unit-testable with a fake protector, and keeps the column a plain string.
- **Storing a separate persisted `KeyConfigured` boolean.** Rejected: two sources of truth can drift; deriving
  it from the envelope's presence cannot.
- **Letting the Agent call the Steam Web API.** Rejected: enrichment/search happen before a server is chosen
  and are pure control-plane display metadata; the Agent's Steam egress stays SteamCMD content only.

## Consequences

- A tenant that never sets a key sees exactly today's keyless behaviour; nothing about the security posture
  changes for them. Turning search on is a deliberate, audited, Owner-level act with a small blast radius.
- A configured key that Steam later rejects surfaces as "search unavailable", not as an error page or an empty
  match — the operator learns their key needs attention without the key ever being shown back to them.
- The key is a long-lived secret in the database (as an AEAD envelope). Rotating it is a re-set; clearing it
  returns the tenant to keyless mode. It is deliberately excluded from support packages and logs.
