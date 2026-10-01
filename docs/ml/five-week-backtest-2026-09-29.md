# Five-week BTTS and Over 2.5 backtest

Period: **2026-08-25 through 2026-09-28**, Europe/Berlin.

**335 scored fixtures out of 710 finished fixtures**. 375 have no eligible saved pre-match prediction. 0 have invalid probabilities.

Source: read-only PostgreSQL export, captured 2026-09-29T20:47:12.050421+00:00.

## Recorded prediction performance

Accuracy scores both Yes and No at 50%. Yes hit rate scores only Yes forecasts; it is not the selected-bet win rate.

| Market | Matches | Correct | Accuracy | 95% interval | Yes wins / calls | Yes hit rate | Always-Yes accuracy | Brier |
|---|---:|---:|---:|---|---:|---:|---:|---:|
| Goal Goal / BTTS | 335 | 204 | 60.9% | 55.6%–66.0% | 195 / 320 | 60.9% | 60.0% | 0.2396 |
| Over 2.5 | 335 | 203 | 60.6% | 55.3%–65.7% | 141 / 227 | 62.1% | 55.8% | 0.2363 |

Lower Brier is better. A constant 50% forecast scores 0.25.

## Recorded selections

Selections use the saved decision after its evidence gates. Skipped markets are abstentions. ROI is a flat-stake simulation using recorded prices, not actual account or published-ticket returns.

| Market | Audited matches | Selections | Wins | Losses | Hit rate | Fresh-priced selections | Profit (1-unit stake) | ROI |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| btts | 185 | 36 | 21 | 15 | 58.3% | 24 | +1.23 | 5.1% |
| over25 | 185 | 45 | 27 | 18 | 60.0% | 27 | +1.17 | 4.3% |

## Week by week

| Week | Finished | Scored | BTTS accuracy | BTTS Yes hit rate | Over 2.5 accuracy | Over Yes hit rate |
|---|---:|---:|---:|---:|---:|---:|
| 2026-08-25–2026-08-31 | 159 | 0 | — | — | — | — |
| 2026-09-01–2026-09-07 | 183 | 0 | — | — | — | — |
| 2026-09-08–2026-09-14 | 154 | 133 | 57.9% | 57.0% | 63.2% | 63.6% |
| 2026-09-15–2026-09-21 | 176 | 164 | 62.2% | 63.1% | 56.7% | 59.1% |
| 2026-09-22–2026-09-28 | 38 | 38 | 65.8% | 65.7% | 68.4% | 70.8% |

## AI forecast comparison

Each row compares AI and its stored statistical inputs on exactly the same fixtures. Models cannot be ranked across different cohorts.

| Recorded model | Paired matches | BTTS: statistics → AI | Over 2.5: statistics → AI | BTTS Brier: statistics → AI | Over Brier: statistics → AI |
|---|---:|---:|---:|---:|---:|
| anthropic/claude-sonnet-5 | 0 | — → — | — → — | — → — | — → — |
| nvidia/nemotron-3-ultra-550b-a55b:free | 99 | 57.6% → 51.5% | 56.6% → 52.5% | 0.2445 → 0.3261 | 0.2345 → 0.3474 |
| z-ai/glm-5.2:free | 117 | 65.0% → 64.1% | 57.3% → 60.7% | 0.2334 → 0.2406 | 0.2439 → 0.2509 |

## Method and limits

Latest immutable snapshot at least one hour before actual kickoff; one per FT fixture. Both market probabilities must be finite in [0,1]. No historical forecasts regenerated. Classification uses p>=0.5. Qualified=false is an abstention, not a No prediction. Only pre-capture decision audits count for selections. ROI uses unit stakes on qualified selections with recorded fresh odds; no imputed odds.

Scored versions: dixon-coles (150), dixon-coles-market-v1 (185).

- Coverage is limited to fixtures and genuine pre-match records in the exported database.
- Versions and selection rules changed during the period; these are recorded-system results, not validation of one fixed new model.
- AI comparisons use the same fixtures within each model, but different models may have different fixture cohorts.
- AI saw statistical probabilities, so it is not an independent voter; the forecast ledger lacks prompt/input hashes.
- Nominal Wilson intervals do not account for dependence between fixtures.
- Priced selections are a subset; their ROI cannot establish profitability across unpriced selections.

The adjacent JSON report contains weekly counts, selection confidence intervals, mean probabilities, base rates, exclusions, source hashes, and the exact scored fixture/snapshot IDs.
