# 12. Player management ships without whitelist *addition*

PRD 36 asks ZWarden.Web to support "whitelist" among its player-management actions, "where
available through supported PZ administrative mechanisms". Measured against the shipped build,
**whitelist addition is not available through any supported mechanism**, and the only path that
works is not a whitelist operation at all — it mints Project Zomboid account credentials.

Feature 19 therefore ships **player enumeration, connect/disconnect events where available,
kick, ban, unban, `removeuserfromwhitelist`, and the whitelist *mode* toggle**. It does **not**
ship whitelist addition.

This is a reinterpretation of PRD 36 against the shipped build, not a defiance of it: the PRD's
own qualifier, "where available through supported PZ administrative mechanisms", licenses this
reading, and **the shipped build is what defines "supported"**.

- Status: accepted
- Decided in: [#5](https://github.com/MCrank/ZWarden/issues/5) (verified against **Build 42.20.4**), rescoped by [#11](https://github.com/MCrank/ZWarden/issues/11) (F19 in `docs/scope-and-sequencing.md`)
- Reinterprets: **PRD 36**

## Context

`addusertowhitelist` and `addalltowhitelist` are marked **`@DisabledCommand`** in 42.20.2 and
are reachable from **nowhere** — not RCON, not the server console. This is not an RCON
limitation. `GameServer.rcon()` and the stdin handler both call `handleServerCommand(cmd, null)`
and `isCommandComeFromServerConsole()` is literally `return this.connection == null`, so **PZ
cannot distinguish RCON from console**: there is no console-only command RCON cannot reach. A
command disabled in the build is disabled everywhere.

What still works:

- **`removeuserfromwhitelist`** — whitelist *removal* is fine.
- **Whitelist *mode*** is the `Open` INI key (default `Open=true`), live-settable over RCON via
  `changeoption Open false`. So an operator can close the server to non-whitelisted players
  through ZWarden.
- **`adduser "user" "password"`** — the only working path to *add* someone.

That last one is why this is a scope decision rather than a gap to work around.

## Why `adduser` is not an acceptable substitute

`adduser` does not whitelist an existing identity. It **creates a Project Zomboid account with a
password ZWarden chooses or relays**. Adopting it would drag into v1.0 a whole problem the rest
of the release does not have:

- a **secret ZWarden mints on an operator's behalf** and must then deliver to a player through
  some channel;
- **storage** of that secret, or a deliberate decision not to store it and the recovery story
  that implies;
- **rotation** and reset paths for credentials belonging to accounts ZWarden does not own.

Nothing else in v1.0 handles user-facing credential delivery. The RCON secret, the one other
secret in this area, is deliberately Agent-generated and never seen by ZWarden.Web
(`docs/trust-boundaries.md` §5), and adding a credential-minting path would cut directly against
that posture.

**PRD 64 criterion 8 is satisfied without it**, which is the test that actually governs v1.0
scope.

## Alternatives considered

- **Implement `adduser` behind an elevated permission and accept the secrets work.** Rejected on
  scope: it is a secrets feature wearing a player-management hat, and it would be the only one
  in the release.
- **Write the whitelist file directly instead of using an administrative command.** Rejected:
  it means ZWarden becoming a second author of PZ's own user store while the server is running,
  which is the same class of problem ADR 11 exists to contain, with no drift-detection story and
  no documented file contract.
- **Report whitelist addition as unsupported in the UI and leave the button there, disabled.**
  Partially adopted in spirit — the *mode* toggle and removal are present, so the concept is not
  absent from the product. What is rejected is shipping an addition affordance that cannot work.

## Consequences

- **An operator who needs to admit a specific new player must do it outside ZWarden**, by
  creating the PZ account themselves. ZWarden can close the server (`Open=false`) and can remove
  someone, but cannot add them. This is a genuine product gap, it is visible to users, and it is
  accepted.
- **Documentation owes an explanation**, not silence. "Whitelist" appears in PZ's own
  vocabulary, so an operator will look for it; the product should say why the addition half is
  missing and what to do instead.
- **Revisit when the shipped build changes.** These commands are disabled, not removed. If a
  future build re-enables `addusertowhitelist`, this decision's whole basis is gone and Feature
  19 should regain the action. Verifying that is cheap; the trigger is any PZ build that changes
  the `@DisabledCommand` set.
- Related and unchanged: `connections` and `disconnect` are gone in 42.20.2, and
  `checkModsNeedUpdate` returns only "Checking started…" — its verdict never reaches the RCON
  caller. Neither is part of this decision, but both are the same kind of shipped-build
  constraint and belong in the same place in a reader's head.
