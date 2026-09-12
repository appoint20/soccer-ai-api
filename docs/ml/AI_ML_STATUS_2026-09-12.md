# AI and ML: measured results and recovery status — 12 September 2026

**There is no evidence for an 80% accurate product.** The new statistical ensemble improves several historical metrics, but fails one release criterion. Historical AI-adjusted forecasts show mixed results on 110 comparable matches. The current ML + AI pick policy still has no verifiable completed sample.

This report supersedes the availability statements in the [11 September report](ML_AI_COMPARISON.md). PostgreSQL became reachable on 12 September. All production exports used read-only, repeatable-read transactions, bypassed application startup and migrations, and rolled back. No historical AI responses were regenerated.

## What the historical AI forecasts actually achieved

The `ModelForecasts` ledger contains AI probability forecasts alongside the statistical estimates supplied to the AI. This is a different path from the bilingual match narrative and the AI confirmation/veto policy. Each AI forecast is compared with its own stored statistical inputs on the **same finished fixtures**, using predictions recorded at least one hour before the actual kickoff. Accuracy scores both Yes and No at 0.5; it is not the win rate of selected bets or combinations.

For the ledger's `anthropic/claude-sonnet-5` model identifier, 110 matches qualify, from **15–24 August 2026**:

| Market | Statistical system | AI-adjusted forecast | AI accuracy, nominal 95% interval |
|---|---:|---:|---:|
| Over / Under 2.5 | 70/110 = **63.64%** | 66/110 = **60.00%** | 50.66–68.67% |
| GG / no GG | 58/110 = **52.73%** | 64/110 = **58.18%** | 48.84–66.97% |

| Probability score, lower is better | Statistical system | AI-adjusted forecast |
|---|---:|---:|
| Over 2.5 Brier / log loss | 0.23336 / 0.65924 | 0.23180 / 0.65593 |
| GG Brier / log loss | 0.24725 / 0.68756 | 0.24392 / 0.68083 |

AI corrected 9 Over/Under calls and spoiled 13. It corrected 20 GG calls and spoiled 14. The probability scores improve slightly while Over/Under classification accuracy falls: those metrics measure different things. These counts do not establish a general AI advantage. Wilson intervals ignore match dependence and are not confidence intervals for the paired change.

The stored statistical GG probabilities are all at least 0.5 on this cohort, so its GG classification accuracy equals always predicting GG (52.73%). The AI comparison must be read against that particular baseline, not as proof that it outperforms a strong current ML model.

Of 150 rows for this model, 2 lack a finished result in this frozen export, 6 fail timing/kickoff checks, and 32 lack valid statistical probabilities. The separate Nvidia row also lacks statistical probabilities, leaving zero comparable matches for that model. Legacy missing probabilities were stored as zero, so statistical probabilities outside the open interval (0,1) are conservatively excluded. AI probabilities must be finite and in [0,1]. Duplicate fixture/model rows are excluded entirely.

The ledger lacks the statistical generation and AI prompt/input hashes. Its model identifier is the recorded attribution, not independent verification of the provider's internal routing. **These results cannot be called validation of the newly trained ensemble.** The dated [machine-readable comparison](ai-combined-audit-2026-09-12.json) includes exclusions, sample sizes, uncertainty, Brier scores, log loss and input checksums. Its [export provenance](ai-combined-audit-2026-09-12.input.json) identifies the 10:27 UTC capture. The comparison contains no raw narratives or credentials.

The old scoreboard compared a pooled system baseline with each model's different fixture set. That could produce a misleading leader. The updated endpoint exposes per-model paired results and permits a flat comparison only on common fixtures with identical statistical inputs. Its descriptive leader is not a claim of statistical superiority. The legacy system goals MAE is now unavailable because the stored value was a recent-goals average, not the model's expected-goals forecast.

## The statistical ML improvement

The expanded export contains **26,123 fixtures, 25,558 finished**. Eight chronological folds score **17,232 out-of-fold predictions**, from 9 September 2023 through 11 September 2026. Each fold fits trees, calibrates on later separate dates, and evaluates on subsequent dates. The mixture weight is learned on calibration data only.

| Target | Pure ML | Dixon–Coles | DC + ML ensemble |
|---|---:|---:|---:|
| Over / Under accuracy at 0.5 | 54.26% | 54.89% | **55.12%** |
| GG / no GG accuracy at 0.5 | 53.52% | 53.92% | **54.63%** |
| Home / draw / away accuracy | Not recorded | 46.74% | **48.09%** |
| 2–3 goals / otherwise accuracy | Not recorded | 54.16% | 54.16% |

Probability-only selections using existing thresholds achieve **64.37% on 2,245 Over forecasts** at p≥0.60 (13.03% coverage), and **59.55% on 4,460 GG forecasts** at p≥0.58 (25.88% coverage). These selections omit live odds, value, AI and ticket gates. They are not the live app's pick accuracy or betting returns. Increasing a threshold and reporting a tiny successful subset would not establish 80% reliability.

The ensemble's Over Brier score is 0.2457 versus DC's 0.2468; GG is 0.2467 versus 0.2474. Exploratory week-block bootstrap intervals for ensemble-minus-DC Brier differences are −0.001070 [−0.001630, −0.000510] for Over and −0.000679 [−0.001089, −0.000261] for GG, using 137 UTC-week clusters. These intervals do not correct for model-development selection.

The 2–3 goals log loss is **0.689578**, slightly worse than DC's **0.689511**. The release gate therefore remains **failed**. No candidate was promoted by this audit, and the acceptance criteria were not weakened after observing this result.

GG false positives number 5,695: 1,311 finish Over without GG and 4,384 finish neither. There are 4,230 Under calls before actual Over results. These are error categories, not proven causal explanations. They help distinguish a 3–0 miss from a 1–0 miss without pretending that GG and Over are interchangeable.

This is a **development evaluation using FastTreeTweedie on ARM macOS**, not a production LightGBM benchmark or an untouched final holdout. Historical feature availability is reconstructed. The [full evaluation](ensemble-audit-2026-09-12.json) and [settings/input hash](ensemble-audit-2026-09-12.input.json) retain those limits. Calibration must remain separate from fitting; see [scikit-learn's calibration guidance](https://scikit-learn.org/stable/modules/calibration.html).

## Serving and AI recovery changes

- Accepted model bundles now transfer through the shared database. The worker publishes the complete home model, away model, calibration, manifest and evaluation together. The API checks compatibility, the stored release decision and file hashes, then loads the complete pair from its local cache. A failed transfer retains the previous verified model or DC fallback. No shared filesystem is required. Render's default filesystem is ephemeral and a persistent disk cannot be shared between services; see [Render's disk documentation](https://render.com/docs/disks).
- New SQLite and PostgreSQL migrations add `GoalRateModelGenerations`. They are additive and were not applied to production during this work. The API checks for a shared generation at most once per minute; model blobs are downloaded only when needed. Generation history is retained.
- Missing AI credentials, invalid bilingual responses and provider account failures no longer masquerade as successful generation. Credential fallback is scoped to the actual provider. The saved narrative repairs stale response snapshots.
- AI responses that finish after kickoff are refused. Forecast recording freezes the first valid pre-match record, checks the actual fixture date/status, and skips missing statistical inputs before making a paid forecast request.
- API-Football failures expose allowlisted error categories without copying provider values into status. The category is diagnostic, not a guessed explanation of the earlier outage.
- Native iOS shows historical AI-adjusted results separately from the current ML + AI selection comparison, including counts and intervals. An empty narrative no longer displays an unlock prompt for content that does not exist.

The updated current-policy comparison still requires genuine pre-match AI timestamps, model/prompt/input provenance, decision audits and fresh recorded odds. The morning export has **zero eligible current-policy pairs**. Older records are not relabeled as current evidence.

## Production status and remaining acceptance work

At 10:27 UTC, the worker was stopped after standings by an API-Football error envelope. At **21:43 UTC**, a new read-only check found a successful sync at **21:29 UTC**, `LastCompletedStep=ai_narratives` and no reported error. It also found only **2 of 56 upcoming database fixtures** in the next five days with both EN/DE narratives. This check included nonempty text regardless of confidence; no additional zero-confidence narratives were found. The forecast ledger remains 151 rows; all 1,492 prediction snapshots identify Dixon–Coles. No exported AI row contains the complete new provenance fields. See the [aggregate production status and source hashes](production-status-2026-09-12.json).

A successful sync status therefore does not establish that AI generation is working. It can reflect a disabled step or the behavior of an older deployed version. The precise deployed configuration and provider outcome have not been verified: the Mac is locked, preventing access to the authenticated Render dashboard. Existing API startup AI generation is preserved; it also depends on the deployed API's own configuration and credentials.

The code and migrations still need deployment to the API and worker. Then verify a real upcoming fixture gains both EN/DE narratives with server-assigned provenance, and that the normal authenticated match endpoint returns that text. Check the deployed worker version, `SYNC__GENERATEAINARRATIVES`, `AISERVICE__ENABLED`, the OpenRouter model and the provider credential on the service making the call. Secrets must stay in the hosting dashboard, not reports or source control.

Model promotion requires the actual production trainer to pass the unchanged gate, followed by a fixed prospective evaluation. The test suite demonstrates transfer correctness, not model accuracy. No new paid AI calls, production mutations or deployment were performed for this audit.

## Verification and reproduction

Backend: **589 tests passed**. Native iOS: **32 tests passed**. Checks include matching forecast cohorts, hindsight exclusions, null rates for missing samples, post-kickoff response rejection, immutable forecasts, transfer between isolated worker/API directories, corrupt/rejected artifact rejection, SQLite migration round trips, and PostgreSQL migration SQL generation. Existing XML-documentation warnings remain.

Offline commands bypass normal startup and migrations:

```sh
dotnet run --project src/soccer-ai-tools -- audit-ai-combined --input-dir=/path/to/frozen-export --output=/tmp/ai-comparison.json
dotnet run --project src/soccer-ai-tools -- audit-goal-rate --input=/path/to/fixtures.json --output=/tmp/ml-audit --settings=src/soccer-ai-api/appsettings.json
dotnet test tests/soccer-ai-unit-tests
```

The input directory must contain export provenance matching every consumed JSON file. Missing optional historical tables remain missing evidence; inconsistent hashes or row counts fail the audit. Never regenerate historical AI opinions to fill gaps in pre-match records.
