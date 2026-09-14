# Worker sync recovery — 13 September 2026

## Failure and production evidence

The supplied worker trace reports PostgreSQL `23505` on
`IX_ModelForecasts_FixtureId_Model`. Forecast recording checked for an existing
row and then inserted through EF. Overlapping callers could both observe an
empty result and try to add the same fixture/model pair.

After that insert failed, EF retained the pending Added entity. The sync
pipeline used the same scoped database context for step writes and SyncState.
Its catch block tried to save `LastError`, retried the failed forecast insert,
and threw again. The outer worker then logged “Scheduled sync failed”. The
resume cursor could repeatedly skip football/odds refresh and return to the
failing forecast step.

A read-only production export confirmed `LastCompletedStep=publish_picks`, the
step directly before `model_forecasts`, and `LastError=null`. At capture, the
last run began at **2026-09-13 18:19:59 UTC** and the last successful run was
**2026-09-13 16:14:56 UTC**. This supports an intermittent recurring failure; it
does not establish that every sync failed continuously for two days.

The log establishes the unique-key collision and failed error-status save. It
does not identify which overlapping callers caused the original collision.

## Fix

- Forecast persistence now uses a parameterized `INSERT … ON CONFLICT
  ("FixtureId", "Model") DO NOTHING` for PostgreSQL and SQLite. It preserves
  the first forecast and does not leave an Added entity tracked after a race.
  It also checks the fixture's current kickoff and pre-match status within the
  insert. SQLite timestamps are bound as UTC ticks; PostgreSQL receives UTC
  timestamps. Other constraint violations still surface.
- Each sync step gets its own service/database scope. Status saves use a
  separate context, so a failed data write cannot be retried when saving the
  error or the next step's progress.
- Forecast measurement is optional, as the existing pipeline contract already
  states. Its failures are logged and do not block AI narratives. Measurement
  is attempted again on the next run. Essential step failures still fail the
  run and persist an error; cancellation still propagates.
- An incomplete run stopped after publishing restarts from football-data
  refresh instead of resuming only the broken forecast tail.

This fix requires **no schema migration or data deletion**. The unique index
continues enforcing one forecast per fixture/model. PostgreSQL documents the
conflict behavior in its [INSERT reference](https://www.postgresql.org/docs/current/sql-insert.html).

## Verification

- Full local suite: **606 passed, one PostgreSQL test initially skipped**.
- The PostgreSQL test was subsequently run against a disposable local
  PostgreSQL 17 container: **passed**. No production writes were used.
- Concurrent contexts are synchronized so both read an absent forecast before
  either inserts. The test checks that only one row remains, later retries do
  not overwrite it, and status can still be saved. The same regression runs on
  SQLite.
- Additional tests inject a real unique-key failure inside the optional
  forecast step and an essential recompute step. They verify continuation to
  AI, the next football refresh, and correct failure-status persistence.
- Invalid non-unique constraints and changed kickoff dates remain rejected.

To run the optional PostgreSQL test, provide `SOCCER_TEST_POSTGRES` pointing to
a dedicated **local** database named `soccer_worker_regression`. The test
rejects remote hosts or other database names and creates/drops only its own
generated schema.

## Deployment status

Changes are in the working tree. Separate AI-endpoint changes being made in the
same workspace have been preserved. No deployment or production status reset
was performed. The authenticated Render dashboard is blocked by the locked
Mac.

Deploy the patched worker and API (both can record forecasts). The worker's
normal stale-startup sync will recover the saved `publish_picks` cursor. Verify
that logs pass `model_forecasts` and reach `ai_narratives`, then confirm a newer
`LastSuccessfulSyncUtc`. A pre-deployment historical success is not evidence
that this patch is live.
