# Source-combination experiment

Evaluation: 25 August–28 September 2026. Same fixtures within every comparison.

**A historical four-source score is unavailable:** 0 finished fixtures have a provider record captured at least one hour before kickoff.

## Native ML experiment

Trainer: LightGbm. Fitting: 20498 matches. Separate calibration: 4727 matches. Calibration ends 2026-08-24T19:30:00Z. Learned ML weight inside the historical/ML mixture: 0.473.

### Historical data versus ML, same reconstructed input timing

| Market | Method | Matches | Accuracy | Brier | Improved / worsened calls |
|---|---|---:|---:|---:|---:|
| btts | historical | 702 | 58.8% | 0.2397 | 0 / 0 |
| btts | ml | 702 | 58.5% | 0.2433 | 70 / 72 |
| btts | historical_ml_fitted | 702 | 59.4% | 0.2406 | 35 / 31 |
| btts | historical_ml_equal | 702 | 59.8% | 0.2407 | 40 / 33 |
| over25 | historical | 702 | 56.8% | 0.2413 | 0 / 0 |
| over25 | ml | 702 | 55.8% | 0.2432 | 90 / 97 |
| over25 | historical_ml_fitted | 702 | 57.1% | 0.2410 | 46 / 44 |
| over25 | historical_ml_equal | 702 | 57.3% | 0.2410 | 47 / 44 |

### Comparison with the current app's saved probabilities

| Market | Method | Matches | Accuracy | Brier | Improved / worsened calls |
|---|---|---:|---:|---:|---:|
| btts | historical | 301 | 57.5% | 0.2396 | 6 / 20 |
| btts | ml | 301 | 58.1% | 0.2438 | 23 / 35 |
| btts | historical_ml_fitted | 301 | 59.5% | 0.2408 | 14 / 22 |
| btts | historical_ml_equal | 301 | 59.1% | 0.2409 | 13 / 22 |
| btts | current_app | 301 | 62.1% | 0.2366 | 0 / 0 |
| btts | current_app_ml_equal | 301 | 58.5% | 0.2393 | 7 / 18 |
| over25 | historical | 301 | 58.1% | 0.2378 | 20 / 26 |
| over25 | ml | 301 | 58.5% | 0.2443 | 34 / 39 |
| over25 | historical_ml_fitted | 301 | 59.1% | 0.2397 | 23 / 26 |
| over25 | historical_ml_equal | 301 | 59.5% | 0.2398 | 24 / 26 |
| over25 | current_app | 301 | 60.1% | 0.2369 | 0 / 0 |
| over25 | current_app_ml_equal | 301 | 61.5% | 0.2395 | 22 / 18 |

### nvidia/nemotron-3-ultra-550b-a55b:free — current app plus recorded AI

| Market | Method | Matches | Accuracy | Brier | Improved / worsened calls |
|---|---|---:|---:|---:|---:|
| btts | current_at_ai_capture | 99 | 57.6% | 0.2445 | 0 / 0 |
| btts | ai_only | 99 | 51.5% | 0.3261 | 9 / 15 |
| btts | current90_ai10 | 99 | 54.5% | 0.2463 | 3 / 6 |
| btts | current75_ai25 | 99 | 56.6% | 0.2517 | 6 / 7 |
| btts | current50_ai50 | 99 | 53.5% | 0.2677 | 8 / 12 |
| over25 | current_at_ai_capture | 99 | 56.6% | 0.2345 | 0 / 0 |
| over25 | ai_only | 99 | 52.5% | 0.3474 | 16 / 20 |
| over25 | current90_ai10 | 99 | 57.6% | 0.2383 | 6 / 5 |
| over25 | current75_ai25 | 99 | 53.5% | 0.2472 | 9 / 12 |
| over25 | current50_ai50 | 99 | 52.5% | 0.2702 | 12 / 16 |

### nvidia/nemotron-3-ultra-550b-a55b:free — three-source experiment (provider unavailable)

| Market | Method | Matches | Accuracy | Brier | Improved / worsened calls |
|---|---|---:|---:|---:|---:|
| btts | current_at_ai_capture | 20 | 65.0% | 0.2342 | 0 / 0 |
| btts | historical_ml_fitted | 20 | 65.0% | 0.2295 | 1 / 1 |
| btts | historical_ml_ai_equal | 20 | 70.0% | 0.2513 | 2 / 1 |
| btts | historical_ml90_ai10 | 20 | 70.0% | 0.2346 | 2 / 1 |
| over25 | current_at_ai_capture | 20 | 60.0% | 0.2129 | 0 / 0 |
| over25 | historical_ml_fitted | 20 | 70.0% | 0.2063 | 3 / 1 |
| over25 | historical_ml_ai_equal | 20 | 65.0% | 0.2168 | 2 / 1 |
| over25 | historical_ml90_ai10 | 20 | 65.0% | 0.2079 | 2 / 1 |

### z-ai/glm-5.2:free — current app plus recorded AI

| Market | Method | Matches | Accuracy | Brier | Improved / worsened calls |
|---|---|---:|---:|---:|---:|
| btts | current_at_ai_capture | 117 | 65.0% | 0.2334 | 0 / 0 |
| btts | ai_only | 117 | 64.1% | 0.2406 | 7 / 8 |
| btts | current90_ai10 | 117 | 65.0% | 0.2311 | 2 / 2 |
| btts | current75_ai25 | 117 | 65.8% | 0.2289 | 3 / 2 |
| btts | current50_ai50 | 117 | 66.7% | 0.2285 | 6 / 4 |
| over25 | current_at_ai_capture | 117 | 57.3% | 0.2439 | 0 / 0 |
| over25 | ai_only | 117 | 60.7% | 0.2509 | 13 / 9 |
| over25 | current90_ai10 | 117 | 61.5% | 0.2411 | 5 / 0 |
| over25 | current75_ai25 | 117 | 62.4% | 0.2384 | 9 / 3 |
| over25 | current50_ai50 | 117 | 63.2% | 0.2376 | 12 / 5 |

### z-ai/glm-5.2:free — three-source experiment (provider unavailable)

| Market | Method | Matches | Accuracy | Brier | Improved / worsened calls |
|---|---|---:|---:|---:|---:|
| btts | current_at_ai_capture | 16 | 75.0% | 0.2098 | 0 / 0 |
| btts | historical_ml_fitted | 16 | 68.8% | 0.2180 | 0 / 1 |
| btts | historical_ml_ai_equal | 16 | 68.8% | 0.1935 | 0 / 1 |
| btts | historical_ml90_ai10 | 16 | 68.8% | 0.2094 | 0 / 1 |
| over25 | current_at_ai_capture | 16 | 56.2% | 0.2453 | 0 / 0 |
| over25 | historical_ml_fitted | 16 | 50.0% | 0.2499 | 1 / 2 |
| over25 | historical_ml_ai_equal | 16 | 56.2% | 0.2311 | 2 / 2 |
| over25 | historical_ml90_ai10 | 16 | 56.2% | 0.2428 | 2 / 2 |

## Interpretation limits

- No four-source historical accuracy is reported without a pre-match provider record.
- ML is a newly reconstructed offline experiment using native LightGBM, not a historical production ML snapshot.
- ML trees, goal-rate scaling and historical/ML weights are fitted before the evaluation period. No model is published.
- All bookmaker features are masked in reconstructed components. Other historical features are reconstructed from the current export, not immutable as-of records.
- For comparisons against recorded forecasts, reconstructed components are included only when the recorded forecast was captured on the same UTC day as kickoff; reconstructed history freezes at day start.
- Fixed 10%, 25%, 50% AI blends and equal three-source weighting are sensitivity tests, not tuned production weights or an independent untouched benchmark.
- AI models have different fixture cohorts; compare candidates within each table, not model names across tables.
- Stored NVIDIA probabilities include many exact 1.0 values. The old parser clamped invalid values, and original raw responses were not retained, so model error and ingestion error cannot be separated.
- No betting profitability is inferred from classification accuracy.

Machine-readable results include all evaluated fixture IDs, source hashes, confidence intervals and selection counts.
