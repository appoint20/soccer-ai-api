# Manual date sync

`POST /api/automation/sync-date?date=2026-09-19&force_ai=true`

Requires the admin `X-API-Key`. A normal app login cannot start or inspect these jobs.
No request body is required. The response is `202 Accepted`, with a `Location` header,
`data.job_id`, and `data.poll`. The job continues after the HTTP connection closes.

After deploying the API containing this change, call:

```sh
curl --request POST \
  'https://soccer-ai-api.onrender.com/api/automation/sync-date?date=2026-09-19&force_ai=true' \
  --header 'X-API-Key: YOUR_ADMIN_API_KEY'
```

Replace the date and key. Poll the returned path on the same API host:

```sh
curl \
  'https://soccer-ai-api.onrender.com/api/automation/sync-date/jobs/JOB_ID' \
  --header 'X-API-Key: YOUR_ADMIN_API_KEY'
```

`date` is required, in exact `yyyy-MM-dd` format, and is a **UTC calendar day**,
matching the analysis and picks endpoints. Today and future dates are accepted.
Already-started, completed, postponed and cancelled fixtures do not receive new
pre-match predictions. Past dates are rejected so hindsight cannot create new
pre-match evidence.

`force_ai` defaults to `false`: current bilingual AI opinions can be reused;
missing/outdated opinions and stale decision explanations are refreshed by the
existing AI service. Set it to `true` to regenerate existing opinions and their
explanations, making fresh paid AI calls. Football data, odds and mathematical
predictions are refreshed regardless of this flag.

## Work performed

| Step | Behavior |
| --- | --- |
| `standings` | Refresh standings for configured leagues in the requested football season. |
| `fixtures` | Import that day's fixtures, update rescheduled fixtures, and refresh supporting season results. A requested day can lie beyond the scheduled sync's usual 14-day import window. |
| `historical_depth` | Backfill prior seasons where the existing history-depth rule requires it. |
| `odds` | Fetch that day's upcoming fixture prices even if recently checked or outside the scheduled odds horizon. Store observations and replace current Bet365 prices; unavailable prices remain unavailable. |
| `ml_predictions` | Recompute English and German snapshots using the serving prediction pipeline. Report the model versions actually used and failed fixture IDs. |
| `ai_analysis` | Generate/reuse bilingual AI assessments, apply their qualification flags, and refresh decision explanations. |
| `final_decisions` | Recompute the day's still-upcoming fixtures after AI has been saved. |
| `publish_picks` | Build the day's final singles, combinations and confidence picks through the existing selection rules; record newly published priced tickets. |

Supporting standings and historical results may cover other dates. New odds,
AI analysis, prediction recomputation and pick publication target the chosen day.
No training, model promotion, or independent model-comparison experiment is run.
If learned-model inference is unavailable, the serving pipeline can fall back to
Dixon–Coles; `model_versions` reports this as `dixon-coles-market-v1`.

AI qualifications influence the decision layer; AI confidence is not averaged
into the mathematical probability. All odds floors, confluence and value rules
still apply. Completion does not guarantee a bet or a claimed accuracy rate.

## Progress and retries

`data.state` is `running`, `completed`, `failed` or `cancelled`.
`data.steps` contains each step's state, timestamps, report and any safe error
description. Reports include refreshed-price coverage, prediction counts/model
versions, AI counts/failed fixture IDs, and the final board plus the number of
newly recorded tickets.

A failed prerequisite stops subsequent steps; those steps are marked `skipped`.
If prediction or AI preparation fails for a fixture, that stage reports its ID
and this job does not publish a new board. Completed fixture writes are retained.
Retry the date after fixing the reported cause; use `force_ai=false` to reuse
already-current AI output. Existing published ledger prices and outcomes are
never rewritten. The current app board is still built from current snapshots;
the ledger preserves historical publication evidence separately.

Core fixture, standings and odds transport/parsing failures are surfaced as
failures rather than counted as successful empty responses. A valid provider
response containing no fixtures or no odds remains legitimate. An empty final
board can therefore mean no eligible fixtures, missing quotes or no qualifying bets.

The job status is stored in API memory; the newest 20 jobs are retained. A restart
loses status and cancels unfinished work, but database writes already completed
remain. A missing/expired job returns `404`.

Concurrent background `sync-date`, `ai-analysis` and `sync-pipeline` calls in the
same API process receive `409`. This gate does not coordinate another API replica,
the scheduled worker, startup AI sync, or the older synchronous automation routes.
It is not a durable or distributed job queue.

## Validation

Local backend suite: 664 passed, 1 optional PostgreSQL test skipped. Tests cover
day boundaries, status/rescheduling changes, forced odds refresh and withdrawn
quotes, malformed/provider failures, AI-before-publication ordering, incomplete
prediction/AI runs, admin-policy requirements, job polling, conflicts and shutdown.
No production sync, live AI provider call, deployment, or new accuracy evaluation
was performed for this endpoint change.
