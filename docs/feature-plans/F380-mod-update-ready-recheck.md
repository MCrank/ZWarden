# Issue #380 Mini-Plan — "Update ready" clears after the restart that applied it

**Status:** one PR (branch `fix/380-mod-update-ready-recheck`), with #385 (flaky scheduler test) as a final commit.

**Written against:** issue #380; #275 (Update ready = Steam `time_updated` > on-disk `.acf` `timeupdated`); #290 D1/D2
(`ModRefreshScheduler`, `ModRefreshTrigger`, `ModRefreshProcessor`); issue #385.

## Objective

After a restart that pulls a Workshop update, the Mods page's "Update ready" chip (and the Fleet hint) clears by
itself once PZ has finished downloading, even when the download takes longer than 3 minutes. Opening the Mods page
also corrects a stale chip.

## Facts found (2026-10-10)

- Only a disk discovery (`ModRefreshKind.DiscoverServer` → a `ModDiscovery` Operation) re-reads `InstalledUpdatedAt`.
- After a boot, `ModRefreshTrigger.OperationSucceededAsync` queues one discovery now and one after
  `PostBootRediscoverDelay` (3 min). That is all.
- The trigger runs once, in the hub's scope. The delayed request is later run by `ModRefreshProcessor` in its tenant's
  scope, where the DB can say whether anything is still Update ready (`ModChangeSet.Derive(...).UpdatesReady`).
- Opening the Mods page calls `RequestUpdateCheckAsync` once per open (first render). It queues `RefreshMetadata`
  only.
- The scheduler's dedupe only drops a request equal to one still waiting in the channel. A discovery that already
  ran does not stop a new one.

## Decisions

- **D1 — re-checks chain from the processor.** A new `ModRefreshKind.PostBootRediscover` with a new
  `ModRefreshRequest.Attempt` field. The trigger queues attempt 1 after `PostBootRediscoverDelay`, in place of the
  plain follow-up discovery. The processor then:
  - attempt 1: always discovers. This keeps D2 of #290: new downloads' `mod.info` ids are read even when nothing is
    Update ready.
  - attempt 2 and later: discovers only if the server still has an item Update ready. Otherwise it stops.
  - after discovering, queues the next attempt after the same delay, unless this was the last one.
- **D2 — the cap.** New `ModRefreshOptions.PostBootRediscoverWindow`, default 15 min. Attempts =
  `max(1, Window / Delay)`, so 5 re-checks at 3, 6, 9, 12 and 15 min.
- **D3 — the Mods page re-reads the disk when it matters.** `RequestUpdateCheckAsync` also queues a
  `DiscoverServer` when the derived overview has at least one item Update ready **and** the newest disk observation
  among those items is older than `ModRefreshOptions.UpdateReadyDiskMaxAge` (default 1 min). The age check is the
  throttle: reopening the page within a minute of a discovery's report queues nothing new.
- **D4 — #385.** `ModRefreshSchedulerTests` uses `FakeTimeProvider` (`Microsoft.Extensions.TimeProvider.Testing`). The
  delayed-request test asserts nothing is readable before `Advance`, and the request is readable after.

## Tests (TDD)

1. `ModRefreshTriggerTests`: a boot queues discovery now plus `PostBootRediscover` attempt 1 after the delay.
2. `ModRefreshProcessorTests`:
   - attempt 1 discovers and queues attempt 2, even with nothing Update ready;
   - a later attempt with an item Update ready discovers and queues the next one (repeats);
   - a later attempt with nothing Update ready does not discover and queues nothing (stops);
   - the last attempt discovers but queues nothing (cap);
   - an unknown server does nothing.
3. `ServerModOverviewServiceTests`:
   - with an item Update ready and an old disk observation, opening queues `RefreshMetadata` and `DiscoverServer`;
   - with nothing Update ready, only `RefreshMetadata`;
   - with an item Update ready but a fresh disk observation (under 1 min), only `RefreshMetadata`.
4. #385: `ModRefreshSchedulerTests` has no `TimeProvider.System` timing. The Infrastructure test floor goes up only by
   the new #380 tests.

## Verify live

On the next real Workshop update for a running server: Fleet hint → Mods "Update ready" → Restart → the chip clears
without a Web redeploy. For a stale chip, reopening the Mods page clears it within a few seconds.
