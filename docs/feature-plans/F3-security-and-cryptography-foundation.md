# Feature 3 Mini-Plan — Security and Cryptography Foundation

**Status:** ready for implementation. Roadmap issue: [F3 (#24)](https://github.com/MCrank/ZWarden/issues/24). Track A.

**Format:** PRD 60. **Written against:** PRD §10 (Sensitive Data Protection), §48 (Logging — no secrets in logs), the verified platform facts in [`deployment-security-standards.md`](../research/deployment-security-standards.md) §5, ADR [0015](../adr/0015-application-layer-secret-encryption.md) (the AEAD envelope + HKDF subkey + keyring, written with this plan), ADR [0005](../adr/0005-both-database-providers-ship-in-v1-0.md) (values in the app DB, keys not), the trust boundaries ([`trust-boundaries.md`](../trust-boundaries.md) §2/§7/§8), and F1/F2's foundations. The four decisions below were settled in the F3 grilling.

## Objective

Deliver the security foundation every later feature that touches a secret will build on: a value-level
**`ISecretProtector`** giving authenticated encryption with an explicit, footgun-free rotation story;
a fail-closed **`IKeyRing`** that loads keys from configuration without ever putting them in the
database; and **secret-aware value types** that cannot accidentally stringify into logs, JSON, or a
debugger — so F4's credentials, F9's Agent credential, and F29/F30's diagnostics inherit "secrets are
encrypted at rest and never leak into output" by construction rather than by discipline.

## Dependencies

- **F2** — the persistence foundation. F3 stores nothing itself, but the envelope it produces is what
  later entities persist as a text/blob column on both providers (ADR 0005); the format is chosen here
  to round-trip unchanged on SQLite and PostgreSQL.
- **F1** — typed IDs, for the `crt-`-style key identifiers pattern (keyId is a short opaque string, not
  a typed ID — see Non-scope).
- **F0** — the offline test tier (all F3 tests are offline; no network, no container) and the
  warnings-as-errors / analyzer policy the new obsolete-API guards must satisfy.
- ADR 0015 — AES-256-GCM under a per-message HKDF-SHA256 subkey; the versioned envelope; the keyring
  rotation model; the thin-own-protector-over-`AesGcm` choice (Data Protection is *not* used here).

## Scope

1. **`ISecretProtector` (Q1, Q2, ADR 0015)** — `Protect(ReadOnlySpan<byte>) → string` and
   `Unprotect(string) → byte[]`, plus string convenience overloads. The implementation derives a fresh
   `subkey = HKDF-SHA256(masterKey, salt=32 random bytes, info="ZWarden.SecretProtector.v1")` per call,
   encrypts once with `AesGcm(subkey, tagSizeInBytes: 16)` and a 12-byte random nonce, and emits the
   envelope `version(1) ‖ keyId ‖ salt(32) ‖ nonce(12) ‖ ciphertext ‖ tag(16)`, base64-encoded.
   Decrypt reads `version`+`keyId`, re-derives the subkey, and authenticates; a tampered or
   wrong-key envelope throws (never returns garbage).
2. **`IKeyRing` + configuration loader (Q3, ADR 0015)** — `ActiveKeyId` and `Get(keyId) → key`. A
   loader binds a set of `keyId → base64(32-byte key)` from configuration (a `ZW_SECRET_KEYS` env var
   and/or a mounted file path) with one marked active. It **fails closed** at startup: absent, short
   (< 32 bytes), unparseable, or no-active-key ⇒ a clear exception that stops the host. Keys are never
   written to the application database.
3. **Secret-aware value types (Q4)** — a `Secret<T>` (and a `SecretString` specialization) wrapping a
   sensitive value so it is **safe by construction**: `ToString()` returns `"***"`, a
   `System.Text.Json` converter serializes it as `"***"` (and refuses to *deserialize* into one
   unless explicitly opted in), `[DebuggerDisplay]` redacts, and there is **no** implicit conversion
   to `string`. The real value is reachable only through an explicit, greppable `.Reveal()`.
4. **Redaction primitives (Q4)** — a small `Redaction` helper (mask-all, keep-last-N, and a
   known-key redactor for structured fields like `password`, `token`, `rcon`, `connectionstring`) that
   F29/F30 will reuse for the support package. Logging-framework-neutral; see §Documentation for the
   Serilog seam.
5. **Obsolete-API guards** — the code uses only the non-obsolete crypto surface: `AesGcm(key,
   tagSizeInBytes)` (never the `SYSLIB0053` tag-less ctors), `Rfc2898DeriveBytes.Pbkdf2(...)` if any
   PBKDF2 appears (never the `SYSLIB0060` ctors), and `HKDF` static methods. An architecture test
   backs this so a regression is a red build, not a silent weakening.
6. **The "no secret reaches a sink" architecture test (Q4)** — a test that fails the build if a
   `Secret<T>`/`SecretString` is passed to a logging call or a string-format sink, or if a raw key or
   `AesGcm` escapes the protector's assembly. This is how the PRD 10 exit condition — "sensitive values
   … cannot accidentally stringify into logs" — is *enforced*, not merely intended.
7. **Security tests** — known-answer round-trips, tamper detection, wrong-key rejection, nonce
   uniqueness across many encryptions, keyring rotation (encrypt under active, decrypt an old-key
   envelope), fail-closed loading, and the redaction/stringify guarantees.

## Non-scope

- **The concrete secret store** — Docker secrets / protected filesystem / host KMS (PRD 10, explicitly
  still open). F3 delivers the *loading seam* (`IKeyRing` from configuration); the reference
  deployment's store is a later feature that slots in behind it.
- **Password hashing** — that is ASP.NET Core Identity's `PasswordHasher` in **F4** (ADR 0006: the
  OWASP-tuned PBKDF2 iteration count). F3 does not hash passwords; PRD 10 says passwords are hashed,
  not encrypted, so they never touch `ISecretProtector`.
- **The Data Protection key ring** (cookies/antiforgery) — reserved for its own feature (F4); ADR 0015
  deliberately keeps it separate from value encryption.
- **Choosing/pinning the logging framework** — F3 stays neutral (Q4). Serilog is the intended stack
  when a logging feature lands (see Documentation); F3 only ensures the secret types drop into it.
- **mTLS / certificate lifecycle** (PRD 17, deferred to v1.1 by ADR 0007) and the Agent credential
  itself (F9) — F3 provides the crypto primitives F9 will use, not the enrollment protocol.
- **A typed ID for keys** — `keyId` is a short opaque config-supplied label, not a persistent entity,
  so it is a plain string, not a `TypedId` (no `key-` prefix added to the registry).

## Domain changes

- **`Secret<T>` / `SecretString`** and **`ISecretProtector`** / **`IKeyRing`** interfaces live in a
  place Domain and Application can both reference without taking an infrastructure dependency. The
  types use BCL `System.Security.Cryptography` only (not an "infrastructure framework"), so they may
  sit in `ZWarden.Domain`; the **concrete** protector, HKDF derivation, and the configuration key
  loader (which read env/files) live in **`ZWarden.Infrastructure`**, keeping Domain free of I/O —
  the same split F1/F2 used (ids in Domain, converters/DbContext in Infrastructure).
- **Glossary:** add **Secret-aware type** (a value that cannot stringify its contents into logs, JSON,
  or a debugger; contents reachable only through an explicit reveal) and **Envelope** (the versioned
  `keyId ‖ salt ‖ nonce ‖ ciphertext ‖ tag` form of a protected value) to `CONTEXT.md` if they sharpen
  the model. Terms only, no implementation detail.

## Contract changes

- **`ISecretProtector`** — the one way any feature encrypts a value at rest. Output is the ADR 0015
  envelope string; input on decrypt is that string; tamper/wrong-key throws.
- **`IKeyRing`** — the one way a protector obtains keys; rotation is "add active key, retain old";
  loading fails closed.
- **The stringify contract** — any `Secret<T>` renders as `"***"` through every accidental path
  (`ToString`, interpolation, JSON, debugger); the real value requires `.Reveal()`.

## Security considerations

- **Keys never share the values' database** (PRD 10 / ADR 0005 / trust-boundaries §7) — the keyring
  loads from configuration, and an architecture test keeps key material out of any EF-mapped type.
- **Fail closed** — a deployment missing a key does not start. No default key, no plaintext fallback.
- **The rotation footgun is removed by construction** (ADR 0015): per-message HKDF subkeys make the
  NIST `2^32` cap and nonce reuse unreachable, rather than defended by a counter that could be wrong.
- **Untrusted-data honesty** (trust-boundaries §8) — the redaction primitives are built knowing their
  input (player names, logs, config values, RCON output) is attacker-influenced end to end; redaction
  handles untrusted input, it does not bless it as clean.
- **No secret in error messages** (trust-boundaries §2) — decrypt failures report *that* they failed,
  never the key, envelope internals, or plaintext.

## Test plan

Written before the code (PRD 2.2). All offline tier.

1. **Round-trip** — `Unprotect(Protect(x)) == x` for empty, small, and large `x`, on the real
   `AesGcm` path.
2. **Nonce/salt uniqueness** — encrypting the same plaintext N times yields N distinct envelopes
   (distinct salt *and* nonce), and all decrypt back.
3. **Tamper detection** — flipping any byte of ciphertext, tag, nonce, salt, or keyId makes
   `Unprotect` throw, never return wrong plaintext.
4. **Wrong key** — an envelope encrypted under keyId A fails to decrypt when A is absent from the ring.
5. **Rotation** — with active key B and retired key A both present: new values encrypt under B, and an
   old A-envelope still decrypts; dropping A makes its envelopes undecryptable (asserted, as the
   documented cost).
6. **Fail-closed loading** — absent config, a < 32-byte key, an unparseable base64 key, and a ring
   with no active key each throw at load, with an actionable message that contains no key bytes.
7. **Explicit tag size** — the protector round-trips with a 16-byte tag; a decrypt that supplies a
   truncated tag is rejected (guards the `SYSLIB0053` intent).
8. **Secret stringify guarantees** — `Secret<T>.ToString()`, `$"{secret}"`, `JsonSerializer.Serialize`
   and the `DebuggerDisplay` all render `"***"`; `.Reveal()` returns the true value; there is no
   implicit `string` conversion (a compile-time assertion / analyzer-style test).
9. **Redaction primitives** — mask-all, keep-last-N, and known-key redaction over a structured bag
   redact `password`/`token`/`rcon`/`connectionstring` and pass through benign fields.
10. **Architecture guards** — (a) no obsolete crypto ctor (`SYSLIB0053`/`SYSLIB0060`) in the codebase;
    (b) a `Secret<T>` cannot be passed to a logging/format sink; (c) raw keys and `AesGcm` do not
    escape the protector assembly; (d) no key material appears on an EF-mapped type.

## Implementation slices

- **S1 — Secret-aware types + redaction (Domain).** `Secret<T>`, `SecretString`, the STJ converter,
  `DebuggerDisplay`, `.Reveal()`, and the `Redaction` helper. *Verify:* tests 8, 9.
- **S2 — Keyring + fail-closed loader (Infrastructure).** `IKeyRing`, the config binding from
  `ZW_SECRET_KEYS`/file, 32-byte + active-key validation. *Verify:* tests 6, and the rotation setup
  for 5.
- **S3 — `ISecretProtector` over `AesGcm` + HKDF (Infrastructure).** The envelope encode/decode, the
  per-message subkey derivation, the explicit 16-byte tag. *Verify:* tests 1, 2, 3, 4, 5, 7.
- **S4 — Architecture tests.** Obsolete-crypto guard, the no-secret-to-sink guard, key-escape guard,
  no-key-on-entity guard. *Verify:* test 10 (each red on an injected violation).
- **S5 — DI wiring + docs.** `AddSecurityFoundation()` registering the protector and keyring;
  `CONTRIBUTING`/`CONTEXT` notes and the Serilog seam. *Verify:* the host resolves `ISecretProtector`;
  fail-closed startup surfaces a clear error.

## Diagnostics

- **Decrypt failure** is a single, catchable exception type carrying the failure *reason category*
  (tamper vs unknown-keyId vs malformed) and **never** key or plaintext bytes — actionable for
  operators, safe for logs.
- **Fail-closed startup** names *which* key input was missing/invalid (env var vs file, missing vs
  short vs no-active), without echoing any bytes.
- **A protected value is self-describing** — its `version` and `keyId` are readable from the envelope
  header, so F29 diagnostics can report "encrypted under keyId X, format v1" without decrypting.
- **The stringify guarantee is observable in tests**, so a regression that lets a secret through is a
  failing build, not a production leak discovered later.

## Documentation

- `CONTRIBUTING.md` — how to encrypt a value (`ISecretProtector`), how keys are supplied
  (`ZW_SECRET_KEYS` / mounted file, base64 32-byte), the rotation recipe (add active, retain old),
  and the rule that new secret fields use `Secret<T>` and are never logged.
- `CONTEXT.md` — **Secret-aware type** and **Envelope** if they sharpen the model.
- **The Serilog seam** — F3 is logging-framework-neutral, but Serilog is the intended stack when a
  logging feature lands. The secret types are shaped so a Serilog `IDestructuringPolicy` /
  `Destructure.ByTransforming<Secret<T>>(_ => "***")` drops in with no type change; documented as the
  wiring point so the future feature is a one-liner, not a redesign.
- **ADR 0015** records the load-bearing, hard-to-reverse decisions (envelope format, HKDF subkey,
  keyring, Data-Protection rejection). This plan records the type-placement and test-topology choices.

## Acceptance criteria

1. `ISecretProtector` round-trips arbitrary values via AES-256-GCM with an explicit 16-byte tag and a
   per-message HKDF subkey; a tampered or wrong-key envelope throws.
2. Encrypting the same plaintext twice yields distinct envelopes (distinct salt and nonce).
3. `IKeyRing` loads from configuration, supports rotation (active + retained keys), keeps keys out of
   the database, and **fails closed** on a missing/short/unparseable/active-less configuration.
4. `Secret<T>`/`SecretString` render `"***"` through `ToString`, interpolation, JSON, and the
   debugger; the value is reachable only via an explicit `.Reveal()`; no implicit string conversion.
5. Redaction primitives mask-all, keep-last-N, and redact known sensitive keys.
6. Architecture tests fail the build on: an obsolete crypto constructor, a secret reaching a
   log/format sink, a raw key or `AesGcm` escaping the protector assembly, or key material on an
   EF-mapped type.
7. The offline tier is green; no obsolete-API or nullable warnings (warnings-as-errors, ADR 0013).

## Definition of Done

Per PRD 61, the applicable subset: acceptance criteria met; tests authored first; unit + architecture
tests pass in the offline tier; **no obsolete crypto API is used** (`SYSLIB0053`/`SYSLIB0060` clean);
the PRD 10 exit condition holds — sensitive values are securely stored and **cannot accidentally
stringify into logs**, enforced by test; error conditions modelled and diagnosable without leaking
secrets; documentation updated; CI green; no unresolved warnings. (Persistence of a concrete secret
entity, password hashing, and the logging pipeline are N/A at F3 — it is a foundation proven against
its own security tests, consumed by F4+ later.)
