# Issue #364 Mini-Plan — a host with no usable PZ image is refused up front, and fails fast

**Status:** one PR (branch `fix/364-no-pz-image`). Sub-issue of the Fleet epic #348.

**Written against:** issue #364; F14 (`ServerProvisioner`, `PzContainerFactory`); ADR 0008 (pinned, never floating,
canonical image); F35 D-1 (`HostDescriptor` on `AgentHello`); #230 (`IHostCapacityCache`, the in-memory per-Agent
report the Deploy sheet and `RegisterAsync` already read); #338 (Deploy server sheet).

## Objective

A Host whose Agent has no usable Project Zomboid image (`ZWARDEN_PZ_IMAGE` blank or a floating `:latest`) is shown
in Deploy server but can't be picked, with the reason. `RegisterAsync` refuses it server-side. If a provision or
Recreate still reaches such an Agent, the Operation fails **at once** with an actionable reason instead of waiting
for the lease reaper.

## Facts found (2026-10-06)

- `ServerProvisioner.SpecFor` passes `_options.PzImageReference ?? string.Empty`. `PzContainerFactory.Validate`
  then throws a bare `ArgumentException`, which `CreateAndStartAsync` doesn't catch (it catches only
  `ContainerCreateException` and IO errors). It escapes to the dispatch catch, so the lease reaper fails it later.
- On Recreate, the old container is stopped and **removed** before the create throws. A Recreate on a host with no
  image would take the server down, then fall into the rollback create, which throws the same way. So the check has
  to run before anything changes.
- The hello's `HostDescriptor` is optional, additive JSON. A new optional member is wire-compatible both ways (an
  older Agent sends none; an older Web ignores it). No protocol version bump is needed.
- Deploy sheet options already carry "— offline" and are `disabled` when not connected (`HostOption.Of`).

## Decisions

- **D1 — one Agent rule for a usable image.** `PzImageRules.Problem(string? reference)` (Agent, `Docker/`) returns
  null for a usable reference, otherwise the actionable reason: blank → "This host has no Project Zomboid image
  configured. Set ZWARDEN_PZ_IMAGE in the Agent's .env and restart the Agent."; floating → "… image '<ref>' is a
  floating 'latest' tag, which is refused (ADR 0008). Set ZWARDEN_PZ_IMAGE to a specific tag or digest …". The
  factory's floating-tag guard keeps throwing (it's the last line of defense), using the same predicate.
- **D2 — the provisioner refuses first.** `ProvisionAsync` and `RecreateAsync` check the rule before any other
  step and return `Failed(reason + " Nothing was created/changed.")`. That's the normal failed-Operation path, so it
  fails within seconds.
- **D3 — the Agent reports readiness on hello.** `HostDescriptor` gains `bool? PzImageReady = null` (null = an Agent
  built before #364, meaning unknown). The connection sends `HostDescriptorProvider.Current with { PzImageReady = … }`
  from its options. The value is fixed for the process lifetime; changing `.env` needs an Agent restart, which
  reconnects and sends a fresh hello.
- **D4 — Web keeps it in memory, like capacity.** `IHostProvisioningCache` (Application) with
  `Record(AgentId, bool)` / `bool? IsPzImageReady(AgentId)`, recorded by `AgentHub.Hello` keyed by the
  authenticated Agent id (never the payload). No migration: a host is only deployable while connected, and a
  connected Agent has sent a hello to this process. It's observed, untrusted data (trust-boundaries §3). It drives
  a refusal that protects the operator from a hang, never a grant.
- **D5 — listing + server-side refusal.** `DeployHost` gains `bool PzImageReady` (true when unknown, so an old Agent
  isn't blocked). `RegisterAsync` returns the new `ServerRegisterFailure.NoPzImage` when the cache says false. Every
  failure mapping (sheet draft, `/api/servers` endpoint, setup) gets a message: "That host has no Project Zomboid
  image configured. Set ZWARDEN_PZ_IMAGE in its Agent's .env and restart the Agent."
- **D6 — the sheet disables the host with its reason.** Option label "<name> — no PZ image configured", `disabled`,
  and not auto-selected when it is the only connected host. Offline takes precedence in the label.
- **D7 — docs say "required".** remote-agent docs + both `.env.example` files: required before this host can run
  servers (no longer "optional now"). The Enroll host token panel adds a note under the `.env` lines instead of a
  commented placeholder, because a second `ZWARDEN_PZ_IMAGE` line next to the example's blank one is how the DMZ
  misconfiguration happened.

## Out of scope

- The generic dispatch catch ("the operation's lease will reap it") stays as it is for genuinely unexpected faults.
- A Hosts-card chip for "no PZ image" (could follow from the same cache).

## Tests

- Agent: `PzImageRules` (null / blank / `latest` / `repo:latest` / pinned tag / digest); `ServerProvisioner`
  provision + recreate with a missing and a floating image → failed outcome with the reason, runtime never touched
  (no create, no stop/remove).
- Agent: the hello carries `PzImageReady` (provider helper for given options).
- Contracts: `AgentHello` round-trips `PzImageReady`; a payload without it reads as null.
- Web/Infra: `ListDeployHostsAsync` marks a host whose cache entry is false; `RegisterAsync` refuses with
  `NoPzImage` and creates nothing; the hub records readiness on hello.
- Web (bUnit): the Deploy sheet option is disabled with "no PZ image configured" and not auto-selected; the
  register failure message; the token panel note.

## Live check (DMZ)

Agent with blank `ZWARDEN_PZ_IMAGE` → the sheet shows the host disabled with the reason. Set it and restart the
Agent → deployable, deploy works. Clean up `srv-01a10a7a-…` with Delete server.
