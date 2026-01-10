# SoccerGPT API

This project provides an advanced AI-powered football match analysis and prediction API. It combines statistical models (Poisson, Monte Carlo, Team Form, H2H) with Large Language Models (LLMs) to generate "fair" odds, probabilities, and betting insights.

## Project Overview

**Goal:** To provide professional-grade football match analysis that relies on data-driven probabilities rather than gut feeling.  
**Core Features:**

- **Hybrid Analysis:** Combines statistical math models with AI reasoning.
- **Backtesting:** Ability to "time travel" and analyze past matches to verify prediction accuracy.
- **Canonical Data Structure:** A strict, single source of truth for match analysis throughout the entire pipeline (Domain -> AI -> API).
- **Time-Aware Analysis:** Ensures no data leakage for historical analysis; only uses data available *before* the match started.

## Endpoints

### 1. `GET /matches/analyze`

**Description:**  
The main workhorse endpoint. Analyzes upcoming matches for a specific date or runs a backtest verification for a past date.

**Business Logic:**

1. **Data Loading:** Fetches matches for the requested date. If the date is in the past, it loads "snapshot" data from that day to ensure historical accuracy.
2. **Statistical Modeling:**
    - **Form Calculation:** Computes 5-match and 3-match rolling form stats (Win %, BTTS %, Over 2.5 %, etc.).
    - **H2H Analysis:** Analyzes the last 5 head-to-head encounters.
    - **Poisson Distribution:** Calculates expected goal probabilities based on attack/defense strengths.
    - **Monte Carlo Simulation:** Runs 10,000 match simulations to adjust for variance and uncertainty.
    - **Confidence Scoring:** Assigns an overall confidence score (0-100) based on model agreement and data reliability.
3. **AI Enrichment:**
    - Sends the *canonical* analysis object to an LLM (Gemini/OpenAI).
    - The LLM generates a "Best Prediction", "Reasoning", and "Verdict" based *strictly* on the provided stats.
    - **Note:** AI analysis is mandatory and performed for every request.
4. **Backtesting (Time Travel):**
    - If the requested date is in the past, the system compares its predictions against the *actual* final score.
    - It returns a `backtest_result` object indicating if the prediction was correct and providing an accuracy report.
5. **Response:** Returns a standard `AnalyzeResponse` containing a list of `MatchAnalysis` objects.

**Query Parameters:**

- `date` (required): YYYY-MM-DD string.
- `page` (optional): Page number (default: 1).
- `limit` (optional): Items per page (default: 50).
- `refresh` (optional): `true` to bypass cache and force re-analysis (default: `false`).

**Example Response (Truncated):**

```json
{
  "total": 12,
  "generated_at": "2025-10-25T14:30:00.123456",
  "is_past_date": false,
  "items": [
    {
      "match_id": "eng_pl_2025_arsenal_vs_chelsea",
      "home_team": "Arsenal",
      "away_team": "Chelsea",
      "date": "2025-10-25",
      "overall_confidence": 78.5,
      "homeStats": {
        "last_5": { "win_rate": 0.8, "btts_rate": 0.6, "over_25_rate": 0.4 },
        "position": 2,
        "points": 24
      },
      "ai_analysis": {
        "best_prediction": "Home Win",
        "reason": "Arsenal's home form is dominant with 2.4 xG per game...",
        "confidence_level": "HIGH"
      },
      "match_analysis": {
        "over_25": { "probability": 0.65, "verdict": "Likely" },
        "btts": { "probability": 0.55, "verdict": "Neutral" }
      }
    }
  ]
}
```

### 2. `POST /tickets/generate`

**Description:**  
Generates betting "tickets" (accumulators/parlays) based on the analyzed matches.

**Business Logic:**

1. **Filtering:** Selects high-confidence matches from the analyzed pool.
2. **Strategy Application:** Applies specific betting strategies (e.g., "Safe Accumulator", "High Risk/High Reward", "Value Bets").
3. **Optimization:** Uses AI to select the optimal combination of bets to maximize expected value (EV) while keeping risk within user-defined bounds.

### 3. `GET /system/health`

**Description:**  
Simple health check to ensure API and dependencies (Database, AI Service) are operational.

---

## Data Structures & Cleanup Verification

**Cleanup Status:** ✅ **Verified**

- We have completely removed the generic `enrichment_data` dictionary that was causing data hiding.
- We have removed the redundant `aggregated_markets` field.
- **Single Source of Truth:** The system now uses a strict `MatchAnalysis` schema (defined in `src/api/schemas.py`) across all layers:
  - **Domain:** `SingleMatchAnalysis` (Canonical)
  - **AI Service:** Consumes `SingleMatchAnalysis`
  - **API Response:** Returns `MatchAnalysis` (identical structure)

**Canonical Object Structure:**
The `MatchAnalysis` object acts as the contract for the entire system:
- **Stats:** `homeStats`, `awayStats` (strictly typed `TeamStats` objects, no random dicts)
- **Models:** `poisson`, `monte_carlo` (Probability objects)
- **Results:** `match_analysis` (The aggregated verdict from all models)
- **AI:** `ai_analysis` (The LLM's interpretation)
