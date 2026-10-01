# Four-source predictions and fresh AI refresh

## What is implemented

`ICombinedPredictionService` combines historical Dixon–Coles, raw learned ML probabilities, API-Football evidence and a new structured AI forecast. `ICombinedPredictionRefreshService` fetches/computes those inputs, persists a new immutable combined version, updates the normal English/German analysis snapshots and optionally forces new AI narration.

The existing app remains backward-compatible. Once a fixture has a combined version, normal analysis and decision computation use that version's probabilities. Fixtures without one retain the existing statistical pipeline. **The explicit refresh workflow does not add AI/provider calls to ordinary GET requests or every scheduled sync.** An [opt-in worker scheduler](COMBINED_PREDICTIONS_AUTOMATION.md) now supports bounded daily and pre-kickoff refreshes; it ships disabled.

## New refresh endpoint

```http
POST /api/automation/predictions/{fixtureId}/refresh?refresh_narration=true
X-API-Key: <admin API key>
```

Use the internal fixture ID returned by `GET /api/analyze`, not API-Football's external fixture ID. No request body is required. Only existing fixtures with status `NS` and a future kickoff are eligible. Missing fixtures return 404, started fixtures 400, and a busy automation runner 409.

The endpoint returns **202 Accepted** immediately, with `job_id`, a job-specific `poll` URL and the fixture's `analysis` URL. The job has its own service scope and continues after the HTTP request ends.

```http
GET /api/automation/predictions/jobs/{jobId}
X-API-Key: <admin API key>
```

Job states: `running`, `completed`, `completed_with_warnings`, `failed`, `cancelled`. A missing ML bundle, missing/invalid provider or AI output, or failed requested narration cannot be reported as a complete four-source success. Inspect `result.prediction.sources`, `result.prediction.status`, `result.narration_refreshed`, and `result.warnings`.

After completion, read the normal response:

```http
GET /api/analyze/{fixtureId}?language=en
GET /api/analyze/{fixtureId}?language=de
```

These reads use the normal app authorization policy. Both languages share the same combined numbers. The returned match includes:

- `match_prediction`: the overall **home / draw / away** call, confidence, qualification and reason.
- `prediction.home_win`, `prediction.draw`, `prediction.away_win`: the individual outcome probabilities.
- `prediction.btts`, `prediction.over25`, `prediction.two_to_three_goals`: the retained goal markets.
- `prediction.match_winner`: retained for older clients; the same 1X2 call as `match_prediction`.
- `combined_prediction`: source probabilities, methods, model IDs, capture times, applied weights, availability and final probabilities.
- `ai` and the existing decision-explanation fields: freshly generated narration when that stage succeeds.

`headline_prediction` retains its existing best-market/betting-selection meaning for backward compatibility. Use **`match_prediction` for the whole-match 1X2 heading**, not `headline_prediction`. The draw's qualification now comes from the draw gate, rather than incorrectly borrowing the home/away gate. A predicted outcome is not necessarily a qualified bet.

## Refresh sequence

1. Validate fixture/team context.
2. Recompute historical probabilities from stored match history and run the accepted ML model. This is inference, not retraining or a full history/standings sync.
3. Make a fresh API-Football `/predictions` request for this fixture. Do not substitute an older provider row if it fails.
4. Send the source breakdown and provisional numerical blend to the configured AI model for a new JSON forecast covering 1X2, BTTS, Over 2.5, exactly 2–3 goals, the BTTS/Over joint, expected total goals and rationale.
5. Reject invalid probabilities, non-normalized 1X2, inconsistent goal-market intersections and impossible expected-goal bounds. Do not convert `65` to 100% or silently guess missing markets.
6. Recheck kickoff/status/team identity, combine the valid inputs, append the evidence/version to the database, and recompute both normal response snapshots.
7. If requested, force the existing bilingual AI narration service to run against the final combined probabilities, then rebuild its analysis/explanation caches. Narration does not numerically change the final blend.

Every refresh requests a **new numerical AI forecast**. `refresh_narration=false` skips only the separate bilingual narration/explanation stage. Narratives older than the new combined version are hidden instead of being presented as fresh explanations of changed numbers. A narration failure leaves the newly stored probabilities intact and is reported explicitly.

## Sources and weights

Initial configuration, shared by API, worker and tools:

```json
{
  "CombinedPrediction": {
    "HistoricalWeight": 0.45,
    "MlWeight": 0.30,
    "ProviderWeight": 0.15,
    "AiWeight": 0.10,
    "AiModel": "nvidia/nemotron-3-super-120b-a12b:free"
  }
}
```

**These are experimental starting weights, not weights learned from the five-week backtest. No accuracy improvement is claimed.** Responses say `weight_status: configured_not_backtest_validated` and use recipe `four-source-experimental-v1`.

- **Historical:** the underlying Dixon–Coles distribution, not the current app's already blended output.
- **ML:** the accepted model's separately exposed raw learned goal-rate distribution, before its existing historical/ML mix. This avoids counting historical predictions twice through the hybrid ML output. No accepted ML bundle means an unavailable ML source, not fake ML numbers or automatic model promotion.
- **Provider:** actual normalized 1X2 percentages. Goal-market probabilities are an explicitly labelled **Poisson approximation from the provider's recent goals data**, not direct probabilities supplied by `/predictions`. It requires at least three games for each team and finite goals-for/goals-against averages. Home rate is the mean of home goals-for and away goals-against; away rate is the converse. Provider form/attack/defence/comparison percentages and textual goals lines are not goal probabilities. This adapter needs future validation.
- **AI:** one new contextual forecast. It sees the preceding sources, so it is correlated evidence, not an independent fourth vote. The explicit combined-model request has no model fallback and rejects model IDs without `:free`. Bilingual narration retains its existing separately configured primary/fallback behavior.

Weights are renormalized over valid available sources **separately for the 1X2 group and the goals group**. This allows a provider with valid 1X2 but missing scoring data to contribute to outcomes without inventing BTTS/Over/2–3 probabilities. Home/draw/away are normalized to sum to one. Goal marginals and the actual BTTS-and-Over joint share the same goal-source weights; the joint is not computed by multiplying BTTS and Over probabilities. If no valid positively weighted source can supply one of the required groups, refresh fails rather than generating a neutral-looking guess.

`all_four_sources_available` is true only when all four contribute to both groups. Missing, invalid or disabled sources produce `status: partial` and an explicit source breakdown. Existing calibration maps trained on the old recipe are **not** applied to this new unvalidated blend.

## Persistence and history

The additive `CombinedPredictionSnapshots` table stores one row per refresh:

- Unique version ID, fixture ID, kickoff and capture time.
- Final probabilities and the applied source breakdown in `PredictionJson`.
- Supplied provider evidence, successful raw AI response, AI request hash and model input in `EvidenceJson`.

Refreshes append versions instead of rewriting old forecasts. The latest valid pre-match version for the fixture/kickoff supplies the live analysis. Existing `FixtureAnalyses` rows are updated for English/German, and their scalar probability cache is kept in sync. A successful provider refresh also updates `FixturePredictions`.

The existing immutable `PredictionSnapshots` ledger includes the combined source breakdown in its capture context. Its once-per-capture-window behavior is unchanged; an earlier record in that window is not overwritten by a manual refresh. The old `ModelForecasts` comparison ledger is also left untouched. Use the dedicated combined-version history when auditing every manual refresh.

Both PostgreSQL and SQLite migrations are included. Deploy/restart through the existing migration-enabled startup before using the new endpoint. No production migration or refresh was executed during implementation.

## Existing endpoints: when to use them

| Endpoint | Behavior |
|---|---|
| `POST /api/automation/predictions/{id}/refresh` | **New:** fresh four-source attempt, saved combined version, optional forced narration |
| `POST /api/automation/ai-analysis?date=YYYY-MM-DD&force=true` | Existing date-level fresh AI narration; uses combined numbers where a combined version already exists |
| `POST /api/automation/sync-date?date=YYYY-MM-DD&force_ai=true` | Existing full date sync, math/AI analysis, final decisions and publication; does not itself create new four-source versions |
| `POST /api/automation/recompute/{id}` | Existing response-snapshot recompute; does not force external AI/provider requests |
| `GET /api/analyze/{id}?refresh=true` | Existing math/snapshot recompute, not a fresh AI request |

## Configuration and operational limits

- Set `OPENROUTER_API_KEY` locally/on the server, or configure an appropriate `OpenRouter:ApiKey`. The numerical forecast also accepts `AiService:ApiKey` when the narrative and forecast endpoints have the same authority. Never send another provider's credential to OpenRouter. Narration still uses its existing `AiService` credential resolution, so remove/replace an incorrect explicit narrative key even when setting an environment key.
- API-Football requires its existing configured credential. A missing or rate-limited source is exposed, not counted as a completed four-source calculation.
- The accepted ML model must already be trained, validated and available through the existing model store. The endpoint deliberately does not retrain or bypass promotion checks.
- A refresh uses provider quota, one numerical AI request, and potentially narration, repair/fallback and final-explanation requests. Free-model endpoints still require authentication and have quota/capacity limits.
- The automation gate prevents overlapping manual jobs **in the current API process**. It is not a distributed lock against another replica or the scheduled worker. Job status is in memory and is lost on restart; saved prediction versions and completed analysis updates survive.
- No live model response or measured accuracy gain is claimed by the implementation tests. They use controlled providers; the previous missing OpenRouter credential still needs to be supplied for an actual NVIDIA round-trip.

## Tests

Coverage includes weighted combination, missing/invalid inputs, future-evidence rejection, avoiding double-counted hybrid ML, all six requested probabilities, joint-goal consistency, database versioning, English/German snapshot updates, immutable old ledgers, narration failure, kickoff races, draw qualification, admin-only endpoints, job conflict/poll/error behavior, actual SQLite migrations and generated PostgreSQL migration SQL.

Run:

```sh
dotnet test tests/soccer-ai-unit-tests --no-restore
```

See [HTTP E2E tests and remote staging rollout](COMBINED_PREDICTIONS_E2E.md) for deterministic request-to-database tests and the opt-in 3–5 fixture NVIDIA smoke runner. Automatic startup narration can be disabled with `AiStartupSync__Enabled=false` to keep staging provider usage bounded.
