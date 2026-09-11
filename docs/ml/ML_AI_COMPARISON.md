# ML improvement and ML + AI measurement — 11 September 2026

The new statistical ensemble improves several retrospective metrics, but **has not earned automatic release**. The 2–3 goals market slightly regresses on proper probability scores. There is **no defensible measured ML + language-model AI accuracy** in the available database. The implementation now records the evidence needed to measure it prospectively.

## Measured model results

These are development evaluations on **16,261 chronological out-of-fold matches**, 13 August 2023 through 2 May 2026, across eight expanding folds. Each fold has distinct training, calibration and test dates. The source contains 24,481 fixtures, including 24,127 finished matches. Runtime: **FastTreeTweedie on ARM macOS**, because the native LightGBM dependency could not load. This is not a production LightGBM benchmark or the live app's measured accuracy.

| Target | Pure ML, existing recipe | Dixon–Coles baseline | New DC + ML ensemble |
|---|---:|---:|---:|
| Over / Under 2.5, accuracy at 0.5 | 54.29% | 54.84% | **55.13%** |
| GG / no GG, accuracy at 0.5 | 53.00% | 54.26% | **54.37%** |
| Home / draw / away, argmax accuracy | Not recorded in this comparison | 46.60% | **47.60%** |
| 2–3 goals / otherwise, accuracy at 0.5 | Not recorded in this comparison | 53.96% | 53.96% |

GG's observed base rate was 54.01%, so always predicting GG would itself score 54.01% accuracy. Both 2–3 goals models predict “otherwise” on every row at 0.5; their 53.96% classification accuracy is the majority-class rate. Accuracy alone can hide a weak model.

| Probability score (lower is better) | Dixon–Coles | Ensemble |
|---|---:|---:|
| Over 2.5 Brier / log loss | 0.2467 / 0.686459 | **0.2460 / 0.685176** |
| GG Brier / log loss | 0.2474 / 0.687974 | **0.2471 / 0.687279** |
| Winner multiclass Brier / log loss | 0.629888 / 1.046317 | **0.623290 / 1.037259** |
| 2–3 goals Brier / log loss | **0.2483 / 0.689815** | 0.2484 / 0.689866 |

The small 2–3 goals regression fails the expanded release gate. The failed result is retained in [the complete ensemble report](ensemble-audit-2026-09-11.json). No model generation or pointer was published. Existing compatible, versioned pure-ML generations remain readable; an unknown recipe or incompatible feature schema is rejected.

Week-block bootstrap intervals use 2,000 resamples of 135 whole UTC weeks, preserving within-week dependence across leagues. Ensemble-minus-DC Brier differences:

- Over 2.5: −0.000625; exploratory 95% interval **[−0.001061, −0.000190]**.
- GG: −0.000339; exploratory 95% interval **[−0.000699, +0.000005]**. This includes zero.

These intervals do not correct for model-development selection. The historical data and previous baseline audit were already inspected before designing the ensemble. This is a chronological development evaluation, **not an untouched final holdout**. Historical feature availability is reconstructed; it is not equivalent to immutable live forecasts. Calibration data must be separate from model fitting, and reusing evaluation data for model selection introduces optimism; see [scikit-learn calibration guidance](https://scikit-learn.org/stable/modules/calibration.html) and [nested evaluation guidance](https://scikit-learn.org/stable/auto_examples/model_selection/plot_nested_cross_validation_iris.html).

## Selected forecasts and the 80% target

These thresholds came from the existing confidence settings. They apply only to probability forecasts, **without odds, EV, AI, confluence or ticket filtering**.

| Selection | Pure ML: hit rate / count / coverage | Ensemble: hit rate / count / coverage |
|---|---|---|
| Over 2.5 probability ≥ 0.60 | 60.67% / 3,270 / 20.11% | **64.01% / 2,137 / 13.14%** |
| GG probability ≥ 0.58 | 58.28% / 4,180 / 25.71% | **59.97% / 4,309 / 26.50%** |

The Over improvement also selects substantially fewer matches. It is not a like-for-like gain on an identical selected subset. The descriptive threshold sweep finds 12/14 correct Over forecasts at ≥0.75 (85.71%), but its nominal Wilson lower bound is only 60.06% and coverage is 0.09%. At ≥0.80, Over has exactly one forecast. Neither result supports an “80% accurate” product claim or a deployed threshold change.

The ensemble makes 5,368 false-positive GG classifications at 0.5: 1,226 finish Over 2.5 without GG and 4,142 finish neither. It predicts Under before 4,023 actual Over results. These counts describe errors, not proven causes. Greater GG-positive coverage increases its false-positive count even as overall GG accuracy improves. The raw counts must be interpreted with the denominators and recall.

## What changed in the ML model

`GoalRateEnsemble` learns one convex ML weight from calibration rows, minimizing combined Over/GG Brier error. The weight is bounded to [0,1], with fewer than 100 eligible rows defaulting to DC. Fold weights ranged from 0.098 to 0.432. The trees are fitted earlier; goal-rate scales and mixture weight are fitted on calibration data; subsequent test labels are never used to fit that fold's weight.

The same weight combines all score-distribution market marginals, including the GG-and-Over joint probability. This preserves their probability relationships. Training evaluation and live inference use the same blend. The generation manifest names the recipe, and calibration stores its weight. No language-model confidence percentage enters this calculation.

Evaluation now includes winner and 2–3 goals proper scores, pure-ML binary controls, paired uncertainty, error categories and coverage. The release gate requires all supported target probability scores to avoid regression against DC; the Over/GG markets must also avoid regression against the historical frequency prior. The threshold sweep does not choose deployment settings.

## Why ML + AI accuracy is currently unknown

The frozen local database contains 580 AI rows for 290 fixtures, but **zero immutable prediction snapshots**. Only two finished fixtures have a latest AI cache update at least one hour before kickoff. Each of GG and Over was correct once out of those two. This is a tiny, mutable **legacy AI-only diagnostic**, not a paired ML + AI measurement. A cache's `UpdatedAt` also changes during mathematical recomputation, so it cannot certify the original AI generation time.

The configured PostgreSQL read-only export was attempted, but the connection closed during setup (`NpgsqlException → EndOfStreamException`). No production data could be audited, and no migrations or writes were run there. The available local football results end on **2 May 2026**. There are zero provider-observed xG values in that source. These limitations cannot be repaired by relabeling imputed xG or recomputing historical AI opinions with a model that may know the results.

See [the AI evidence audit](ai-combined-audit-2026-09-11.json) and its [export provenance](ai-combined-audit-2026-09-11.input.json).

## Prospective ML + AI comparison now implemented

The existing AI policy changes **qualification**, not probabilities. `Confirm` adds a confirmation when AI agrees, `Veto` can remove a pick, and `Ignore` leaves selection unchanged. An AI `false` means “not endorsed”; it is not a prediction that the opposite football outcome will happen. AI already receives model estimates in its prompt, so agreement is not independent evidence.

The statistics API and native iOS statistics screen now compare:

1. Model + rules, before the AI opinion.
2. Model + rules + AI, under the actual recorded policy.
3. Only original model picks also backed by AI, as a shadow filter.

All three arms use the same eligible fixtures. Each market shows correct/picks, hit rate, coverage, nominal Wilson intervals, and picks added or removed by AI. These are qualified individual-market selections, not every classification, headline confidence picks, or combination-ticket returns. A higher hit rate with lower coverage is not proof of causal AI improvement.

Each real AI response records its generation time, actual requested model, system-prompt hash and fixture-input hash, assigned by the provider adapter. The model cannot self-declare these provenance fields in its response. The immutable three-hour prediction ledger captures those fields, the pre-AI counterfactual, actual policy, probabilities and odds state. It keeps one snapshot per fixture/window; analysis refreshes cannot rewrite a recorded forecast. If AI arrives after that window's first capture, it becomes measurable in a later capture.

Settlement selects the latest recorded snapshot at least one hour before the actual kickoff and excludes changed kickoff dates. Comparison excludes missing AI, unverifiable provenance and stale prices. Every arm enforces the current recorded price floor (at least 1.70) and value requirements. Zero qualified picks means **unknown hit rate**, not 0% success. The app explains empty history in German and English.

An accompanying decision fix recomputes combo eligibility and Kelly stake after AI changes qualification; an AI veto previously left stale combo/stake values behind.

## Validation and next evidence

Apply the nullable AI-provenance migrations to both deployed database variants as appropriate when releasing this code. Historical rows remain unverified. Start the updated worker/API to accumulate immutable captures; do not backfill old AI decisions as if they were live predictions. No paid AI requests were made for this audit.

The next model decision needs current data and the actual production LightGBM runtime, followed by a fixed prospective test period. Provider-observed xG, injuries and lineups should be added only with usable observation times and evaluated through ablations. More API calls alone do not establish more signal. Keep the ensemble's failed release result, collect genuine pre-match ML/AI pairs, and evaluate hit rate, coverage, probability scores and odds-based returns separately before promoting a model or marketing an accuracy claim.

Reproduce the offline checks from the backend repository (these commands bypass application-host startup and migrations):

```sh
python3 tools/ml/export_ai_evidence.py --help
dotnet run --project src/soccer-ai-tools -- audit-goal-rate --input=/path/to/fixtures.json --output=/tmp/goal-rate-audit --settings=src/soccer-ai-api/appsettings.json
dotnet run --project src/soccer-ai-tools -- audit-ai-combined --input-dir=/path/to/export --output=/tmp/ai-comparison.json
dotnet run --project src/soccer-ai-tools -- export-ml-audit --settings=/path/to/settings.json --output=/tmp/football-export
```

Frozen input hash: `c27b9f4f37f3ac95512deb5d2a9a74032afe98ad0ec2493f1bbc83a4b045d1ec`. Model settings are recorded in [ensemble input provenance](ensemble-audit-2026-09-11.input.json). Source hashes are in the adjacent source-provenance file. The report retains the unsuccessful release gate rather than rewriting the acceptance criteria after seeing the result.
