# ZWarden

ZWarden is a secure, self-hosted control plane for deploying, operating, monitoring, and
troubleshooting Project Zomboid dedicated servers. This glossary is the project's canonical
vocabulary; it holds terms only, never implementation detail or decisions. Decisions live in
`docs/adr/`.

## Language

### Components

The product has exactly three first-class components, and their names are the only component
vocabulary.

**ZWarden.Web**:
The authoritative side of the system: UI, API, persistence, operation coordination, authorization,
audit, and the SignalR endpoints Agents connect to.
_Avoid_: Manager, control plane (as a component name), server (as a component name), backend

**ZWarden.Agent**:
The host-resident component that carries out server management on one host, connecting outbound to
ZWarden.Web.
_Avoid_: worker, node, daemon, client

**ZWarden.PZServer**:
The canonical managed Project Zomboid runtime container.
_Avoid_: game server (as a component name), container image

**Control plane**:
A description of the product as a whole, used in prose only. It never names a component.

### Domain

**Tenant**:
The owner of every tenant-scoped record. A self-hosted installation has one; a hosted installation
has many. Never selected by the browser.
_Avoid_: organization, workspace, account, customer

**Auth0 Organization**:
An external identity-provider reference held against a Tenant. It is not a Tenant and the two
identifiers are never interchangeable.

**Tenant-owned**:
A record that carries an immutable tenant scope, set once when it is created and filtered on every read.
It is scoped to exactly one Tenant; a Tenant is never tenant-owned.
_Avoid_: tenant-scoped entity (as a distinct concept), owned record

**Tenant filter**:
The scope check, always evaluated, that limits every tenant-owned read to the current Tenant. The
current Tenant derives from the authenticated session, never from the browser.
_Avoid_: tenant guard, row filter

**Default tenant**:
The single, fixed Tenant of a self-hosted installation, seeded at startup under a well-known identifier.
A hosted installation has many Tenants and no default.
_Avoid_: system tenant, root tenant

**Session**:
The authenticated, tenant-bearing context established at sign-in and carried in a secure cookie. The
current Tenant derives from it, never from the browser.
_Avoid_: token, JWT, login (as a noun for this)

**Authentication event**:
A record of an authentication-relevant occurrence — a sign-in, a lockout, an MFA verification, a
password reset, an external-login link. It is not an Audit event: the audit record and its store are a
separate, later concern.
_Avoid_: audit event (as a synonym), log entry

**Identity provider seam**:
The abstraction through which an external identity provider authenticates a subject and is mapped to a
local user. It is not an Auth0 Organization, and it is optional — the control plane authenticates users
itself and does not require one.
_Avoid_: SSO (as the whole thing), Auth0 (as the seam name)

**Server**:
One Project Zomboid server instance under ZWarden's management.
_Avoid_: instance, game, world, box

**Host**:
The machine an Agent runs on, and where its Servers run.
_Avoid_: server (in this sense), node, machine

**Operation**:
A durable, auditable unit of mutating work against a Server, with its own lifecycle and progress.
Only one conflicting mutating Operation may run against a Server at a time.
_Avoid_: job, task, command (as a synonym), action

**Configuration Revision**:
A recorded before-and-after state of a Server's configuration, captured as parsed values rather
than file bytes.
_Avoid_: version, snapshot, backup (which means something else here)

**Backup**:
A verifiable copy of a Server's data, created by an Operation and restorable through one.

**Enrollment**:
The authorized process by which an unknown Agent becomes a trusted one, using a single-use,
short-lived credential.
_Avoid_: registration (which means adding a Server), pairing, onboarding

**Mod ID** / **Workshop Item ID**:
Two distinct identifiers that must never be conflated: a Steam Workshop item may contain several
Project Zomboid mods.

**Support Package**:
A sanitized, redacted diagnostic bundle a user can share without leaking secrets.
_Avoid_: log dump, diagnostic export

### Identifiers

**Typed ID**:
A persistent-entity identifier: a time-ordered UUIDv7 scoped to an entity-type prefix, rendered
canonically as `<prefix>-<uuid>` (e.g. `agt-019c…`). Typed IDs are **non-interchangeable** — an
`AgentId` is never a `UserId` — and the raw UUID is never exposed to users, logs, APIs or
diagnostics.
_Avoid_: GUID (as the public form), untyped id, raw uuid

**Prefix registry**:
The canonical, closed set of entity-type prefixes. Each is short, lowercase-ASCII, unique, and
never reused for another entity type; adding one is an ADR (PRD 7).

| Entity | Prefix | Entity | Prefix |
| --- | --- | --- | --- |
| Tenant | `ten-` | Mod | `mod-` |
| User | `usr-` | Workshop item | `wsi-` |
| Role | `rol-` | Mod profile | `mdp-` |
| Agent | `agt-` | Ban record | `ban-` |
| Server | `srv-` | Player record | `ply-` |
| Operation | `op-` | Permission assignment | `prm-` |
| Audit event | `aud-` | Certificate record | `crt-` |
| Backup | `bkp-` | Notification | `ntf-` |
| Diagnostic package | `diag-` | Enrollment | `enr-` |
| Configuration revision | `cfg-` | | |

### Security

**Secret-aware type**:
A value wrapper for sensitive material that cannot stringify its contents — it renders a redaction
marker through printing, interpolation, serialization and the debugger, and the contents are read
only through an explicit reveal.
_Avoid_: sensitive string, raw secret (as the type of a stored credential)

**Envelope**:
The self-describing form of a protected secret: a key reference, the random values, the ciphertext
and its authentication tag, together. A protected value is always an envelope, never bare
ciphertext.
_Avoid_: blob, cipher text (as the whole stored value)

**Key ring**:
The set of encryption keys available at runtime — one active for protecting new values, all retained
for reading existing ones. It is never stored alongside the values it protects.
_Avoid_: keystore, key vault (which name external systems)
