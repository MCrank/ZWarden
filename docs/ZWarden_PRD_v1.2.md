# ZWarden --- Product Requirements Document

**Product:** ZWarden\
**Tagline:** *Control the outbreak.*\
**Project Type:** Greenfield\
**Primary Platform:** .NET 10\
**Architecture:** Multi-tenant-capable Blazor Control Plane +
Distributed ZWarden Agent + Canonical Managed Project Zomboid Container\
**Deployment Philosophy:** Docker-first, secure-by-default, simple
self-hosted installation with optional hosted SaaS, multi-host, and
multi-server deployments\
**Document Status:** Baseline PRD\
**Version:** 1.2

------------------------------------------------------------------------

# 1. Product Vision

ZWarden is a secure, self-hosted control plane for deploying, operating,
monitoring, and troubleshooting Project Zomboid dedicated servers.

The product shall provide administrators with a modern web interface
capable of managing the complete lifecycle of one or more Project
Zomboid servers, including:

-   installation
-   startup and shutdown
-   restarting
-   server configuration
-   Sandbox configuration
-   SteamCMD updates
-   Workshop/mod management
-   connected-player management
-   kick and ban operations
-   whitelist and administrative operations
-   RCON
-   live console/log viewing
-   backups and restore
-   health monitoring
-   operational diagnostics
-   audit history
-   secure remote management
-   automated HTTPS deployment
-   multi-server management
-   multi-host management

The preferred user experience shall allow a new installation to be
deployed with Docker Compose with minimal manual configuration.

ZWarden shall support two first-class deployment models:

1.  **Self-hosted control plane**
    -   The customer runs ZWarden.Web, ZWarden.Agent, and the managed PZ
        server.
    -   SQLite is supported for small installations.
    -   PostgreSQL is supported for larger installations.
    -   The customer controls the database, network, credentials, and
        infrastructure.
2.  **Hosted SaaS control plane**
    -   ZWarden.Web is operated as a multi-tenant service.
    -   Customers register an account and create one or more
        tenants/workspaces.
    -   Human authentication may use Auth0 Organizations or another
        supported OIDC provider.
    -   Customers generate one-time Agent enrollment credentials in the
        portal.
    -   Their locally installed ZWarden.Agent connects outbound to the
        hosted control plane.
    -   The customer does not need to expose a management port or run
        the web portal locally.

The same Agent binary, Agent protocol, authorization concepts,
diagnostics, and server-management capabilities shall work in both
deployment models.

A hybrid deployment may be supported later, but it shall not require a
separate Agent architecture.

ZWarden shall provide its own canonical Project Zomboid server container
so that the supported deployment model does not depend upon undocumented
behavior or filesystem conventions of third-party Project Zomboid
images.

------------------------------------------------------------------------

# 2. Core Product Principles

## 2.1 Secure by default

Security shall be treated as a core architectural requirement rather
than a later hardening activity.

The product shall follow:

-   OWASP Top 10
-   OWASP ASVS
-   applicable OWASP Cheat Sheet guidance
-   least privilege
-   defense in depth
-   secure defaults
-   deny-by-default authorization
-   explicit trust boundaries
-   authenticated encryption for secrets
-   no sensitive information in logs
-   strong auditability
-   supply-chain security

Security-relevant functionality shall include automated tests.

## 2.2 Test-driven development

TDD is mandatory.

The normal implementation workflow shall be:

1.  Red
2.  Green
3.  Refactor

Production functionality shall not be considered complete merely because
it works interactively.

Tests shall form part of the executable specification of the product.

Testing shall use:

-   TUnit
-   TUnit Mocks
-   integration testing
-   containerized infrastructure testing
-   Playwright where browser-level validation is required

External infrastructure boundaries may be mocked when appropriate.

Core domain behavior should generally be tested directly rather than
mocked.

## 2.3 Supportability

The product shall be diagnosable without requiring unrestricted shell
access to the host.

Every major subsystem shall expose meaningful health and diagnostic
information.

Users shall be able to generate:

-   human-readable diagnostics
-   a sanitized support package
-   sanitized AI troubleshooting context
-   machine-readable diagnostic JSON

Secrets shall never be required for routine troubleshooting.

## 2.4 Simple deployment

The preferred small-installation experience shall remain approximately:

``` bash
docker compose up -d
```

Kubernetes shall not be required and shall not influence core
architectural decisions.

## 2.5 Explicit privilege separation

The Blazor Manager shall not directly receive unrestricted host-level
privileges.

Privileged operations shall be performed by ZWarden Agent.

Primary trust chain:

``` text
Browser
   │
   ▼
ZWarden.Web
   │
   ▼
ZWarden.Agent
   │
   ▼
PZ Runtime
```

------------------------------------------------------------------------

# 3. Technology Baseline

## 3.1 Application platform

-   .NET 10
-   latest stable C# language version supported by the selected .NET 10
    SDK
-   ASP.NET Core
-   Blazor Web App
-   Interactive Server rendering where appropriate
-   Blazor Blueprint UI
-   SignalR
-   .NET Worker Service
-   System.Text.Json
-   Microsoft.Extensions.Configuration
-   Microsoft.Extensions.DependencyInjection
-   Microsoft.Extensions.Logging
-   OpenTelemetry
-   Auth0 Organizations or another OIDC-compatible identity provider for
    hosted SaaS
-   PostgreSQL as the preferred hosted SaaS database

------------------------------------------------------------------------

# 4. UI Technology

Primary UI framework:

**Blazor Blueprint UI**

The application shall minimize custom JavaScript.

JavaScript interop shall only be introduced where browser functionality
requires it, including possible uses such as:

-   clipboard interaction
-   browser downloads
-   WebAuthn/passkeys
-   specialized console interaction
-   functionality unavailable directly through Blazor

------------------------------------------------------------------------

# 5. Testing Technology

Primary testing framework:

**TUnit**

Mocking framework:

**TUnit Mocks**

Additional testing technologies may include:

-   ASP.NET Core WebApplicationFactory
-   Testcontainers
-   bUnit where compatible and valuable
-   Playwright for .NET
-   real SQLite databases
-   real PostgreSQL test containers
-   Docker integration environments

Pure unit testing shall not substitute for integration testing where
real infrastructure behavior is important.

Particularly important integration boundaries include:

-   Docker
-   SQLite
-   PostgreSQL
-   SignalR
-   SteamCMD
-   filesystem operations
-   RCON
-   backup/restore
-   container lifecycle

------------------------------------------------------------------------

# 6. Persistent Identifier Standard

ZWarden shall use **UUIDv7-based identifiers** for persistent domain
entities.

Identifiers shall be globally unique, time-ordered UUIDv7 values.

Raw UUIDs shall not normally be exposed directly to users, logs, APIs,
diagnostic output, or operational interfaces.

Instead, IDs shall use a short semantic prefix.

General format:

``` text
<prefix>-<UUIDv7>
```

Examples:

``` text
usr-019c...
agt-019c...
srv-019c...
```

The prefix communicates entity type while the UUIDv7 component provides
globally unique distributed identity.

------------------------------------------------------------------------

# 7. Canonical ID Prefixes

Initial namespace:

  Entity                   Prefix
  ------------------------ ---------
  User                     `usr-`
  Role                     `rol-`
  Agent                    `agt-`
  Server                   `srv-`
  Operation                `op-`
  Audit event              `aud-`
  Backup                   `bkp-`
  Diagnostic package       `diag-`
  Enrollment               `enr-`
  Configuration revision   `cfg-`
  Mod                      `mod-`
  Workshop item            `wsi-`
  Mod profile              `mdp-`
  Ban record               `ban-`
  Player record            `ply-`
  Permission assignment    `prm-`
  Certificate record       `crt-`
  Notification             `ntf-`

Additional prefixes may be added through an Architecture Decision
Record.

------------------------------------------------------------------------

# 7A. Multi-Tenancy and SaaS Domain Model

ZWarden shall model tenancy explicitly from the beginning.

Core concepts:

``` text
Tenant
TenantMembership
TenantInvitation
TenantSettings
Subscription
Agent
Server
```

Every tenant-owned record shall carry an immutable tenant scope,
directly or through a validated ownership relationship.

The browser shall never be trusted to select an arbitrary tenant ID.
Tenant context shall be derived from the authenticated session, selected
organization/workspace, and server-side authorization.

For hosted SaaS:

``` text
Auth0 Organization
        │
        ▼
ZWarden Tenant
        │
        ├── Memberships
        ├── Agents
        ├── Servers
        ├── Operations
        ├── Backups
        ├── Audit Events
        └── Diagnostics
```

The Auth0 organization identifier shall be stored as an external
identity-provider reference. ZWarden shall retain its own internal
tenant identifier:

``` text
ten-<UUIDv7>
```

Auth0 shall authenticate the human user and provide organization
context. ZWarden shall remain the authoritative source for tenant
membership, resource ownership, server permissions, and operational
authorization.

Self-hosted installations shall use the same tenant-aware domain model
even when the default installation contains only one tenant. This avoids
a later multi-tenancy rewrite.

Prefixes must:

-   remain short
-   remain unique
-   never be reused for a different entity type
-   use lowercase ASCII
-   be documented centrally

------------------------------------------------------------------------

# 8. ID Representation

The application shall use strongly typed IDs rather than passing
arbitrary strings throughout the domain model.

Conceptually:

``` text
UserId
AgentId
ServerId
OperationId
BackupId
```

shall be separate logical types.

An Agent ID and User ID shall therefore not be accidentally
interchangeable despite both internally containing UUIDv7 values.

Database storage may store:

-   native UUID values
-   textual prefixed IDs

depending upon measured database and portability considerations.

The public canonical representation shall remain:

``` text
agt-<UUIDv7>
```

rather than an untyped GUID.

Serialization and parsing shall validate the expected prefix.

For example:

``` text
AgentId.Parse("usr-...")
```

must fail.

------------------------------------------------------------------------

# 9. Database Architecture

Supported providers:

## Small installations

SQLite

## Larger installations

PostgreSQL

Database access:

-   Entity Framework Core
-   EF Core migrations
-   provider-compatible domain model
-   optimistic concurrency where appropriate

The application should avoid provider-specific database functionality
unless there is significant justification.

------------------------------------------------------------------------

# 10. Sensitive Data Protection

Secrets shall be encrypted at the application layer using authenticated
encryption.

A likely algorithm is AES-256-GCM, subject to current platform security
guidance at implementation time.

Encryption keys shall not be stored in the same database as encrypted
values.

Potential secret storage mechanisms include:

-   Docker secrets
-   protected filesystem secrets
-   host-managed secret stores

Future enterprise integrations may include:

-   HashiCorp Vault
-   Azure Key Vault
-   AWS KMS
-   compatible external KMS solutions

An abstraction such as:

``` text
ISecretProtector
```

shall isolate secret-storage implementation.

Passwords shall be securely hashed, not reversibly encrypted.

------------------------------------------------------------------------

# 11. Authentication

Authentication shall use an abstraction over the appropriate current
ASP.NET Core authentication primitives.

Self-hosted installations shall support local authentication.

Hosted SaaS shall support an OIDC-compatible identity provider, with
Auth0 Organizations as the initial preferred provider.

Required capabilities:

-   secure username/password authentication for local mode
-   strong password policy
-   MFA
-   recovery codes
-   account lockout / abuse protection
-   secure session handling
-   external identity mapping
-   tenant/organization context mapping

Desired capability:

-   passkeys/WebAuthn

The identity-provider abstraction shall prevent Auth0-specific types
from leaking into the Domain or Application layers.

Browser authentication should favor secure HttpOnly cookies rather than
exposing bearer tokens to browser code without a justified requirement.

------------------------------------------------------------------------

# 12. Authorization

Authorization shall be:

-   RBAC-enabled
-   policy based
-   permission based
-   tenant scoped
-   resource scoped
-   enforced server-side
-   enforced again at privileged Agent command boundaries

Roles alone shall not be sufficient. The final decision must include the
tenant, target resource, assigned permission, and applicable safety
rules.

Example permissions:

``` text
Server.View
Server.Start
Server.Stop
Server.Restart

Server.Configuration.View
Server.Configuration.Edit

Server.Mod.View
Server.Mod.Install
Server.Mod.Remove

Player.View
Player.Kick
Player.Ban
Player.Unban

Backup.View
Backup.Create
Backup.Restore
Backup.Delete

Console.View
Console.Execute

Agent.View
Agent.Manage

Audit.View
User.Manage
Role.Manage
```

Permissions may be scoped to individual servers.

Authorization shall be checked in application/business logic.

Hiding a UI control is not authorization.

------------------------------------------------------------------------

# 12A. RBAC and Permission Model

ZWarden shall support **role-based access control (RBAC)** combined with
fine-grained, policy-based, resource-scoped permissions.

Roles provide manageable permission bundles. Policies and resource
checks enforce the final decision.

The authorization decision shall conceptually evaluate:

``` text
Authenticated user
    + Tenant membership
    + Assigned role(s)
    + Permission
    + Target resource
    + Resource ownership/tenant scope
    + Operational safety rules
    = Allow or Deny
```

Auth0 roles and organization membership may be used as identity-provider
inputs for hosted SaaS, but ZWarden shall not rely on Auth0 as the sole
authorization store. This prevents the core product from being coupled
to one identity provider and allows self-hosted installations to use
local authentication.

## Built-in roles

  -----------------------------------------------------------------------
  Role                                Intended capability
  ----------------------------------- -----------------------------------
  Platform Owner                      SaaS-only platform administration;
                                      tenant support and platform
                                      operations

  Tenant Owner                        Full control of one tenant,
                                      including members, Agents, servers,
                                      settings, and security

  Administrator                       Broad operational administration
                                      within assigned servers

  Operator                            Routine server lifecycle, updates,
                                      backups, and approved configuration
                                      operations

  Moderator                           Limited player and server
                                      operations; may restart servers and
                                      apply approved mod changes when
                                      granted

  Viewer                              Read-only visibility

  Support/Diagnostics                 Time-limited, explicitly granted
                                      diagnostic access with no default
                                      mutating permissions
  -----------------------------------------------------------------------

The term **Moderator** is intentional. It refers to a human staff role
and must not be confused with Project Zomboid game modifications, which
shall be called **game mods** or **Workshop mods**.

## Moderator role

The default Moderator role shall be intentionally limited.

Typical permissions:

``` text
Server.View
Server.Health.View
Server.Log.View
Player.View
Player.Kick
Player.Ban
Player.Unban
Server.Restart
Mod.View
Mod.ApplyApprovedProfile
Mod.UpdateApproved
```

The default Moderator role shall not include:

``` text
Server.Stop
Server.Delete
Server.Configuration.Edit
Mod.InstallArbitrary
Mod.RemoveArbitrary
Backup.Restore
Console.Execute
Agent.Manage
User.Manage
Role.Manage
Tenant.Manage
```

A Tenant Owner or Administrator may create a custom role or adjust the
Moderator permission bundle, subject to security policy and audit
requirements.

High-risk permissions may require additional safeguards such as:

-   confirmation prompts
-   cooldowns
-   maintenance windows
-   two-person approval
-   step-up authentication
-   explicit server assignment
-   time-limited grants

## Permission naming

Permissions shall use stable, machine-readable names:

``` text
Server.View
Server.Start
Server.Stop
Server.Restart

Server.Configuration.View
Server.Configuration.Edit

Mod.View
Mod.Install
Mod.Remove
Mod.Update
Mod.ApplyApprovedProfile

Player.View
Player.Kick
Player.Ban
Player.Unban

Console.View
Console.Execute

Backup.View
Backup.Create
Backup.Restore
Backup.Delete

Agent.View
Agent.Manage

Tenant.View
Tenant.Manage
Tenant.Members.Manage
Tenant.Enrollment.Manage

User.Manage
Role.Manage
Audit.View
Diagnostics.View
Diagnostics.Export
```

Authorization shall be enforced in application/business logic and at the
Agent command boundary. Hiding a UI control is never sufficient.

# 13. Primary Application Components

The product shall consist of three first-class software components.

## ZWarden.Web

Responsibilities:

-   Blazor UI
-   authentication
-   authorization
-   API
-   persistence
-   operation coordination
-   SignalR endpoints
-   audit logging
-   diagnostics
-   user administration

## ZWarden.Agent

Responsibilities:

-   host-level server management
-   Docker integration
-   native runtime support
-   RCON
-   SteamCMD
-   filesystem management
-   backups
-   mod operations
-   runtime health
-   log collection
-   diagnostics

## ZWarden.PZServer

Responsibilities:

-   canonical Project Zomboid runtime
-   SteamCMD integration
-   standardized filesystem
-   predictable startup behavior
-   health integration
-   canonical configuration conventions

------------------------------------------------------------------------

# 14. Recommended Solution Layout

``` text
ZWarden.sln

src/
├── ZWarden.Domain/
├── ZWarden.Application/
├── ZWarden.Infrastructure/
├── ZWarden.Contracts/
├── ZWarden.Web/
├── ZWarden.Agent/
├── ZWarden.Rcon/
├── ZWarden.Diagnostics/
└── ZWarden.PZServer/

tests/
├── ZWarden.Domain.Tests/
├── ZWarden.Application.Tests/
├── ZWarden.Infrastructure.Tests/
├── ZWarden.Agent.Tests/
├── ZWarden.Rcon.Tests/
├── ZWarden.Security.Tests/
├── ZWarden.IntegrationTests/
└── ZWarden.EndToEndTests/
```

------------------------------------------------------------------------

# 15. Architectural Dependency Rules

Domain shall depend upon no infrastructure frameworks.

Application may depend upon Domain.

Infrastructure may implement interfaces defined by Application/Domain.

Web may compose Application and Infrastructure.

Agent may consume Contracts and Agent-specific abstractions.

Forbidden domain dependencies include:

``` text
EF Core
ASP.NET Core
Docker
SignalR
SteamCMD
filesystem-specific implementations
```

Architecture tests shall enforce dependency boundaries.

------------------------------------------------------------------------

# 16. Manager ↔ Agent Protocol

Agent connections shall be initiated outbound from Agent to Manager.

Preferred transport:

``` text
HTTPS + WSS
```

Preferred real-time technology:

SignalR

Conceptual path:

``` text
Agent
   │
   └──────── WSS ────────► Manager
```

Agents should not require publicly exposed inbound management ports.

------------------------------------------------------------------------

# 17. Agent Authentication

Production Agent authentication shall support mutual TLS.

Enrollment shall use one-time enrollment credentials.

The design shall include:

-   certificate issuance
-   renewal
-   rotation
-   expiration
-   revocation
-   Agent disablement
-   lost-Agent recovery

------------------------------------------------------------------------

# 18. Agent Protocol Messages

Contracts shall be strongly typed.

Representative messages:

``` text
AgentHello
AgentHeartbeat
AgentStateSnapshot

CommandEnvelope<T>
OperationProgress
OperationCompleted

ServerStateChanged
PlayerConnected
PlayerDisconnected
HealthChanged
LogEntry
```

Protocol messages shall include applicable metadata such as:

``` text
ProtocolVersion
MessageId
AgentId
ServerId
OperationId
Timestamp
```

Arbitrary remote shell execution shall not exist.

------------------------------------------------------------------------

# 19. Command Safety

The Agent shall expose domain-specific actions.

Allowed concept:

``` text
RestartServer
StopServer
UpdateServer
KickPlayer
BanPlayer
InstallWorkshopItem
CreateBackup
RestoreBackup
```

Forbidden concept:

``` text
ExecuteShellCommand(string command)
```

The Agent may internally invoke required operating-system processes, but
commands shall be constructed from trusted application code and
validated parameters.

------------------------------------------------------------------------

# 20. Operations

Mutating server actions shall execute as durable operations.

Each operation shall have:

``` text
OperationId
AgentId
ServerId
OperationType
RequestedBy
RequestedAt
State
Progress
Result
```

Operation IDs shall use:

``` text
op-<UUIDv7>
```

Operations shall support idempotency.

Duplicate receipt of the same operation ID shall not cause a destructive
operation to execute twice.

------------------------------------------------------------------------

# 21. Server Operation Concurrency

Only one conflicting mutating lifecycle operation may execute against a
server at a time.

Examples of mutually exclusive operations include:

-   restart
-   update
-   backup restore
-   mod mutation
-   major configuration application

Read-only operations may execute concurrently.

------------------------------------------------------------------------

# 22. Canonical Managed PZ Container

ZWarden shall distribute and support its own Project Zomboid Docker
image.

The image shall provide:

-   SteamCMD
-   lifecycle scripts/tooling
-   standardized filesystem conventions
-   health checks
-   required runtime dependencies
-   predictable process supervision
-   integration points for ZWarden.Agent

Project Zomboid dedicated server files should normally be obtained from
Steam through SteamCMD rather than embedded into the distributable image
unless redistribution rights and operational requirements clearly
support another approach.

------------------------------------------------------------------------

# 23. Canonical PZ Filesystem

Logical filesystem layout:

``` text
/pz/
├── server/
├── runtime/
└── data/
    ├── config/
    ├── saves/
    ├── logs/
    ├── workshop/
    └── backups/
```

Exact physical implementation may evolve, but the logical contract shall
remain stable.

The Agent shall abstract underlying Steam and Project Zomboid filesystem
details from Manager.

------------------------------------------------------------------------

# 24. Container Security

Containers shall default toward:

-   non-root users
-   minimal Linux image
-   reduced capabilities
-   `no-new-privileges`
-   explicit writable mounts
-   read-only root filesystem where practical
-   restricted internal networks
-   resource limits where appropriate
-   pinned production image references
-   health checks

The PZ server shall not have access to the Manager database network.

------------------------------------------------------------------------

# 25. Container Labels

Canonical managed containers shall include labels similar to:

``` text
io.zwarden.managed=true
io.zwarden.server-id=srv-...
io.zwarden.agent-id=agt-...
io.zwarden.schema-version=1
io.zwarden.runtime=project-zomboid
```

Agents shall only manage containers explicitly assigned to them.

Adoption of existing containers shall require an explicit import
workflow.

------------------------------------------------------------------------

# 26. Container Networks

The reference deployment shall isolate traffic into logical networks.

Conceptually:

``` text
frontend
control
gameserver
data
```

Example:

``` text
Internet
   │
   ▼
Caddy
   │ frontend
   ▼
ZWarden.Web
   │ control
   ▼
ZWarden.Agent
   │ gameserver
   ▼
ZWarden.PZServer
```

PostgreSQL shall reside only on the data network required by
ZWarden.Web.

------------------------------------------------------------------------

# 27. Docker Engine Access

Raw Docker socket access represents effectively privileged host access.

The architecture shall minimize exposure.

Preferred long-term architecture:

``` text
ZWarden.Agent
   │
   ▼
restricted Docker API/socket proxy
   │
   ▼
Docker Engine
```

The Agent shall never become a generic Docker administration interface.

------------------------------------------------------------------------

# 28. Project Zomboid Ports

The product shall explicitly model:

-   Project Zomboid gameplay ports
-   Steam/query requirements
-   RCON
-   additional player ports where required

Only ports necessary for gameplay or deliberate administrative exposure
shall be host-published.

RCON shall remain private by default.

Exact port requirements shall be verified against the targeted current
Project Zomboid release during implementation.

------------------------------------------------------------------------

# 29. RCON Architecture

RCON shall terminate at Agent.

Preferred flow:

``` text
Browser
   │
   ▼
ZWarden.Web
   │
   ▼
ZWarden.Agent
   │ private network
   ▼
PZ RCON
```

Browser shall never receive:

-   RCON password
-   internal RCON credential material

Manager should preferably not store RCON credentials for managed local
deployments.

RCON credentials should be locally owned by Agent wherever practical.

------------------------------------------------------------------------

# 30. RCON Library

RCON protocol implementation shall be isolated from Agent orchestration.

Suggested logical project:

``` text
ZWarden.Rcon
```

Responsibilities:

-   connect
-   authenticate
-   execute supported command
-   parse response
-   timeout
-   reconnect
-   health detection
-   error categorization

------------------------------------------------------------------------

# 31. SteamCMD Lifecycle

PZ Server installation shall use an explicit lifecycle.

``` text
Missing
  ↓
Installing
  ↓
Validating
  ↓
Installed
  ↓
Ready
```

Update process:

``` text
Notify players
   ↓
Save world
   ↓
Create backup
   ↓
Stop server
   ↓
SteamCMD update
   ↓
Validate installation
   ↓
Update required Workshop content
   ↓
Start server
   ↓
Health validation
   ↓
Ready
```

Failures shall produce durable operation errors rather than leaving the
server in an ambiguous state.

------------------------------------------------------------------------

# 32. Project Zomboid Configuration

The Manager shall support structured management of applicable
configuration including:

-   server INI
-   Sandbox configuration
-   spawn configuration
-   supported administrative configuration
-   mod configuration

Raw text editing may be provided as an advanced capability.

Structured editing is preferred.

------------------------------------------------------------------------

# 33. Configuration Revisions

Every meaningful configuration mutation shall create a revision.

Identifier:

``` text
cfg-<UUIDv7>
```

Revision metadata shall include:

-   server
-   user
-   timestamp
-   previous state
-   resulting state
-   relevant operation
-   reason where appropriate

Writes shall use safe atomic patterns.

------------------------------------------------------------------------

# 34. Mod Management

Workshop item identity and Project Zomboid Mod ID shall be modeled
separately.

The system shall support:

-   Workshop ID
-   Mod ID
-   metadata
-   enabled state
-   server assignment
-   ordering
-   update state
-   dependencies where discoverable
-   compatibility state where discoverable

------------------------------------------------------------------------

# 35. Mod Profiles

Users should eventually be able to create reusable mod profiles.

Example:

``` text
Vanilla
Lightly Modded
Hardcore
Testing
Production
```

Profiles may be applied or cloned across compatible servers.

------------------------------------------------------------------------

# 36. Player Management

Manager shall support, where available through supported PZ
administrative mechanisms:

-   current player list
-   player connection state
-   kick
-   ban
-   unban
-   whitelist
-   administrative access management
-   server messaging

Actions shall be permission controlled and audited.

------------------------------------------------------------------------

# 37. Remote Console

The product shall provide a web-based administrative console.

The console shall use:

``` text
Browser
  ↓ SignalR
ZWarden.Web
  ↓ SignalR
ZWarden.Agent
  ↓ RCON
PZ
```

Raw RCON console access shall require a higher privilege than structured
player/server operations.

Commands shall be audited.

------------------------------------------------------------------------

# 38. Live Log Streaming

Agent shall support live log streaming.

Streaming shall be subscription based.

Raw log traffic should not be transmitted for all servers continuously
when nobody is consuming it.

Important structured events may be transmitted independently.

Logs shall be treated as untrusted data.

------------------------------------------------------------------------

# 39. Health Model

A running Docker container shall not automatically mean that the PZ
server is healthy.

Health probes shall cover:

-   container health
-   process health
-   startup health
-   network health
-   RCON health
-   application health

Aggregate states may include:

``` text
Unknown
Starting
Healthy
Degraded
Unhealthy
Stopping
Stopped
Updating
BackingUp
Restoring
Failed
```

------------------------------------------------------------------------

# 40. Agent Heartbeats

Agents shall send periodic heartbeat/state data.

Potential information includes:

-   Agent version
-   protocol version
-   hostname alias
-   OS
-   CPU architecture
-   Docker availability
-   uptime
-   available disk
-   available memory
-   managed server states
-   current operations

Manager shall differentiate Agent Offline from Server Offline.

------------------------------------------------------------------------

# 41. Agent Reconnection

SignalR disconnects shall be expected.

After reconnect, Agent shall provide an authoritative state snapshot.

The synchronization process shall resolve:

-   server state
-   players
-   active operations
-   PZ versions
-   mod state
-   health state

Runtime Agent state is authoritative over stale Manager assumptions.

------------------------------------------------------------------------

# 42. Backups

ZWarden shall provide first-class backup support.

Backup ID:

``` text
bkp-<UUIDv7>
```

Triggers:

-   manual
-   scheduled
-   pre-update
-   pre-mod-change
-   pre-restore
-   pre-major-configuration-change

Backup metadata shall include:

-   BackupId
-   ServerId
-   timestamp
-   trigger/reason
-   PZ version
-   configuration revision
-   mod revision where applicable
-   world identity
-   file size
-   checksum

------------------------------------------------------------------------

# 43. Backup Integrity

Backup artifacts shall include cryptographic integrity verification.

SHA-256 is acceptable as an initial integrity checksum.

Restore flow:

``` text
Select backup
   ↓
Verify checksum
   ↓
Validate archive contents
   ↓
Validate paths
   ↓
Check disk capacity
   ↓
Create protective backup
   ↓
Stop server
   ↓
Restore to staging
   ↓
Validate
   ↓
Swap into place
   ↓
Start
   ↓
Health validation
```

Archive extraction shall be protected against directory traversal / Zip
Slip-style attacks.

------------------------------------------------------------------------

# 44. Backup Destinations

Initial:

-   local mounted storage

Future optional destinations:

-   S3-compatible object storage
-   SMB
-   NFS

Remote providers shall remain optional extensions and shall not
complicate the baseline installation.

------------------------------------------------------------------------

# 45. HTTPS / Reverse Proxy

The reference deployment shall use Caddy unless future verification
identifies a superior fit.

Responsibilities:

-   HTTPS termination
-   ACME
-   Let's Encrypt
-   HTTP-01
-   automatic renewal
-   HTTP-to-HTTPS redirect
-   WebSocket forwarding

ZWarden.Web shall not need direct Internet exposure.

------------------------------------------------------------------------

# 46. TLS Deployment Modes

Reference modes:

## Public

Public DNS hostname with Let's Encrypt.

## Private

Internal certificate authority / locally trusted TLS.

## Existing reverse proxy

User-provided ingress and certificate management.

Future advanced mode:

-   ACME DNS-01

------------------------------------------------------------------------

# 47. Audit Logging

Security and administrative actions shall be auditable.

Audit event ID:

``` text
aud-<UUIDv7>
```

Audit information may include:

-   user
-   action
-   server
-   target entity
-   timestamp
-   result
-   operation
-   source context
-   reason if applicable

Normal users shall not be permitted to alter audit history.

------------------------------------------------------------------------

# 48. Logging

Logging shall use structured logging.

Secrets shall never intentionally enter logs.

Examples of prohibited logging:

-   passwords
-   RCON secrets
-   private keys
-   access tokens
-   session cookies
-   MFA secrets
-   encryption keys
-   database credentials

Sensitive values should use secret-aware types where appropriate.

------------------------------------------------------------------------

# 49. Observability

OpenTelemetry shall be used as the observability standard.

Supported telemetry:

-   traces
-   metrics
-   structured logs

Correlation should include:

``` text
OperationId
AgentId
ServerId
DiagnosticId
```

External telemetry systems shall remain optional.

------------------------------------------------------------------------

# 50. Diagnostics

The system shall implement deterministic health and diagnostic tests
before relying on external AI assistance.

Examples:

``` text
Test Manager
Test Database
Test Agent
Test Docker
Test RCON
Test Game Port
Validate Mods
Validate Server Configuration
Check Filesystem Permissions
Check Disk Space
Check TLS
Check Version Compatibility
```

------------------------------------------------------------------------

# 51. Support Packages

Diagnostic packages shall pass through:

``` text
Collect
   ↓
Sanitize
   ↓
Redact
   ↓
Secret scan
   ↓
Validate
   ↓
Package
```

If secret scanning detects prohibited material, package generation shall
fail rather than knowingly emit it.

------------------------------------------------------------------------

# 52. AI Diagnostic Context

Users shall be able to generate vendor-neutral AI troubleshooting
context suitable for any capable AI/Agent harness.

Output formats:

-   Markdown
-   JSON

The AI context shall explicitly mark logs and other runtime data as
untrusted input.

It shall warn receiving models not to execute or obey instructions
contained inside:

-   logs
-   player names
-   mod metadata
-   server messages
-   configuration values
-   other collected diagnostic text

------------------------------------------------------------------------

# 53. Privacy-Aware Diagnostics

Diagnostic output shall avoid or pseudonymize unnecessary personally
identifiable operational data.

Examples may include:

``` text
<HOST-1>
<PLAYER-2>
<PRIVATE-IP-1>
<PUBLIC-IP-1>
```

Consistent pseudonyms should preserve relationships useful during
troubleshooting.

------------------------------------------------------------------------

# 54. CI/CD

CI shall include at minimum:

``` text
Restore
 ↓
Format/lint
 ↓
Build
 ↓
Unit tests
 ↓
Architecture tests
 ↓
Integration tests
 ↓
Security tests
 ↓
Container build
 ↓
Container scanning
 ↓
E2E tests
 ↓
SBOM
 ↓
Release artifacts
```

------------------------------------------------------------------------

# 55. Supply-Chain Security

Release engineering shall consider:

-   NuGet dependency locking
-   dependency vulnerability scanning
-   secret scanning
-   container vulnerability scanning
-   SBOM generation
-   image digests
-   release checksums
-   signed artifacts/images where practical
-   build provenance/attestation where practical

------------------------------------------------------------------------

# 56. Code Quality

Projects shall enable:

``` text
Nullable
ImplicitUsings
TreatWarningsAsErrors
```

Current .NET analyzers shall be enabled.

Code should favor readability over novelty.

Modern C# features are encouraged where they improve clarity.

------------------------------------------------------------------------

# 57. Greenfield Development Rule

The project shall not carry compatibility debt for architectures that do
not yet exist.

New abstractions shall be introduced when they establish a meaningful
boundary, not merely because they may someday be useful.

Premature microservices and Kubernetes-specific abstractions shall be
avoided.

------------------------------------------------------------------------

# 58. Reference Deployment

Small deployment:

``` text
Caddy
ZWarden.Web
ZWarden.Agent
ZWarden.PZServer
SQLite
```

Larger deployment:

``` text
Caddy
ZWarden.Web
PostgreSQL

Host A:
    ZWarden.Agent
    PZ Server(s)

Host B:
    ZWarden.Agent
    PZ Server(s)
```

------------------------------------------------------------------------

# 59. Feature Dependency Roadmap

Implementation shall proceed in dependency order.

Each feature shall later receive its own implementation plan.

Each implementation plan shall be decomposed into small work packages
intended to fit comfortably within a single development/Agent context.

As a planning guardrail, feature work packages should generally remain
substantially below approximately 100,000 tokens of total
implementation/debugging context.

If a plan cannot reasonably fit that boundary, it shall be split further
before implementation begins.

## Feature 0 --- Repository and Engineering Foundation

**Dependencies:** None.

Deliverables:

-   repository
-   solution structure
-   project structure
-   .NET 10 SDK pinning
-   compiler configuration
-   analyzers
-   `.editorconfig`
-   warnings-as-errors
-   CI skeleton
-   TUnit setup
-   TUnit Mocks setup
-   test conventions
-   Architecture Decision Record framework
-   contribution/development documentation

**Exit condition:** Clean build and test pipeline from an empty
baseline.

## Feature 1 --- Domain Foundation and Typed UUIDv7 IDs

**Dependencies:** Feature 0.

Deliverables:

-   UUIDv7 generation
-   prefixed ID specification
-   strongly typed domain IDs
-   parsing
-   formatting
-   serialization
-   EF conversion strategy
-   validation
-   ID prefix registry
-   comprehensive tests

Representative IDs:

``` text
usr-...
agt-...
srv-...
op-...
```

**Exit condition:** Entity identifiers can be generated, persisted,
serialized, parsed, and rejected when used with the wrong entity type.

## Feature 2 --- Persistence Foundation

**Dependencies:** Features 0--1.

Deliverables:

-   EF Core infrastructure
-   SQLite provider
-   PostgreSQL provider
-   migrations
-   database initialization
-   migration execution strategy
-   concurrency primitives
-   integration tests for both providers

**Exit condition:** Same core persistence test suite passes against
SQLite and PostgreSQL.

## Feature 3 --- Security and Cryptography Foundation

**Dependencies:** Features 0--2.

Deliverables:

-   secret abstractions
-   authenticated encryption
-   encryption-key loading
-   secure configuration handling
-   secret-aware logging types
-   redaction primitives
-   security tests

**Exit condition:** Sensitive values can be securely stored and cannot
accidentally stringify into logs.

## Feature 3A --- Tenant and Multi-Tenancy Foundation

**Dependencies:** Features 0--3.

Deliverables:

-   Tenant entity
-   tenant-scoped identifiers
-   tenant context abstraction
-   tenant ownership rules
-   tenant-aware repositories
-   tenant isolation tests
-   single-tenant self-hosted default

**Exit condition:** Every tenant-owned operation is scoped and
cross-tenant access is denied by default.

## Feature 3B --- Identity Provider Abstraction and Auth0 Integration

**Dependencies:** Feature 3A.

Deliverables:

-   local identity abstraction
-   OIDC abstraction
-   Auth0 Organizations integration
-   external subject mapping
-   organization-to-tenant mapping
-   login context validation
-   identity-provider integration tests

**Exit condition:** A human can authenticate through local mode or the
selected OIDC provider and resolve a valid ZWarden tenant context.

## Feature 3C --- RBAC and Tenant Authorization

**Dependencies:** Features 3A--3B.

Deliverables:

-   roles
-   permissions
-   role assignments
-   tenant-scoped membership
-   resource authorization
-   built-in Moderator role
-   custom role support
-   policy handlers
-   authorization tests

**Exit condition:** Tenant and server permissions are enforced
independently of the UI and external identity provider.

## Feature 3D --- Tenant Administration and Invitations

**Dependencies:** Feature 3C.

Deliverables:

-   tenant creation
-   tenant settings
-   membership management
-   invitations
-   membership removal
-   role assignment
-   tenant security settings
-   audit integration

**Exit condition:** Authorized tenant owners can manage membership and
roles without cross-tenant leakage.

## Feature 4 --- Authentication

**Dependencies:** Features 2--3B.

Deliverables:

-   Identity integration
-   user management
-   login/logout
-   password policies
-   secure cookies
-   account recovery
-   MFA
-   authentication audit events

**Exit condition:** Secure local account lifecycle is functional and
tested.

## Feature 5 --- Authorization and Resource Permissions

**Dependencies:** Features 3A--3C and 4.

Deliverables:

-   permission definitions
-   roles
-   policy authorization
-   server-scoped authorization model
-   resource authorization handlers
-   authorization tests

**Exit condition:** Permissions are enforced server-side regardless of
UI visibility.

## Feature 6 --- Audit System

**Dependencies:** Features 2, 4, 5.

Deliverables:

-   audit entity
-   audit writer
-   immutable/append-oriented semantics
-   administrative audit viewer
-   correlation IDs
-   filtering

**Exit condition:** Security-sensitive operations have durable audit
records.

## Feature 7 --- Manager/Agent Contracts

**Dependencies:** Features 0--1.

Deliverables:

-   protocol version
-   message envelopes
-   commands
-   events
-   heartbeat contract
-   state snapshot
-   operation progress
-   serialization tests
-   compatibility rules

**Exit condition:** Manager and Agent can share versioned strongly typed
contracts without runtime implementation dependencies.

## Feature 8 --- Agent Runtime Skeleton

**Dependencies:** Feature 7.

Deliverables:

-   .NET Worker Service
-   Agent identity
-   configuration
-   lifecycle
-   local persistence where required
-   health state
-   graceful shutdown
-   diagnostic hooks

**Exit condition:** Agent can start, identify itself, report local
health, and shut down safely.

## Feature 9 --- Agent Enrollment and Trust

**Dependencies:** Features 3, 7, 8, 3D.

Deliverables:

-   enrollment tokens
-   Agent registration
-   certificate/credential issuance
-   credential storage
-   revocation
-   rotation architecture
-   trust tests

**Exit condition:** Unknown Agents cannot connect without an authorized
enrollment process.

## Feature 10 --- SignalR Agent Control Plane

**Dependencies:** Features 7--9.

Deliverables:

-   Agent hub
-   outbound Agent connection
-   authentication
-   reconnect
-   heartbeat
-   state snapshot
-   protocol negotiation
-   connection monitoring

**Exit condition:** Manager reliably detects Agent online/offline state
and Agent resynchronizes after reconnect.

## Feature 10A --- SaaS Agent Enrollment and Tenant-Scoped Connectivity

**Dependencies:** Features 3A--3D and 7--10.

Deliverables:

-   one-time enrollment credentials
-   enrollment expiration and revocation
-   Agent registration
-   Agent-to-tenant binding
-   Agent credential issuance
-   outbound SaaS connectivity
-   tenant-scoped SignalR connection
-   reconnect and state resynchronization
-   enrollment audit events

**Exit condition:** A customer can register an Agent from the hosted
portal and manage only the servers assigned to that Agent and tenant.

## Feature 11 --- Durable Operations Engine

**Dependencies:** Features 2, 6, 10.

Deliverables:

-   operation entity
-   operation queue
-   operation state machine
-   UUIDv7 operation IDs
-   progress reporting
-   idempotency
-   cancellation rules
-   per-server locking
-   failure semantics

**Exit condition:** A test operation can survive disconnect/reconnect
without duplicate execution.

## Feature 12 --- Canonical PZ Container Foundation

**Dependencies:** Feature 0.

Deliverables:

-   PZ container Dockerfile
-   base Linux environment
-   non-root runtime
-   SteamCMD
-   canonical filesystem
-   persistent volumes
-   container labels
-   startup tooling
-   base health check

**Exit condition:** A clean container can install and launch a vanilla
Project Zomboid server reproducibly.

## Feature 13 --- Agent Docker Runtime

**Dependencies:** Features 8, 11, 12.

Deliverables:

-   Docker API abstraction
-   container discovery
-   canonical-label validation
-   inspect/start/stop/restart
-   allowed-container enforcement
-   Docker health diagnostics

**Exit condition:** Agent can manage only explicitly assigned ZWarden PZ
containers.

## Feature 14 --- Server Registration and Inventory

**Dependencies:** Features 2, 5, 10, 13, tenant scope.

Deliverables:

-   Server domain entity
-   Agent/server association
-   discovery
-   import/register
-   server metadata
-   dashboard inventory

**Exit condition:** Manager displays registered servers and
authoritative runtime state.

## Feature 15 --- Basic Server Lifecycle

**Dependencies:** Features 11, 13, 14.

Deliverables:

-   start
-   stop
-   restart
-   lifecycle progress
-   safe timeout handling
-   audit integration
-   authorization
-   UI

**Exit condition:** Authorized users can safely control server lifecycle
through durable operations.

## Feature 16 --- Health and Observability

**Dependencies:** Features 10, 14, 15.

Deliverables:

-   hierarchical health model
-   container probe
-   process probe
-   startup probe
-   network probe
-   OpenTelemetry baseline
-   runtime metrics
-   UI status visualization

**Exit condition:** Manager can distinguish stopped, starting, healthy,
degraded, and failed states.

## Feature 17 --- SteamCMD Lifecycle

**Dependencies:** Features 11--16.

Deliverables:

-   installation state machine
-   update
-   validate
-   version detection
-   failure recovery
-   operation progress
-   audit integration

**Exit condition:** PZ server installation and updates are reproducible
and observable.

## Feature 18 --- RCON Foundation

**Dependencies:** Features 12, 14, 16.

Deliverables:

-   RCON library
-   authentication
-   command/response
-   timeout
-   reconnect
-   Agent-only secret ownership
-   RCON health probe

**Exit condition:** Agent can securely communicate with PZ over an
internal network.

## Feature 19 --- Player Management

**Dependencies:** Features 5, 6, 18, Moderator RBAC policies.

Deliverables:

-   player enumeration
-   connect/disconnect events where possible
-   kick
-   ban
-   unban
-   whitelist actions where supported
-   granular permissions
-   audit records
-   UI

**Exit condition:** Authorized moderation operations work without
exposing raw RCON credentials.

## Feature 20 --- Configuration Management

**Dependencies:** Features 11, 14, 15.

Deliverables:

-   INI parsing
-   supported Lua/config parsing
-   structured model
-   validation
-   atomic writes
-   config revisions
-   restore revision
-   advanced raw view/edit where appropriate

**Exit condition:** Configuration can be safely changed without blind
file replacement.

## Feature 21 --- Workshop and Mod Discovery

**Dependencies:** Features 17, 20.

Deliverables:

-   Workshop item model
-   Mod ID model
-   metadata discovery
-   Workshop→Mod mapping
-   installed-state detection
-   compatibility diagnostics where possible

**Exit condition:** System correctly distinguishes Steam Workshop item
IDs from Project Zomboid Mod IDs.

## Feature 22 --- Mod Management

**Dependencies:** Features 11, 17, 20, 21.

Deliverables:

-   install
-   remove
-   enable
-   disable
-   reorder
-   update
-   configuration synchronization
-   safe server restart coordination

**Exit condition:** Users can modify a server's mod set through a
controlled operation.

## Feature 23 --- Mod Profiles

**Dependencies:** Feature 22.

Deliverables:

-   profile creation
-   profile editing
-   assignment
-   cloning
-   comparison/diff
-   safe application

**Exit condition:** Reusable mod sets can be applied to compatible
servers.

## Feature 24 --- Backup Foundation

**Dependencies:** Features 11, 14, 20.

Deliverables:

-   backup model
-   local destination
-   checksums
-   backup operation
-   retention metadata
-   automatic pre-operation backup API

**Exit condition:** A consistent server backup can be created and
cryptographically verified.

## Feature 25 --- Restore

**Dependencies:** Feature 24.

Deliverables:

-   restore validation
-   archive safety
-   staging
-   protective backup
-   atomic restore
-   health verification
-   failure handling

**Exit condition:** A known-good backup can be restored without unsafe
extraction or uncontrolled overwrite.

## Feature 26 --- Automated Backup Scheduling

**Dependencies:** Features 24--25.

Deliverables:

-   recurring schedules
-   retention policies
-   pruning
-   scheduled operation coordination
-   audit history

**Exit condition:** Backups can run safely on schedules without
colliding with lifecycle operations.

## Feature 27 --- Live Logs

**Dependencies:** Features 10, 14, 16.

Deliverables:

-   log ingestion
-   structured log events
-   SignalR subscriptions
-   filters
-   tail
-   bounded buffering
-   output sanitization

**Exit condition:** Users can view live logs without transmitting every
server log continuously.

## Feature 28 --- Remote Administrative Console

**Dependencies:** Features 5, 6, 18, 27, elevated console permission.

Deliverables:

-   console page
-   RCON command transport
-   streaming output
-   command authorization
-   history where appropriate
-   auditing
-   input safety

**Exit condition:** Privileged users can interact with RCON without
receiving RCON credentials.

## Feature 29 --- Diagnostics Engine

**Dependencies:** Features 3, 14, 16--28.

Deliverables:

-   health checks
-   DB diagnostics
-   Docker diagnostics
-   Agent diagnostics
-   RCON diagnostics
-   filesystem diagnostics
-   SteamCMD diagnostics
-   mod diagnostics
-   TLS diagnostics
-   compatibility diagnostics

**Exit condition:** Common failure modes can be diagnosed from within
the application.

## Feature 30 --- Sanitized Support Package

**Dependencies:** Feature 29.

Deliverables:

-   diagnostic collector
-   redaction
-   pseudonymization
-   secret scanner
-   manifest
-   ZIP output
-   validation
-   DiagnosticId

**Exit condition:** A useful support package can be generated with
automated secret-leak prevention.

## Feature 31 --- AI Troubleshooting Context

**Dependencies:** Feature 30.

Deliverables:

-   Markdown export
-   JSON export
-   schema version
-   diagnostic summary
-   untrusted-data delimiters
-   prompt-injection warning
-   safe diagnostic-action vocabulary
-   copy-to-clipboard workflow

**Exit condition:** A user can safely paste self-contained diagnostic
context into an external AI Agent.

## Feature 32 --- HTTPS Reference Deployment

**Dependencies:** Features 4, 10.

Deliverables:

-   Caddy configuration
-   HTTPS
-   HTTP redirect
-   HTTP-01
-   WebSocket forwarding
-   certificate lifecycle
-   public deployment documentation
-   private deployment documentation

**Exit condition:** Reference deployment can operate securely over HTTPS
with minimal configuration.

## Feature 33 --- First-Run Setup

**Dependencies:** Features 4, 5, 9, 14, 32.

Deliverables:

-   first administrator
-   TLS mode
-   Agent enrollment
-   PZ server discovery
-   server registration
-   initial health checks
-   setup completion state

**Exit condition:** A new user can move from fresh Compose deployment to
functioning server management through guided setup.

## Feature 33A --- Hosted SaaS Deployment

**Dependencies:** Features 3A--3D, 10A, 14, 16, 29--33.

Deliverables:

-   multi-tenant hosted deployment
-   PostgreSQL deployment
-   Auth0 configuration
-   tenant isolation verification
-   hosted Agent enrollment
-   SaaS operational limits
-   platform administration
-   tenant-aware diagnostics
-   hosted HTTPS and WebSocket configuration

**Exit condition:** Multiple independent tenants can securely use one
hosted ZWarden control plane while their Agents and servers remain
isolated.

## Feature 34 --- Complete Docker Compose Distribution

**Dependencies:** Features 12--33.

Deliverables:

-   reference Compose
-   persistent volumes
-   networks
-   Caddy
-   ZWarden.Web
-   ZWarden.Agent
-   ZWarden.PZServer
-   SQLite mode
-   PostgreSQL mode
-   secrets bootstrap
-   health dependencies
-   upgrade documentation

**Exit condition:** Fresh installation works through the supported
documented deployment process.

## Feature 35 --- Remote Agent / Multi-Host Support

**Dependencies:** Features 9--16, 32.

Deliverables:

-   remote Agent installation
-   Agent inventory
-   host health
-   server assignment
-   certificate lifecycle
-   WAN reconnect behavior
-   host-scoped permissions where appropriate

**Exit condition:** One Manager can securely operate PZ servers across
multiple hosts.

## Feature 36 --- Multi-Server Operational UX

**Dependencies:** Features 14--35.

Deliverables:

-   dashboard
-   fleet status
-   bulk-read operations
-   server filtering
-   multi-server alerts
-   operational summaries

Mutating bulk operations shall receive additional safety design before
implementation.

**Exit condition:** Multiple servers can be efficiently observed and
managed.

## Feature 37 --- Notifications

**Dependencies:** Features 11, 16, 29.

Potential capabilities:

-   backup failure
-   Agent offline
-   PZ crash
-   update failure
-   certificate issue
-   disk-space warning
-   mod failure

External notification providers shall be modular.

## Feature 38 --- Native PZ Runtime Support

**Dependencies:** Stable Docker-based feature set.

Deliverables:

-   native process abstraction
-   systemd integration where applicable
-   filesystem discovery
-   native update workflow
-   privilege restrictions

This feature intentionally comes after the canonical Docker
implementation so that third-party/native variability does not dictate
the initial architecture.

## Feature 39 --- Existing Third-Party Docker Adoption

**Dependencies:** Stable canonical Docker feature set.

Deliverables:

-   discovery
-   explicit adoption
-   capability detection
-   mapping external paths/settings
-   compatibility warnings
-   limited-support status where appropriate

The canonical ZWarden PZ container remains the preferred deployment.

## Feature 40 --- Production Hardening and 1.0 Release Gate

**Dependencies:** All required 1.0 features.

Required review:

-   OWASP Top 10 assessment
-   OWASP ASVS assessment
-   threat-model review
-   authentication review
-   authorization review
-   encryption review
-   secrets review
-   Docker privilege review
-   backup/restore security review
-   dependency scan
-   container scan
-   fuzz/input testing where useful
-   penetration-style internal tests
-   disaster-recovery test
-   migration/upgrade test
-   fresh-install test
-   documentation review
-   SBOM
-   signed release artifacts where supported

**Exit condition:** Release satisfies documented security and
reliability gates.

------------------------------------------------------------------------

# 60. Feature Mini-Plan Standard

Before implementing any roadmap feature, a dedicated mini-plan shall be
created.

Each mini-plan shall contain:

## Objective

Exactly what capability is being added.

## Dependencies

Previously completed features or required abstractions.

## Scope

Explicit in-scope behavior.

## Non-scope

Explicitly deferred behavior.

## Domain changes

Entities, value objects, services, state machines, and invariants.

## Contract changes

API, Agent, SignalR, persistence, and serialization contracts.

## Security considerations

Threats and required controls.

## Test plan

Tests written before production implementation.

## Implementation slices

Small independently verifiable tasks.

## Diagnostics

How failure will be detected and understood.

## Documentation

Required user/developer/ADR updates.

## Acceptance criteria

Objective conditions for completion.

## Definition of Done

All code, tests, security requirements, documentation, migration, and
diagnostics complete.

A feature mini-plan shall be split further when its implementation scope
is too large to complete safely within one Agent context.

------------------------------------------------------------------------

# 61. Definition of Done

A feature is complete only when applicable requirements below are
satisfied:

-   acceptance criteria met
-   tests were authored as executable specifications
-   unit tests pass
-   integration tests pass
-   security tests pass
-   architecture rules pass
-   authorization is enforced
-   relevant actions are audited
-   no secrets are logged
-   diagnostic behavior exists
-   error conditions are modeled
-   failure recovery is defined
-   database migrations are complete
-   SQLite tests pass where applicable
-   PostgreSQL tests pass where applicable
-   documentation is updated
-   applicable ADRs are updated
-   CI is green
-   no unresolved warnings
-   threat considerations reviewed
-   user-visible errors are actionable and do not disclose sensitive
    information

------------------------------------------------------------------------

# 62. Pre-Implementation Verification Gate

Before Feature 0 package versions are locked, current official
documentation shall be verified for:

-   .NET 10 SDK
-   current C# language support
-   ASP.NET Core 10
-   Blazor Web App
-   Blazor Blueprint UI
-   TUnit
-   TUnit Mocks
-   EF Core 10
-   SQLite EF provider
-   Npgsql / PostgreSQL EF provider
-   SignalR
-   ASP.NET Core Identity
-   current passkey/WebAuthn support
-   OpenTelemetry .NET
-   Docker Engine API libraries
-   Testcontainers
-   Playwright
-   bUnit if selected
-   Caddy
-   Let's Encrypt / ACME requirements
-   Project Zomboid dedicated-server requirements
-   Project Zomboid network ports
-   Project Zomboid RCON behavior
-   SteamCMD requirements
-   current OWASP Top 10
-   current OWASP ASVS

Package names and versions shall not be assumed from historical
documentation.

The verification result should become an ADR or technology-baseline
document committed to the repository.

------------------------------------------------------------------------

# 63. Explicit Non-Goals for Initial Product

The initial architecture shall not require:

-   Kubernetes
-   microservices beyond the explicit Manager/Agent trust boundary
-   Redis
-   message brokers
-   distributed cache infrastructure
-   third-party cloud services
-   external observability infrastructure
-   external identity providers
-   third-party PZ Docker images

These may be integrated later only where product requirements justify
the added complexity.

------------------------------------------------------------------------

# 63A. SaaS Security and Isolation Requirements

Hosted SaaS shall add the following mandatory controls:

-   every request resolves an authenticated tenant context
-   every tenant-owned query is tenant-scoped
-   every command is authorized against tenant and server ownership
-   Agent connections are bound to exactly one tenant
-   enrollment credentials are single-use and short-lived
-   support access is explicit, time-limited, and audited
-   platform operators cannot silently impersonate tenant users
-   cross-tenant integration tests are required
-   tenant identifiers and external Auth0 organization IDs are not
    interchangeable
-   tenant deletion requires a deliberate, audited workflow
-   usage limits must fail closed
-   diagnostics must never combine data from unrelated tenants

SaaS billing, quotas, and plan enforcement may be added later, but
tenant isolation and tenant-scoped authorization are not optional SaaS
features.

# 64. Product Success Criteria

ZWarden is successful when a Project Zomboid administrator can:

1.  deploy the supported stack with minimal setup
2.  securely authenticate
3.  manage one or more servers
4.  understand server health
5.  start, stop, and restart servers
6.  update Project Zomboid
7.  manage mods correctly
8.  administer players
9.  securely use RCON
10. create and restore backups
11. audit administrative activity
12. diagnose common failures without SSH
13. generate safe troubleshooting information
14. manage remote servers without opening privileged Agent ports
15. operate the platform without needing to understand its internal
    Docker architecture

The product should make the secure path the easiest path.

------------------------------------------------------------------------

# 65. Technology Verification References

The following official documentation was reviewed when updating this
PRD:

-   ASP.NET Core policy-based authorization:
    https://learn.microsoft.com/en-us/aspnet/core/security/authorization/policies?view=aspnetcore-10.0
-   ASP.NET Core role-based authorization:
    https://learn.microsoft.com/en-us/aspnet/core/mvc/security/authorization/roles?view=aspnetcore-10.0
-   Auth0 Organizations:
    https://auth0.com/docs/manage-users/organizations/organizations-overview
-   Auth0 RBAC: https://auth0.com/docs/manage-users/access-control/rbac

These references support the architectural direction, but package
versions, feature availability, pricing, and provider plan requirements
must be reverified during implementation.
