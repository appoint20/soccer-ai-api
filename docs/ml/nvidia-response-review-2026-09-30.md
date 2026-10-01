# NVIDIA response review and source-combination findings

Completed 30 September 2026. The backtest retains the original fixed window, **25 August–28 September 2026**, so continuing the investigation on a later day does not change the benchmark.

## What the tests show

Accuracy here means a correct Yes/No call at a 50% threshold, not the hit rate of qualified betting picks. Brier score measures the quality of the probabilities; lower is better. Every comparison below uses identical fixtures for its baseline and candidate, but different rows of this summary can have different cohorts.

| Experiment | Fixtures | BTTS: baseline → candidate | Over 2.5: baseline → candidate | Assessment |
|---|---:|---:|---:|---|
| Historical model → fitted historical/ML blend | 702 | 58.8% → 59.4% | 56.8% → 57.1% | Small accuracy changes; BTTS Brier worsens |
| Saved current app → 50% app / 50% reconstructed ML | 301 | 62.1% → 58.5% | 60.1% → 61.5% | Brier worsens in both markets |
| Saved app at AI capture → 50% app / 50% saved NVIDIA Ultra | 99 | 57.6% → 53.5% | 56.6% → 52.5% | Worse accuracy and Brier; ingestion concerns |
| Saved app → equal historical / ML / saved NVIDIA Ultra | 20 | 65.0% → 70.0% | 60.0% → 65.0% | Only one extra correct call per market; Brier worsens in both |
| Saved app at AI capture → 50% app / 50% saved GLM | 117 | 65.0% → 66.7% | 57.3% → 63.2% | Better on this sample, not an untouched validation result |
| Historical + ML + provider + AI | **0 eligible** | **Not measurable** | **Not measurable** | No timestamp-valid pre-match provider history |

**Conclusion: there is no reliable evidence yet that combining all four sources improves the current app.** The tiny three-source result is not evidence of a four-source improvement. A 70% accuracy estimate on 20 fixtures has a roughly 48–85% Wilson 95% interval. Neither these hit rates nor their changes establish profitability.

The ML experiment uses native LightGBM, with 20,498 fitting rows and 4,727 separate calibration rows, all before the test window. The fitted historical/ML mixture gives ML weight 0.473. This is a reconstructed offline experiment, not a recovered production ML history. There were zero stored ML generations in the export. Bookmaker features were masked in training and prediction. When comparing reconstructed features against recorded app/AI probabilities, only same-UTC-day captures are included to avoid using later days' results.

The export contains 87 latest provider records, including 36 for finished fixtures inside the evaluation window. **None of those 36 was captured at least one hour before kickoff.** Using them as if they were pre-match inputs would introduce look-ahead bias. The provider table stores the latest row rather than an immutable prediction history. Its comparison ratings and 1X2 forecasts also are not direct BTTS/Over 2.5 probabilities.

Full metrics, cohorts, confidence intervals, input hashes and caveats: [comparison report](source-combination-experiment-2026-09-29.md), [machine-readable results](source-combination-experiment-2026-09-29.json). The saved GLM cohort also contains 19/117 boundary probabilities; the same legacy parsing concern applies. Do not interpret the GLM improvement as a clean-model benchmark or choose its weight using this test window.

## What NVIDIA actually returned previously

These are **stored forecast fields and saved rationale text**, not newly generated responses or retained raw HTTP envelopes. The archived model is `nvidia/nemotron-3-ultra-550b-a55b:free`, not the Super model selected for the new probe. Results are shown only to assess already-saved forecasts; they were not supplied to the original model.

### Getafe vs Deportivo La Coruna — 13 September 2026

- Fixture 26533; forecast row 165; captured 15:22:00 UTC, kickoff 16:30:00 UTC.
- NVIDIA BTTS: **48%**; Over 2.5: **40%**. Saved app at that capture: 50% and 44% respectively.
- Saved NVIDIA expected goals: **0.39**; predicted score: **0–0**; confidence: 65%.
- Actual result: **1–1**. NVIDIA's BTTS No was wrong; Under 2.5 was correct.

Saved rationale, verbatim:

> Getafe's home 0.67 GA and 0% Over 2.5 in last 3 vs Depor's 2.33 GF/1.33 GA away suggests ~2.2 total goals. Model's 44% Over 2.5 looks high; market 31% too low. BTTS 48% splits model 50% and market 42% given Getafe's 33% BTTS vs Depor's 100%.

There is an internal contradiction: the rationale says approximately 2.2 goals, whereas the stored expected-goals field says 0.39. More strongly, a 40% chance of at least three goals requires expected total goals of at least 1.2, so these stored fields cannot describe a coherent goal distribution. Without the raw response, attribution between model output and ingestion is uncertain. Syntactically valid JSON and range checks alone are insufficient.

### Karlsruhe SC vs Energie Cottbus — 13 September 2026

- Fixture 26514; forecast row 160; captured 09:22:00 UTC, kickoff 11:30:00 UTC.
- Stored NVIDIA BTTS: **100%**; Over 2.5: **100%**; expected goals: 3.2; predicted score: 1–2; confidence: 100%.
- Actual result: **0–3**. BTTS was wrong; Over 2.5 was correct.

Saved rationale, verbatim:

> Both teams have weak defenses (Karlsruhe conceding 2.33, Cottbus 3.67 last 3) and high BTTS rates (67% each). Cottbus 100% Over 2.5 in last 3. Model underweights Cottbus attack vs Karlsruhe's poor home defense.

In the eligible NVIDIA cohort, **32 of 99 rows contain boundary values (0 or 1) for each goal market**. The old ingestion code clamped out-of-range probabilities, so a response such as `65` could have become `1.0`. Raw responses were not retained. Therefore this evaluates the **saved NVIDIA pipeline**, not clean NVIDIA model quality. Local parser changes now reject invalid probability ranges instead of silently clamping them.

## Fresh prediction and bilingual narration test

OpenRouter currently lists [NVIDIA Nemotron 3 Super (free)](https://openrouter.ai/nvidia/nemotron-3-super-120b-a12b:free) with zero input/output token pricing and JSON-schema support. Free access is still authenticated, capacity-limited and subject to [account rate limits](https://openrouter.ai/docs/api_reference/limits); it is not guaranteed unlimited service.

**Fresh test status: awaiting an OpenRouter key. No inference request was sent and no fresh NVIDIA narration was generated.** The local shell has no `OPENROUTER_API_KEY`; the configured API user-secret AI key is not an OpenRouter-format key. No secret values were printed or sent to another provider. There are no NVIDIA-tagged bilingual narration rows in the exported analysis table, so the archived rationales above must not be presented as successful English/German app narrations.

The read-only probe has prepared inputs for:

1. Eldense vs Oviedo — 2 October, 18:30 UTC.
2. Chesterfield vs Tranmere — 3 October, 11:30 UTC.

See [prepared evidence and explicit missing-key status](nvidia-response-probe-2026-09-30.json). The probe reselects eligible upcoming fixtures at execution time; refresh the context export if they have started. It sends historical/ML probabilities, team goal statistics, saved app probabilities and the timestamped raw provider opinion. Its requested output is AI goal probabilities, expected home/away goals, rationale, and English/German narration.

This is an explicitly labelled **contextual-AI prototype**, not the production narration prompt or an accuracy backtest. Provider opinions remain evidence, not invented fourth-source goal probabilities. The probe accepts only an explicit NVIDIA `:free` model, checks current zero pricing, caps provider token prices at zero, disables reasoning, uses no model fallback or automatic retries, saves raw successful response bodies before parsing, and rejects invalid numbers instead of clamping. It never writes to the application database or publishes a model. Narration language, factual consistency and probability coherence still need human review after a successful request.

To run with a locally configured `OPENROUTER_API_KEY` (never paste the key into chat):

```sh
cd /Users/shivm/Workspace/soccer-ai-api
PYTHONDONTWRITEBYTECODE=1 python3 tools/ml/probe_nvidia.py \
  --input-dir /tmp/soccer-nvidia-context-2026-09-29 \
  --goals /tmp/soccer-source-goals-results/source-goals.json \
  --output docs/ml/nvidia-response-probe-2026-09-30.json
```

## Better four-source design

1. **Freeze inputs before kickoff:** retain immutable historical/ML distributions, provider payload plus capture time, AI prompt/model/version, raw response, validation outcome, and final decision. Use one decision timestamp per fixture and only evidence available by that time.
2. **Use a common goal-market representation:** derive BTTS and Over 2.5 from historical and ML score distributions. Fit a provider-to-goal-market adapter only after collecting valid pre-match provider history; do not average provider attack ratings or 1X2 percentages into goal probabilities.
3. **Treat AI as a correlated contextual signal:** if it sees the other models, it is not an independent fourth vote. Validate schema, ranges, implied-goal consistency and factual grounding. Keep invalid or unavailable AI out of the blend with an explicit missing-source reason.
4. **Fit weights before testing:** compare the existing app, historical/ML baseline, provider addition and AI addition using chronological, out-of-fold predictions and separate per-market calibration. Fit a regularized stacking model or constrained blend on earlier data, not equal weights selected from this five-week result. Include missing-source indicators and a deterministic fallback.
5. **Narrate the final decision last:** after the numerical ensemble is frozen, ask AI to explain those exact probabilities and the disagreements without changing them. This prevents an independent AI opinion from contradicting the final user-facing prediction.
6. **Shadow-test before replacing the app:** measure Brier/log loss, calibration, paired accuracy changes, source coverage, latency/failure rate and qualified-pick performance with timestamped odds. Promote only on a later untouched cohort with enough observations, not this 20-fixture sample.

## Reproduction and validation

The read-only source export is `/tmp/soccer-four-source-2026-09-29`. Native ML forecasts are `/tmp/soccer-source-goals-results/source-goals.json`; the reports record their hashes. Raw database exports remain local rather than being added to the repository.

```sh
dotnet publish src/soccer-ai-tools -c Release -r linux-x64 --self-contained false \
  -o /tmp/soccer-source-audit-publish
docker run --rm --platform linux/amd64 --network none --cpus 4 --memory 4g \
  --entrypoint dotnet -e DOTNET_PROCESSOR_COUNT=4 \
  -v /tmp/soccer-source-audit-publish:/audit:ro \
  -v /tmp/soccer-four-source-2026-09-29:/input:ro \
  -v /tmp/soccer-source-goals-results:/output \
  soccer-ai-worker:lightgbm-check /audit/soccer-ai-tools.dll audit-source-goals \
  --input=/input/fixtures.json --output=/output/source-goals.json \
  --settings=/audit/appsettings.json --from=2026-08-25T00:00:00Z --until=2026-10-10T00:00:00Z
PYTHONDONTWRITEBYTECODE=1 python3 tools/ml/audit_source_combinations.py \
  --input-dir /tmp/soccer-four-source-2026-09-29 \
  --goals /tmp/soccer-source-goals-results/source-goals.json \
  --output docs/ml/source-combination-experiment-2026-09-29
dotnet test tests/soccer-ai-unit-tests --no-restore
PYTHONDONTWRITEBYTECODE=1 python3 -m unittest discover -s tests/ml -v
```

The native command requires the existing local Linux x64 image with LightGBM runtime dependencies; the ARM offline fallback is not the trainer used for the reported scores. There are no fixtures in the two-hour gap between the Berlin evaluation start and the UTC training cutoff used above. Outcomes after the evaluation end are excluded from all reported metrics.

Validation completed: **783 .NET tests passed, 1 existing PostgreSQL integration test skipped; 6 Python tests passed.** The new holdout test verifies that changing holdout labels cannot change the fitted model/calibration or the first holdout fixture's own forecast. Python tests verify paired scoring, export checksums, future-evidence rejection, no paid-model fallback, and strict response validation. No changes were deployed and no ensemble was promoted.
