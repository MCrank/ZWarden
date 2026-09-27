# Feature #258 Mini-Plan — Pick the Build 42 Steam branch when creating a server

**Status:** implemented on branch `feat/258-branch-picker` (one PR), closes [#258](https://github.com/MCrank/ZWarden/issues/258).
v1.0. Sub-issue of #230; adds one field to the new-server wizard.

**Written against:** issue #258 + maintainer comment 2026-09-25 (branch discovery = curated list + custom field);
PRD 25/27 (closed create template); [ADR 0020](../adr/0020-agent-protocol-versioning-and-catalogue.md) (additive
contracts, no version bump); [ADR 0025](../adr/0025-steamcmd-update-orchestration.md) (update = SteamCMD within
the container's `ZW_PZ_BETA`); F229/F230 mini-plans (Recreate preserves container env; heap threading).

## Objective

1. When creating a server, the operator picks **which Build 42 branch** it installs: latest public (default),
   the Steam `unstable` preview, a pinned 42.x branch, or a custom branch name.
2. The branch is **fixed after create**. F17 Update and #229 Recreate stay on it. Switching branch in place is
   out of scope; it would be a later managed operation built on Recreate.
3. The branch is **visible** on the fleet board and on Server Detail.
4. A branch that doesn't exist **fails the install closed, fast, with a clear reason**.

## Verified branch list (live `app_info_print 380870`, 2026-09-27)

| Branch | Build id | Steam description | Build updated |
|---|---|---|---|
| `public` | 24909836 | (none) | 2026-08-24 |
| `unstable` | 25485538 | "Unstable" | 2026-09-23 |
| `42.19` | 24929695 | "Build 42.19.2" | 2026-08-25 |
| `legacy41` | 24928750 | "Build 41.78.21" | 2026-08-25 |

Private branches exist (`privatebranches 1`) and are never listed. PZ publishes per-version branches (`42.19`),
so pinning a 42.x is a dropdown choice. The ~2026-09-23 build is on `unstable`, not `public`.

A nonexistent branch makes SteamCMD print `ERROR! Failed to set beta '<name>'`. It downloads nothing and exits 0,
so the existing "fully installed" check already fails closed, but only after 3 retries and a generic message.

## Maintainer decisions (2026-09-27, all as recommended)

| # | Decision |
|---|---|
| D1 | **Curated list includes `unstable`, with a warning.** Choices: Latest public (default) · Unstable (newest 42.x preview; mods may break; the world may not survive going back) · Pinned 42.19 (Build 42.19.2) · Custom. |
| D2 | **`legacy41` is rejected**, including when typed as a custom branch ("ZWarden supports Build 42 only"). |
| D3 | **Fail fast on a bad branch.** pz-lib detects `Failed to set beta`, stops retrying, and logs `[zwarden] Steam branch '<x>' does not exist or is password-protected; refusing to launch`. |
| D4 | **Display on the fleet board and Server Detail.** Fleet: a small branch tag next to Version when not public. Server Detail: Branch + Version facts and "Updates stay on this branch". |

## Settled design (defaults taken without a fork)

- **`ServerBranchRules`** (Domain/Servers, alongside `HostPortRules`): null or empty = public. A branch is
  `[a-z0-9._-]{1,64}`, must not start with `-` or `.`, and must not be `legacy41`. `public` is normalized to null.
  The rule is shared by the Web (form + JSON API) and the Agent (re-validates before building the spec). The
  charset matters because pz-lib puts `-beta ${ZW_PZ_BETA}` into the SteamCMD runscript unquoted.
- **`ServerBranchCatalog`** (Domain): the curated list, updated per release, with a description and a "preview"
  flag for `unstable`.
- **Contracts:** additive optional `string? Branch` on `CreateServer` (not echoed on `ProvisionResult`: `Server.Branch` is the operator's choice, and the Agent enforces it at create and keeps it on Recreate). No protocol bump
  (ADR 0020, same as #262/#230).
- **Agent:** `PzContainerSpec.Branch`. The factory adds `ZW_PZ_BETA=<branch>` only when set, so the closed env
  gains one fixed key and nothing free-form. `PzContainerFactory.ReadBranch(env)` mirrors `ReadJvmHeap`.
  `ServerContainer` carries the branch, and **Recreate preserves it** (both the new spec and the rollback spec).
  The factory re-checks the branch is validated + normalized. A new closed-set test pins the exact env keys.
- **Persistence:** `Server.Branch` (nullable, max 64), set at `Register` from the operator's choice. It is
  the operator's intent and not observed back. There is one migration per provider (`AddServerBranch`).
- **Web:** `NewServerRequest`/`RegisterServerRequest`/`ServerContainerPayload` carry `Branch`. A new
  `ServerRegisterFailure.InvalidBranch`. The wizard gets a Branch native select plus a custom text input
  (static SSR, explicit `Name`). `ServerSummary` + `FleetFacts` expose the branch.
- **Docs:** fix the stale branch names (pz-lib comment, PZServer README, `pzserver-architecture.md`, F12/F17
  plans, ADR 0025 note, runtime research branch table), and document pinning vs following public.

## Slices (TDD, one commit each)

1. Domain `ServerBranchRules` + `ServerBranchCatalog` (+ tests).
2. pz-lib fail-fast on `Failed to set beta` (+ bats) and comment refresh.
3. Contracts `Branch` + Agent spec/env/read-back/Recreate preservation + Agent re-validation (+ tests).
4. Domain `Server.Branch` + EF config + migrations (Sqlite + Postgres) + inventory/payload/dispatcher threading
   (+ Infra tests).
5. Web: wizard selector, JSON API field, fleet tag, Server Detail facts (+ Web tests; floors bumped).
6. Docs.

## Out of scope

- A live SteamCMD branch query (option (a)); possible follow-up.
- Changing branch after create (a managed "change branch" operation with a mandatory backup, built on Recreate).
- Build 41.
