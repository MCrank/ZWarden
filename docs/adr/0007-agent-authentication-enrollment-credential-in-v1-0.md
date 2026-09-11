# 7. Agent authentication is a bearer credential in v1.0; mTLS moves to v1.1

v1.0 authenticates an Agent with a **single-use, short-lived enrollment credential**, exchanged
during enrollment for a **revocable, rotatable per-Agent credential** presented over **WSS**.
**Mutual TLS and the certificate authority are deferred to v1.1.**

**This overturns PRD 17**, which says production Agent authentication *shall* support mutual TLS.
The residual risk is stated plainly below and is not hedged: **theft of the credential on a
compromised host is not mitigated by possession-of-key the way mTLS would be.**

- Status: accepted
- Decided in: [#11](https://github.com/MCrank/ZWarden/issues/11) (F9 in `docs/scope-and-sequencing.md`, §10 item 1); named as the system's sharpest weakness in [#7](https://github.com/MCrank/ZWarden/issues/7) / `docs/trust-boundaries.md` §3
- Overturns: **PRD 17**

## Context

PRD 17 asks for mutual TLS *and* the entire certificate lifecycle behind it: issuance, renewal,
rotation, expiration, revocation, Agent disablement, lost-Agent recovery. That is not one
feature; it is a private CA and its operational story, in a product whose first release is meant
to be installable with a Compose file (PRD 2.4, criterion 1). None of that design exists — it
sits on the map's "Not yet specified" list, unspecified, and it was gating Feature 9, which
everything in the Agent plane sits behind.

What PRD 17 is *for* is served differently here. Two of its requirements are met by the
credential model with no CA at all:

- **Criterion 14** (no inbound management port on a host) is satisfied by the **outbound**
  WSS/SignalR connection model either way; it is a property of the direction of the connection,
  not of the credential.
- **PRD 63A's single-use, short-lived enrollment rule** is satisfied either way, and is
  unchanged: enrollment still uses a one-time credential.

So what mTLS would actually add over this design is narrower than PRD 17 implies: possession-of-
key authentication in place of possession-of-secret, and a revocation story backed by a CRL
rather than by a row in ZWarden's own database. The first is real. The second is arguably worse
in a self-hosted product, because ZWarden already has an authoritative database and a CA would
add a second source of truth about which Agents are trusted.

## The residual risk, stated without hedging

Transport security and **server** authentication come from WSS: the Agent verifies it is talking
to the right ZWarden.Web, and nobody on the wire can read or alter the traffic.

**Agent** authentication rests on a bearer credential. Therefore:

> An attacker who reads the credential off a compromised host can impersonate that Agent from
> anywhere, until the credential is revoked or rotated. mTLS would not fix this either if the
> private key is readable, but mTLS at least *permits* hardware-backed or non-exportable key
> storage, which a bearer secret does not. The gap is real and is not closed by rotation
> frequency alone.

Two things bound it, and neither eliminates it. First, `docs/trust-boundaries.md` §1 already
establishes that **ZWarden.Agent sits inside the host's trust domain** — Docker socket access is
effectively host root — so an attacker with the credential has, in most realistic scenarios,
already got the host and can do through the Agent what it could do as the Agent. The credential
is not the crown jewel on a host that is already lost. Second, the credential is **revocable and
rotatable** by design, and Agent disablement is in Feature 9's scope, so the blast radius is
bounded in time by an operator action rather than by a certificate expiry.

This is the **first item on the Feature 40 attack list**, on purpose.

## Alternatives considered

- **Ship mTLS in v1.0, as PRD 17 says.** Rejected on sequencing, not on merit: the CA design,
  its lifecycle and its recovery story are unspecified, and building them would put the entire
  Agent plane behind an unscoped piece of security infrastructure. It is still the right
  destination — hence v1.1, not "never".
- **Long-lived static shared secret per Agent.** Rejected: same theft exposure with none of the
  revocation or rotation story, which is what makes the bearer model tolerable at all.
- **Defer Agent enrollment itself to v1.1 and ship single-host only.** Rejected by the scope
  test: PRD 64 criterion 14 puts remote multi-host Agents (F35) *in* v1.0, so there is no version
  of v1.0 without enrolled Agents.

## Consequences

- **PRD 17 is not met by v1.0.** Anyone auditing against the PRD will find the gap; this ADR is
  the answer, and the gap is intentional and time-boxed to v1.1.
- **The CA design remains unspecified** and stays on the "not yet specified" list: issuance,
  renewal, rotation, revocation, lost-Agent recovery. Deferring it does not design it.
- **Feature 35 (remote/multi-host Agents) ships in v1.0 carrying this credential model**, with
  no certificate lifecycle. WAN reconnect behaviour is therefore credential-based too.
- **The v1.1 migration is a real piece of work, not a switch.** Moving to mTLS means every
  already-enrolled Agent has to acquire a certificate through some path that is itself
  authenticated by the credential this ADR chose — so the credential model has to remain
  supported through the transition.
- **Credential storage on the host is now a load-bearing detail** with no framework help. The
  reference deployment's concrete secret-storage mechanism is still open (PRD 10) and this
  decision raises its importance.
