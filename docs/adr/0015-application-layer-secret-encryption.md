# 15. Application-layer secret encryption: AES-256-GCM under a per-message HKDF subkey, in a versioned envelope

ZWarden protects secrets at the application layer with its own thin `ISecretProtector` over
`System.Security.Cryptography.AesGcm` — **not** ASP.NET Core Data Protection. **Every value is
encrypted under a fresh subkey derived as `HKDF-SHA256(masterKey, salt = 32 random bytes)`, so each
`AesGcm` key encrypts exactly one message.** The wire form is a self-describing, versioned envelope
`version ‖ keyId ‖ salt ‖ nonce ‖ ciphertext ‖ tag`, with a 16-byte (128-bit) tag stated explicitly
and a 12-byte (96-bit) random nonce. Keys are held in a **keyring** (`keyId → 32-byte key`) with one
active key for encryption and every key available for decryption; the master key never lives in the
database that holds the values it protects (PRD 10).

- Status: accepted
- Decided in: [#24](https://github.com/MCrank/ZWarden/issues/24) (the F3 mini-plan grilling)
- Bears on: PRD 10 (sensitive-data protection), PRD 48 (no secrets in logs), ADR 0005 (values live in
  the app database; keys must not), and every later feature that stores a secret (F4 credentials,
  F9 Agent credential, F29/F30 diagnostics redaction)

## Context

PRD 10 requires authenticated encryption for secrets and names AES-256-GCM as the likely algorithm,
"subject to current platform security guidance at implementation time". The verified platform facts
(`docs/research/deployment-security-standards.md` §5) make the naïve reading of that a trap:

- **The tag-size-less `AesGcm` constructors are obsolete** (`SYSLIB0053`): a caller must state the
  tag size, or risk a truncated-tag mismatch. So an explicit 16-byte tag is not a style choice.
- **NIST SP 800-38D §8.3 caps a single key at `2^32` encryptions when the 96-bit nonce is random**,
  after which the birthday-bound risk of nonce reuse — which in GCM is catastrophic, leaking the
  authentication subkey — becomes unacceptable. **Microsoft documents the nonce-reuse *requirement*
  but nowhere documents this `2^32` obligation.** A developer who reads only the .NET docs will never
  learn that random-nonce GCM imposes a key-rotation duty at all. F3's roadmap note calls this out by
  name: the rotation story "must be explicit, not inherited".
- **ASP.NET Core Data Protection's default is AES-256-CBC + HMACSHA256, not GCM.** GCM is reachable
  only through the `CngGcmAuthenticatedEncryptorConfiguration` path, which is Windows-CNG-only — the
  wrong shape for a product whose reference deployment is Linux containers.
- **`HKDF` (RFC 5869) ships as a static BCL class** on `net10.0` (`Extract`/`Expand`/`DeriveKey`),
  so a derive-a-subkey design costs no dependency.

The decision is therefore forced twice: which primitive, and how the rotation obligation is
discharged so that no later feature can reintroduce the footgun by writing "no code at all".

## Decision

**Own a thin `ISecretProtector` over `AesGcm`.** Value-level protection is not Data Protection's job;
Data Protection stays reserved for its actual role (the cookie/antiforgery key ring, a later feature).
Conflating the two would drag in the CBC+HMAC default or the Windows-only CNG-GCM path.

**Derive a per-message subkey.** For each `Protect`, generate `salt` (32 random bytes) and
`subkey = HKDF-SHA256(ikm = masterKey[keyId], salt, info = "ZWarden.SecretProtector.v1")`, then
`AesGcm(subkey, tagSizeInBytes: 16)` encrypts once with a fresh 12-byte random nonce. **Because each
subkey encrypts exactly one message, both nonce reuse and the `2^32` per-key cap become unreachable
by construction** rather than by a runtime counter we must maintain and never get wrong. The master
key's exposure is bounded by HKDF, not by a usage tally.

**A versioned, self-describing envelope.** Bytes are laid out as
`version(1) ‖ keyId ‖ salt(32) ‖ nonce(12) ‖ ciphertext ‖ tag(16)`, base64 for text columns. The
leading `version` byte lets the format itself evolve (a future AEAD or KDF change) without ambiguity;
`keyId` selects the master key at decrypt time.

**A keyring, one active key.** `IKeyRing` exposes `ActiveKeyId` and `Get(keyId) → key`; encryption
uses the active key, decryption uses whichever `keyId` the envelope names. **Rotation is: add a new
key, mark it active; retain old keys so existing values still decrypt** — no re-encryption pass
required, and no envelope-format change. Keys are 32 bytes, loaded from configuration (a base64 env
var and/or a mounted file), validated at startup, and the loader **fails closed**: a missing, short,
or unparseable key stops startup rather than silently degrading. Keys are never persisted to the
application database (PRD 10 / ADR 0005).

## Alternatives considered

- **ASP.NET Core Data Protection (`IDataProtector`) for value encryption.** Rejected: its default is
  AES-256-CBC + HMAC, and the GCM path is Windows-only CNG — both wrong for Linux containers and for
  PRD 10's GCM intent. It remains the right tool for its own job (framework key ring) and is not
  displaced there.
- **A single data-encryption key with a random nonce and a runtime invocation counter** that alarms
  and forces rotation before `2^32`. Rejected: it makes correctness depend on live bookkeeping that
  must survive process restarts and races, to defend a bound the HKDF-subkey design removes outright.
  Simpler crypto, strictly more operational risk.
- **ChaCha20Poly1305.** A clean primitive (fixed 256-bit key / 96-bit nonce / 128-bit tag, no
  tag-size obsolescence), but it departs from PRD 10's stated AES-256-GCM and still needs a
  random-nonce rotation story. Kept in reserve behind the envelope's `version` byte if a future
  platform reason favours it; not selected now.
- **Deterministic / synthetic nonce (SIV-style).** Rejected for v1.0: no BCL AES-GCM-SIV, so it means
  a third-party crypto dependency on the secret path — the worst place to carry one — for a property
  (misuse resistance) the per-message-subkey design already delivers against random nonces.

## Consequences

- **A recorded, immutable envelope format.** Once values are stored, the `version ‖ keyId ‖ salt ‖
  nonce ‖ ciphertext ‖ tag` layout is a persisted binary contract on two providers (ADR 0005);
  changing it is a new `version` byte plus a decrypt path for the old one, never an in-place rewrite.
  This is the "hard to reverse" that earns the ADR.
- **Rotation is cheap and non-destructive**, but decryption of old values depends on **retaining
  retired keys in the keyring**. Dropping a key orphans every value encrypted under it — stated here
  so a future operator does not learn it by data loss.
- **Per-value cost is one HKDF expansion plus a 44-byte overhead** (version + keyId + 32-byte salt +
  12-byte nonce + 16-byte tag, before base64). Accepted: secrets are low-volume and short, and the
  cost buys the removal of the `2^32` and nonce-reuse footguns.
- **The keyring fails closed**, so a deployment that forgets to supply a key does not start — a loud
  failure, deliberately chosen over a silent plaintext or default-key fallback.
- **The concrete secret *store* is still open (PRD 10, out of F3 scope).** F3 delivers the loading
  seam (`IKeyRing` from configuration); the reference deployment's Docker-secret / host-KMS choice is
  a later feature and slots in behind the same interface.
