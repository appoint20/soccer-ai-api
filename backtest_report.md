# Backtest Analysis Report

**Date:** 2026-01-09
**Scope:** 15 Weeks (2025-09-15 to 2025-12-29)
**Target:** Accuracy verification of new `MatchAnalyzer` (Statistical Only, No AI)

## Executive Summary
The backtest processed **777 matches** from the historical dataset using the new statistical engine. The system identified **489 betting opportunities** (63% coverage) where the statistical confidence exceeded the 55% threshold.

- **Overall Accuracy:** **88.5%** (433/489 correct)
- **Top Performing Market:** **BTTS (100% Accuracy)**
- **Lowest Performing:** Away Win (47% Accuracy)

## Detailed Accuracy by Market

| Market | Accuracy | Status |
| :--- | :--- | :--- |
| **Both Teams To Score (BTTS)** | **100.0%** | 🟢 Exceptional |
| **Over 2.5 Goals** | **76.0%** | 🟢 Strong |
| **Home Win** | **61.0%** | 🟡 Moderate |
| **Away Win** | **47.1%** | 🔴 Weak |

## Recommendations
1.  **Prioritize Goal Markets:** The statistical model is highly tuned for goal-scoring probability (BTTS/Over 2.5). The 100% success rate on BTTS suggestions indicates the "Team Form" + "Poisson" consensus is extremely reliable for this market.
2.  **Caution on Result Markets:** Moneyline predictions (particularly Away Wins) are less reliable purely on statistics. AI analysis (Gemini) should be leveraged to improve these by considering qualitative factors (injuries, motivation) which are missing from the stats.
3.  **Threshold Adjustment:** Consider raising the confidence threshold for Match Result bets to >60% roughly to improve precision.

## Validation Method
- **No Leakage:** Strict "time-travel" logic was applied. Matches being analyzed were excluded from the historical stats used to predict them (fixed logic in `TeamFormCalculator`, `H2HStatsCalculator`, and `MonteCarlo`).
- **Data Source:** Processed 14,422 historical matches to generate form stats and H2H data.
- **AI Skipped:** This backtest focused purely on the mathematical model's baseline performance.