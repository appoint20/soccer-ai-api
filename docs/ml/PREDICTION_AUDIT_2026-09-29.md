# Prediction pipeline and AI reliability audit

Audit date: 29 September 2026. Backend: `soccer-ai-api` (the `soccer-gpt-api` working directory contains no active backend). Production evidence was exported through a read-only, repeatable-read PostgreSQL transaction. No production configuration, data, model promotion or deployment was changed.

The current implementation does **not** combine historical statistics, ML, API-Football and an AI numerical forecast into one final probability. It has most of the components, but the provider and AI forecasts are not numerical inputs to `ProbabilityPipeline`.

## What calculates the prediction today

| Stage | Actual implementation | Effect on the user prediction |
|---|---|---|
| Historical model | `DixonColesModel` estimates home and away scoring rates from prior finished fixtures, exponential age weighting, venue splits and Bayesian shrinkage. Shipped settings use a 180-day half-life, 70% venue weighting, prior strength 10, and low-score correction rho −0.13. | A normalized score matrix yields BTTS, Over 2.5, match winner, 2–3 goals and BTTS AND Over 2.5. |
| ML model | `GoalRateForecaster` loads an accepted versioned home/away goal-rate model. For current ensemble artifacts it mixes ML and Dixon–Coles distributions using a weight learned on separate calibration data. Its training/calibration cutoff must precede the fixture. | Used when an eligible, compatible model exists. Otherwise `ProbabilityPipeline` falls back to Dixon–Coles. Enabling `HybridModel` alone does not prove ML is serving. |
| Bookmaker calibration | `MarketCalibrator` blends the classical output with available implied odds. Shipped `MarketWeight` is 0.5. Full Over/Under and 1X2 prices have margin removal; BTTS has only the Yes price and uses 1/odds. | With valid prices, the fallback is 50% model and 50% market before later calibration; missing prices leave that market's model probability unchanged. The ML path already uses price features and skips this second blend. |
| Outcome calibration | `MatchAnalysisService` applies historical probability calibration when enough qualifying evidence exists. | Raw probabilities and displayed calibrated probabilities can differ. |
| API-Football `/predictions` | Loaded from `FixturePredictions` into `ProviderPrediction`. | Displayed and used by the final-decision explanation; it was missing from the initial AI opinion and the separate numerical forecast prompts. This audit adds that context to both prompts. It still does not directly change final probabilities. |
| AI opinion | `OpenAiAnalysisService` produces bilingual analysis, advisory qualification flags and evidence confidence. Its prompt explicitly reserves numerical probabilities for the statistical model. | `DecisionService` and the rule engine apply the configured agreement policy to qualification. An AI confidence of 80 is not an 80% event probability. |
| Separate AI forecast | `OpenRouterForecastService` asks each entry in `OpenRouter:Models` for goals, BTTS and Over probabilities; `ModelForecastLedger` freezes them for comparison. | A measurement stream, not an input to the final product probability. It has separate enablement/model configuration from `AiService`. |
| Final explanation | A further AI call explains the already completed decision. | Text generation; it cannot change the decision or probabilities. |

For the underlying score matrix, BTTS sums cells where both teams score at least once; Over 2.5 sums cells where total goals are at least three. BTTS AND Over 2.5 excludes 1–1. Its probability must come from the joint score distribution, not multiplication of BTTS and Over probabilities.

Production evidence: all **6,949** exported immutable snapshots identify `dixon-coles` or `dixon-coles-market-v1`. The **335** scored fixtures in the five-week cohort also use only those versions. There is no recorded evidence here that the trained ML ensemble supplied those forecasts. The export does not establish whether this is caused by an absent generation, a rejected release gate, configuration or deployment differences; the deployment/model-store logs are needed for that distinction.

## Why the AI output is missing

The source configuration specifies `z-ai/glm-5.2:free` and `deepseek/deepseek-v4-flash-0731:free`. Neither ID appears in OpenRouter's live model catalog checked during this audit. The last recorded free narration model is `z-ai/glm-5.2:free`, captured on 25 September. A retired model cannot serve requests just because its former price was zero. [OpenRouter model catalog](https://openrouter.ai/api/v1/models).

The production sync record captured at 20:47 UTC shows a run started at **20:19:59 UTC on 29 September**, last completed step `model_forecasts`, and error **“AI analysis incomplete: 0 persisted, 34 failed.”** The last wholly successful sync is **20 September, 23:23 UTC**. These are observed failures. The aggregate error does not reveal the actual HTTP response or the current deployment's environment overrides, so it cannot prove which provider error caused every failed request.

Other verified failure paths in the code:

- Every 429 previously aborted the run before trying the fallback. OpenRouter documents both platform quota limits and upstream provider overload as sources of 429. Switching models can help the latter; it cannot replenish a daily platform quota. [OpenRouter limits](https://openrouter.ai/docs/api_reference/limits).
- A static primary-failure counter could leave the primary skipped across later service scopes for the lifetime of the process. It is now scoped to the current service instance, allowing later syncs to try it again.
- Reasoning was enabled without a budget for narratives, while the numerical forecast had only a 2,048-token output cap and no reasoning control. Depending on the provider, reasoning can consume the available generation budget or delay the final JSON. Defaults now request reasoning off; this reduces that risk but is not a guarantee for every model. [Reasoning controls](https://openrouter.ai/docs/guides/best-practices/reasoning-tokens).
- If both narrative model settings were blank, the code silently selected paid Anthropic models. That branch now reports configuration failure.
- Numerical forecasts silently clamped invalid values: for example, `65` as a probability became `1.0`. Such output is now rejected instead of being stored as certainty.
- `OpenRouter:Models` is empty by default, independently of the narrative model settings. Setting `AiService:DefaultModel` alone does not enable numerical AI forecasts. Production has previously recorded forecasts, so its actual deployed overrides must be checked separately.

No local OpenRouter inference credential is available. The catalog checks are live, but no authenticated inference smoke test was performed, and recovery in the deployed worker remains unverified.

## Available free NVIDIA models and local changes

The live catalog lists these NVIDIA general-purpose free models at zero input/output token price, including `nvidia/nemotron-3-super-120b-a12b:free`, `nvidia/nemotron-3.5-lightning:free`, and `nvidia/nemotron-3-ultra-550b-a55b:free`. Listing is not an uptime or football-accuracy guarantee. The Super entry advertises structured output support, making it a reasonable candidate to test for this JSON workflow. [Nemotron Super](https://openrouter.ai/nvidia/nemotron-3-super-120b-a12b:free), [Nemotron Lightning](https://openrouter.ai/nvidia/nemotron-3.5-lightning:free).

Local defaults now use **NVIDIA Nemotron 3 Super free**, with **Google Gemma 4 31B free** as a different-vendor fallback. Both IDs and zero token prices were checked against the live catalog. Numerical forecast reasoning control, provider context, strict numeric validation, quota-aware fallback, and scoped failure recovery are covered by regression tests.

Free inference has request limits: the documented ordinary free allowance is **20 requests/minute and 50/day**, rising to **1,000/day** after the relevant lifetime credit purchase threshold. Narration plus final explanation normally costs two requests per newly analyzed fixture; one separate forecast makes three before retries. A 34-fixture run can therefore exceed the lower allowance even when every individual model is free. Provider capacity can impose additional failures. [OpenRouter rate-limit guidance](https://openrouter.zendesk.com/hc/en-us/articles/39501163636379-OpenRouter-Rate-Limits-What-You-Need-to-Know).

After deployment, inspect overrides on both the API and worker. The intended settings are:

```text
AISERVICE__DEFAULTMODEL=nvidia/nemotron-3-super-120b-a12b:free
AISERVICE__FALLBACKMODEL=google/gemma-4-31b-it:free
AISERVICE__REASONING__ENABLED=false
AISERVICE__ENABLED=true
SYNC__GENERATEAINARRATIVES=true
```

`OPENROUTER_API_KEY` must be the OpenRouter account key on the service executing requests. For the separate numeric comparison stream, explicitly set `OPENROUTER__MODELS__0=nvidia/nemotron-3-super-120b-a12b:free` and remove retired entries. It consumes additional quota. Changing `render.yaml` does not replace manually managed service overrides. No secrets belong in this report.

Verify recovery with one upcoming fixture: both languages must persist with server-assigned model, generation time and input/prompt hashes, the match endpoint must return them, and an enabled numerical stream must add a pre-kickoff `ModelForecast`. Check the real account counter using `GET /api/v1/key`. The source changes alone do not establish production recovery.

## Recommended design for the four sources

Implement a **learned, calibrated ensemble**, keeping each source and its availability visible. The requested sequence is sensible for gathering evidence; equal voting or arbitrary 25% weights would not establish improvement.

1. **Historical goals baseline.** Keep the Dixon–Coles score distribution, expected goals and its history/sample counts. Freeze its inputs before kickoff.
2. **ML goals forecast.** Train home/away scoring models on chronological data, including rolling goals, available xG, opponent strength, venue and rest. Use only features available at forecast time. Retain an explicit fallback when no accepted artifact exists.
3. **API-Football evidence.** Persist the full response with capture time and normalized fields. Its home/draw/away percentages are winner probabilities. Its comparison `goals` and `poisson` values are relative team ratings, and `under_over` is a line recommendation. They cannot be averaged directly into BTTS or Over probabilities. Learn their relationship to each goals target using earlier recorded outcomes, or use them as features in the stacking model. [API-Football prediction response](https://www.api-football.com/news/post/how-to-get-started-with-api-football-the-complete-beginners-guide).
4. **One structured AI interpretation.** Give the AI the available historical, ML and provider evidence in a single versioned payload. Request explicit goals-market opinions, supporting facts, missing-data fields and a concise bilingual explanation. Validate the schema and numerical ranges. Self-reported confidence is not a source weight. Keep initial AI prose separate from the definitive final-probability wording.
5. **Learn the combination, then generate the final result.** Train a regularized meta-model from out-of-fold source predictions and source-availability flags. For valid probabilities on the same target, an initial candidate is `p_final = calibrate(w_history*p_history + w_ml*p_ml + w_provider*p_provider + w_ai*p_ai)`, with nonnegative weights summing to one and missing-source handling learned/validated explicitly. `p_provider` exists only after the provider-feature mapping above has been trained. An alternative is to stack goal rates and derive all markets from one coherent final score distribution. Preserve joint-market consistency either way. Narrate the completed final probabilities, not a preliminary component's values.

Historical and ML forecasts overlap in their inputs; AI is explicitly shown those forecasts. They are correlated evidence, not four independent votes. Weights must be allowed to reach zero. For the first release, keep the AI numerical contribution in shadow evaluation until it improves held-out probability scores.

Use separate chronological periods for base-model fitting, ensemble weights, and probability calibration, followed by an untouched evaluation period. The requested last five weeks are evaluation data; do not tune weights on them and claim the same period validates the result. Report Brier score, log loss, reliability by probability bucket, selected-market precision, sample count and coverage. Show odds-based returns only where a valid quote was recorded before the prediction. Calibration must use data separate from fitting; small calibration sets are particularly risky for isotonic methods. [Calibration guidance](https://scikit-learn.org/stable/modules/generated/sklearn.calibration.CalibratedClassifierCV).

Operationally, generate in one worker, cache by fixture plus input/prompt/model version, prioritize near-kickoff fixtures, and queue against the actual remaining free allowance. Avoid duplicate generation in API startup and worker runs. A final response should identify used and missing sources, source probabilities, learned weights, final probability, narrative status and provenance. Provider unavailability should produce an explicit statistical-only result rather than a missing match prediction.

This audit implements the concrete AI reliability fixes and input-context correction. The four-source learned ensemble is a design recommendation, not an already implemented or validated production model.

## Five-week evidence supporting that recommendation

Full results: [five-week backtest](five-week-backtest-2026-09-29.md) and [machine-readable evidence](five-week-backtest-2026-09-29.json).

Period: **25 August–28 September 2026**, five complete seven-day windows in Europe/Berlin. Of 710 finished fixtures in the export, 335 have an eligible immutable prediction at least one hour before the actual kickoff. The first two weeks have no such stored forecasts. Missing records remain excluded.

| Measure | BTTS | Over 2.5 |
|---|---:|---:|
| Yes/No classification accuracy | 204/335 = **60.9%** | 203/335 = **60.6%** |
| Always predict Yes on the same fixtures | 60.0% | 55.8% |
| Yes-forecast hit rate | 195/320 = **60.9%** | 141/227 = **62.1%** |
| Actual recorded qualified selections | 21/36 = **58.3%** | 27/45 = **60.0%** |
| Fresh-priced qualified subset, simulated 1-unit stakes | 24 selections, **+1.23 units** | 27 selections, **+1.17 units** |

Those small priced subsets do not establish overall profitability. Selection rules and model versions changed during the period, so the report evaluates the recorded system, not one fixed new model.

On **99 paired fixtures**, recorded NVIDIA Ultra forecasts achieved **51.5% BTTS / 52.5% Over** accuracy versus **57.6% / 56.6%** for their contemporaneous statistical inputs. AI Brier scores were also worse: **0.3261 vs 0.2445** for BTTS and **0.3474 vs 0.2345** for Over. This is evidence against giving that recorded AI output an automatic positive weight; it is not a test of the newly configured Super model.

On a different **117-fixture** cohort, recorded GLM forecasts improved Over classification accuracy but worsened Brier score in both markets. Accuracy alone would miss that probability-quality deterioration. The existing strict AI-policy comparison has 50 eligible common fixtures but zero value-qualified goals selections, so it cannot establish a policy hit-rate lift.

## Validation and reproduction

The full .NET suite completed with **781 passed and 1 skipped** before the final malformed-error-envelope regression case; the affected rejection tests were then rerun. No live inference or deployed smoke test was performed. Existing user edits were preserved.

The Python report's snapshot pairing and AI cohort counts/accuracy/Brier values agree with the existing C# audit on the identical dated cohort. The report records input hashes and every scored fixture/snapshot ID.

```sh
dotnet run --project src/soccer-ai-tools -- export-ml-audit --settings=src/soccer-ai-tools/appsettings.json --output=/tmp/football-export
python3 tools/ml/audit_markets.py --input-dir=/tmp/football-export --output=docs/ml/five-week-backtest --end=2026-09-29 --weeks=5
dotnet test tests/soccer-ai-unit-tests --no-restore
```

`--end` is exclusive local midnight. The report tool verifies export hashes, reads frozen records only, and makes no provider calls or database changes. The export settings path contains the connection configuration and must remain private.
