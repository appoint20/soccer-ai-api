# Scheduled four-source predictions

## Implemented, not activated

`CombinedPredictionWorker` runs inside the existing worker process and calls the same refresh service as the manual HTTP endpoint. Each successful scheduled refresh appends the combined prediction, updates the English/German analysis snapshots and requests fresh bilingual narration. Home/draw/away, BTTS, Over 2.5 and exactly 2–3 goals are retained.

**Automation is disabled by default. No production deployment, activation, live scheduled provider request or remote database migration was performed.** Activation would cause recurring provider usage and database updates and requires explicit environment approval. The existing narration flow is unchanged. Provider polling configurations have since been reduced to two-hour intervals; see [sync and explanation policy](SYNC_AND_EXPLANATIONS.md).

## Default policy after opt-in

| Setting | Default |
|---|---:|
| Poll interval after each pass | 120 minutes |
| Upcoming match horizon | 24 hours |
| Daily refresh | Once per fixture/kickoff per UTC day |
| Final refresh window | From 4 hours until 30 minutes before kickoff |
| Maximum refresh attempts per pass | 3 |
| Maximum refresh attempts per UTC day | 5 |
| Timeout per refresh | 20 minutes |
| Require an accepted shared ML generation | Yes |

Only `NS` fixtures are eligible. Earliest kickoffs are considered first. A current combined snapshot, including one produced manually, suppresses the equivalent scheduled refresh. In the final window, a snapshot captured since T−4h already satisfies that window. A daily capture taken before that boundary does not suppress the final refresh. Rescheduled kickoffs get their own windows.

The polling delay starts after a pass ends, so slow provider requests extend the cadence. Limits can mean some fixtures/windows are not served; this is not a guarantee of complete match coverage. Five attempts is a conservative starting budget, **not five HTTP requests**: numerical AI, narration, explanation, repairs and SDK retries can each consume requests. Other app jobs and services sharing the key have their own usage and are outside this scheduler's budget.

## Safety and recovery

- Missing/incorrectly shaped credentials, disabled AI services or a missing accepted ML generation block the pass before any reservation or provider call. Presence checks do not prove that a key is unexpired or has quota; actual failures remain visible in the outcome.
- Scheduled narration rejects paid primary/fallback model IDs. The existing numerical service requires an explicit `:free` model. No credential value appears in attempt history.
- Reservations are persisted **before** external requests. Failed, timed-out and cancelled attempts consume their window and daily budget; restarting a worker does not reset them.
- A unique fixture/kickoff/window index and serializable transaction enforce duplicate protection, one active scheduled attempt and the daily budget across workers sharing the database. SQLite locking conflicts and PostgreSQL serialization conflicts defer work rather than trigger provider calls.
- A running reservation older than one hour is marked `interrupted` during the next reservation attempt. It still consumes its original window; this deliberately favors avoiding duplicate paid/quota-consuming requests over automatic retry after an uncertain crash.
- Partial or failed results stop the current pass. The next poll can process another eligible fixture. The same consumed window is **not automatically retried**; a later daily/final window can still be eligible. Repair a failed window with an explicit manual refresh after fixing the cause.
- Shutdown cancels active work and attempts to persist `cancelled`. Already completed writes are retained. If outcome persistence itself fails, the reservation remains and follows interrupted-run recovery.
- The ML publication gate is never bypassed and training is not triggered by this scheduler. `RequireAcceptedMl=false` explicitly permits attempts without shared ML, but such results remain partial and cannot be reported as four-source success.

Duplicate protection applies to this scheduler, not arbitrary manual refreshes, legacy narration jobs, provider-capture loops or external callers. Do not run competing narration workflows for the same fixtures during rollout. There is no claim of exactly-once execution at external providers.

## Inspect execution history

```http
GET /api/automation/predictions/scheduled
X-API-Key: <admin key>
```

The usual API envelope contains `data.attempts`, the latest 25 durable reservations. Each includes fixture/kickoff, window, start/finish timestamps, snapshot ID when returned, and status:

`running`, `completed`, `partial`, `failed`, `timed_out`, `cancelled`, `interrupted`.

Blocked/disabled passes make no reservation; inspect worker logs for `blocked`, `disabled` or `daily_limit`. History alone does not establish whether a worker is currently deployed or running. Read saved analyses using the existing `GET /api/analyze/{id}?language=en|de` routes.

## Enable only in an approved environment

1. Confirm the exact staging worker/service and separate staging database, then apply the additive migration through normal startup: `20261001180000_AddCombinedPredictionAutomationPostgres` (PostgreSQL) or `20261001180001_AddCombinedPredictionAutomation` (SQLite).
2. Install valid provider credentials securely and an accepted ML generation; complete the existing [staging smoke checks](COMBINED_PREDICTIONS_E2E.md).
3. Set `CombinedPredictionAutomation__Enabled=true` on that **approved worker only**. The API host does not run this scheduler.
4. When enabling this flow, explicitly disable competing broad narration jobs with `Sync__GenerateAiNarratives=false` on the worker and `AiStartupSync__Enabled=false` on the API. Review other replicas and scheduled jobs sharing the same key. Existing behavior is unchanged until you make these configuration changes.
5. Observe the attempt-history endpoint, persisted EN/DE results and provider usage before increasing caps. Keep numerical AI and narration on the intended free models.

The complete configuration section is present in the worker's settings with `Enabled=false`. Other environment overrides use the same names, for example `CombinedPredictionAutomation__MaxRefreshesPerDay`.

## Verification

Local validation on 2026-10-01: **834 .NET tests passed with zero skipped**, including actual PostgreSQL migration, scheduled persistence and concurrent-claim checks; **16 Python tests passed**. The disposable PostgreSQL container was removed after testing. Full .NET log: `/tmp/soccer-automation-full-tests.log`.

Deterministic tests cover UTC boundaries, daily/final selection, kickoff exclusion, disabled/missing prerequisites, daily/run caps, restart-safe reservations, failure and cancellation recording, partial-status honesty, abandoned reservations, worker shutdown, PostgreSQL concurrent claims, real migrations, admin authorization and scheduled refresh-to-bilingual-HTTP retrieval. Controlled providers are used; these tests do not establish live NVIDIA availability or an accuracy improvement.

```sh
dotnet test tests/soccer-ai-unit-tests/soccer-ai-unit-tests.csproj \
  --filter 'FullyQualifiedName~CombinedPredictionAutomationTests|Category=EndToEnd'
```

PostgreSQL tests require `SOCCER_TEST_POSTGRES` pointing to the disposable localhost `soccer_worker_regression` database; CI provisions it automatically. Never point regression tests at staging or production.
