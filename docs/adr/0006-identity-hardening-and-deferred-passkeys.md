# 6. Identity hardening: the PBKDF2 iteration count is configured, and passkeys wait for v1.1

Two decisions about ASP.NET Core Identity, both of which look like defaults and are not.
**The PBKDF2 iteration count is a deliberate configuration set against OWASP's current figure,
never inherited from the framework default.** And **passkeys are out of v1.0** — their opt-in
`Stores.SchemaVersion` is treated as a **migration decision**, not a feature flag.

- Status: accepted
- Decided in: [#11](https://github.com/MCrank/ZWarden/issues/11) (§10 item 6, and F4's entry in `docs/scope-and-sequencing.md`); evidence from [#6](https://github.com/MCrank/ZWarden/issues/6) and [#3](https://github.com/MCrank/ZWarden/issues/3)
- Bears on: PRD 11 (MFA and passkeys as a *desired* capability), PRD 2.1

## Context: the iteration count

Read from source, `PasswordHasherOptions.cs`: ASP.NET Core Identity's `PasswordHasher<TUser>`
ships **PBKDF2-HMAC-SHA512, 100,000 iterations**, 128-bit salt, 256-bit subkey (format marker
`0x01` = v3; `0x00` is the legacy v2 at PBKDF2-HMACSHA1/1,000).

OWASP's current figure for the **same PRF** is **220,000** — double-checked, 220k rather than
the 210k that circulates secondhand. So the shipped default sits roughly **2.2× below** current
guidance, and it is the value you get by writing no code at all.

That is exactly the shape of thing PRD 2.1 exists to prevent: a security parameter that is
correct-looking, invisible, and stale. Two further facts make it worse to leave alone:

- **Identity has not adopted Argon2 or bcrypt, and the .NET BCL has no Argon2 at all**, by
  stated policy — .NET requires OS-library backing on two platforms and only OpenSSL implements
  it. OWASP's preference order is Argon2id → scrypt → bcrypt → PBKDF2 (FIPS only), so ZWarden is
  on the *last* choice, which makes the tuning parameter the only lever available.
- .NET 10 obsoletes **every `Rfc2898DeriveBytes` constructor** (`SYSLIB0060`) in favour of the
  static `Rfc2898DeriveBytes.Pbkdf2(...)` one-shots, so any hand-rolled derivation on this path
  must use the new API.

### The decision, precisely

`PasswordHasherOptions.IterationCount` is set explicitly in Feature 4, to a value chosen against
OWASP's then-current figure for PBKDF2-HMAC-SHA512, and the chosen number and the date it was
chosen are recorded where the configuration lives. **This ADR deliberately does not fix the
integer** — pinning a number here that Feature 4 cannot revisit would recreate the problem in a
different file. What is fixed is that the value is *chosen*, not *inherited*, and that it is
reviewed at the Feature 40 release gate.

## Context: passkeys

Passkeys are **GA in .NET 10** and better built than expected: a full stable API
(`IdentityPasskeyOptions`, `IPasskeyHandler<TUser>`, `SignInManager.PasskeySignInAsync`,
`PerformPasskeyAttestation/AssertionAsync`, `UserManager.AddOrUpdatePasskeyAsync`), an
`AspNetUserPasskeys` table, and a scaffolded management and login UI with working
conditional-mediation autofill. PRD 11 lists them as a *desired* capability.

They are still out of v1.0, for three measured reasons:

1. **They are a primary factor with no built-in 2FA story.** PRD 11 requires MFA. Passkeys do
   not deliver it, so shipping them does not discharge a requirement — it adds a second
   authentication surface next to the one that does.
2. **Attestation statements are not verified by default.** `VerifyAttestationStatement` is null,
   which means "always return true". Shipping the default is shipping an unverified attestation
   path.
3. **The RP ID is inferred from the `Host` header** unless `IdentityPasskeyOptions.ServerDomain`
   is set, which makes host-header validation a precondition rather than a nicety. That is
   recorded as a boundary rule in `docs/trust-boundaries.md` §2 and is not free.

Also: `MapIdentityApi` has **no** passkey endpoints — the template's are app-authored — and
there is no Ed25519/EdDSA support (nine COSE algorithms).

### Why the schema version is a migration decision

Passkey storage is gated behind `options.Stores.SchemaVersion = IdentitySchemaVersions.Version3`.
The default is `Version1`, which calls `builder.Ignore<TUserPasskey>()`. That is not a feature
toggle: it changes the EF model and therefore the migration graph of a database that, by then,
holds production data on two different providers (ADR 5). Turning it on later is a schema
migration to be planned, tested on both providers and applied through Feature 2's migration
strategy — not a line added to `Program.cs` during a feature.

Recording it now matters because the *cheap* moment to add the column is before v1.0 ships and
the *correct* moment is when the feature is actually built. We are choosing the correct one, and
accepting that it costs a migration later.

## Alternatives considered

- **Inherit Identity's 100,000 iterations.** Rejected: 2.2× below current OWASP guidance, and
  invisible in the codebase, which makes it un-reviewable at the Feature 40 gate.
- **Replace `PasswordHasher<TUser>` with an Argon2id implementation.** Rejected for v1.0: the
  BCL has no Argon2, so this means a third-party dependency on the credential path — the worst
  place in the system to carry one — for a product whose deployment story is meant to be simple.
  The OWASP-tuned PBKDF2 route is explicitly permitted by the same guidance.
- **Ship passkeys in v1.0 with `SchemaVersion = Version3` set now** so the column exists even if
  the feature is dark. Rejected: a schema whose only purpose is to make a future decision cheaper
  is a decision made early with less information, and the unverified-attestation default means
  the path would exist before anyone has decided what to do about it.

## Consequences

- **A later passkey feature owes a migration on both providers**, not just a code change. That
  cost is accepted knowingly and belongs in the v1.1 plan.
- **MFA in v1.0 is Identity's existing story** (PRD 11: MFA, recovery codes, lockout), and is
  not satisfied by anything passkey-shaped.
- **Host-header validation is still required at the browser boundary**, for the reasons in
  `docs/trust-boundaries.md` §2, even though the passkey feature that makes it acute is
  deferred.
- NIST SP 800-63-4 is final (superseding 800-63-3 as of 2025-08-01) and its password rules are
  the ones Feature 4's policy must meet: **15-character minimum** for single-factor, 64-character
  maximum supported, salt ≥32 bits, **no composition rules**, **no periodic rotation**, and a
  mandatory breached-password blocklist check. Worth stating because several of those forbid
  things a "strong password policy" is usually assumed to include.
