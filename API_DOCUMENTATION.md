# Soccer GPT API Documentation

## Overview

A FastAPI-powered soccer match prediction service with ML-powered predictions for European football leagues.

**Base URL:** `http://localhost:8000`

---

## Quick Start

```bash
# Activate virtual environment
source venv/bin/activate

# Start the server
uvicorn src.api.main:app --reload --port 8000
```

---

## Startup Process

When the API starts, it automatically:

1. **Sanitizes Fixtures** - Converts `fixtures.xlsx` or `fixtures.csv` to clean `fixtures_clean.csv`
   - Keeps only supported leagues
   - Removes unused columns (104 → 10 columns)

2. **Loads ML Models** - Loads trained XGBoost models for Over 2.5, BTTS, and Result predictions

3. **Loads Historical Data** - Loads `data/processed/matches.json` for team statistics

---

## Supported Leagues

| Code | League |
|------|--------|
| E0 | England Premier League |
| E1 | England Championship |
| E2 | England League One |
| E3 | England League Two |
| D1 | Germany Bundesliga |
| F1 | France Ligue 1 |
| F2 | France Ligue 2 |
| I1 | Italy Serie A |
| I2 | Italy Serie B |
| SP1 | Spain La Liga |

---

## API Endpoints

### 1. Health Check

```
GET /health
```

Returns API status and version.

---

### 2. Analyze Matches

```http
GET /analyze/matches?date=2025-12-27&offset=0&limit=50
```

Analyzes all matches for a given date with pagination.

**Parameters:**

| Param | Default | Description |
|-------|---------|-------------|
| `date` | required | Date in YYYY-MM-DD format |
| `offset` | 0 | Pagination offset |
| `limit` | 50 | Items per page (max 100) |

**Response:**

```json
{
  "items": [
    {
      "match_id": "E0_2025-12-27_Liverpool_Wolves",
      "home_team": "Liverpool",
      "away_team": "Wolverhampton",
      "date": "2025-12-27",
      "time": "15:00",
      "league": "E0",
      "odds": {
        "home": 1.5,
        "draw": 4.0,
        "away": 6.0,
        "over25": 1.5,
        "btts": 1.8
      },
      "average": {
        "home_goal_avg": 2.1,
        "away_goal_avg": 1.3,
        "home_win_rate": 0.7,
        "away_win_rate": 0.3,
        "home_conceded_avg": 0.8,
        "away_conceded_avg": 1.5
      },
      "h2h": {
        "total_matches": 5,
        "home_wins": 3,
        "draws": 1,
        "away_wins": 1,
        "avg_goals": 3.2,
        "btts_rate": 0.6,
        "over25_rate": 0.8
      },
      "predictions": {
        "over25": {
          "prediction": "YES",
          "probability": 0.75,
          "confidence": "HIGH"
        },
        "btts": {
          "prediction": "YES",
          "probability": 0.68,
          "confidence": "MEDIUM"
        },
        "result": {
          "prediction": "H",
          "probabilities": {
            "home_win": 0.65,
            "draw": 0.25,
            "away_win": 0.10
          },
          "confidence": "HIGH"
        }
      },
      "poisson_distribution": {
        "home_win": 0.6,
        "draw": 0.25,
        "away_win": 0.15,
        "over25": 0.7,
        "btts": 0.65,
        "expected_home_goals": 2,
        "expected_away_goals": 1
      },
      "team_stats": {
        "btts": {
          "combined_pct": 68.9,
          "home_team": { "overall_9": {...}, "home_6": {...} },
          "away_team": { "overall_9": {...}, "away_6": {...} }
        },
        "over25": {
          "combined_pct": 72.2,
          "home_team": { "overall_9": {...}, "home_6": {...} },
          "away_team": { "overall_9": {...}, "away_6": {...} }
        },
        "qualification": {
          "over25_qualified": true,
          "btts_qualified": true
        }
      }
    }
  ],
  "total": 48,
  "generated_at": "2025-12-31T20:00:00"
}
```

**Note:** `offset` and `limit` only appear when explicitly provided in the request.

---

### 3. Generate Tickets

```http
GET /tickets/generate?date=2025-12-27&min_confidence=LOW&min_odds=1.50
```

Generates betting tickets from analyzed matches.

**Rules Applied:**

- ✅ Only **qualified** matches (teams with sufficient historical stats)
- ✅ **3 matches per ticket**
- ✅ **Max 2 games per league** per ticket
- ✅ **Mixed markets** (Over 2.5 + BTTS combined)
- ✅ **No duplicate matches** across tickets

**Parameters:**

| Param | Values | Description |
|-------|--------|-------------|
| `date` | YYYY-MM-DD | Match date |
| `min_confidence` | LOW, MEDIUM, HIGH | Minimum confidence threshold |
| `min_odds` | Decimal (e.g. 1.60) | Optional minimum odds filter |
| `max_odds` | Decimal (e.g. 2.80) | Optional maximum odds filter |

**Response:**

```json
{
  "date": "2026-01-01",
  "tickets": [
    {
      "ticket_id": "MIX-2026-01-01-1-abc1",
      "selections": [
        {
          "home_team": "Liverpool",
          "away_team": "Leeds",
          "league": "E0",
          "market": "over25",
          "odds": 1.62,
          "confidence": 0.98,
          "qualified": true
        }
      ],
      "combined_probability": 0.93
    }
  ]
}
```

---

### 4. Weekly Tickets

```
GET /tickets/weekly?week_start=2025-12-29
```

Generates 5 weekly tickets using historical match statistics.

**Rules:**

- 2 Mixed tickets (Home Win + Goals markets)
- 3 Goals-only tickets (Over 2.5 / BTTS)
- Min odds: 1.60 (goals), 2.0 (result)
- 2-day match window (busiest consecutive days)
- NO draw predictions

---

### 5. Backtest

```
GET /backtest/run?weeks=10
```

Runs backtesting simulation on historical data.

---

## Data Flow

```
┌─────────────────┐
│  fixtures.xlsx  │
│  or .csv        │
└────────┬────────┘
         │
         ▼
┌─────────────────┐
│ Excel Sanitizer │ ← Runs on startup
│ (clean data)    │
└────────┬────────┘
         │
         ▼
┌─────────────────┐
│ fixtures_clean  │ ← 10 columns, supported leagues only
│     .csv        │
└────────┬────────┘
         │
         ▼
┌─────────────────┐    ┌─────────────────┐
│  CSVLoader      │    │ Historical Data │
│ (upcoming)      │    │ (matches.json)  │
└────────┬────────┘    └────────┬────────┘
         │                      │
         └──────────┬───────────┘
                    ▼
           ┌─────────────────┐
           │  ML Prediction  │
           │    Service      │
           └────────┬────────┘
                    │
         ┌──────────┼──────────┐
         ▼          ▼          ▼
    ┌─────────┐ ┌─────────┐ ┌─────────┐
    │ Over2.5 │ │  BTTS   │ │ Result  │
    │  Model  │ │  Model  │ │  Model  │
    └─────────┘ └─────────┘ └─────────┘
                    │
                    ▼
           ┌─────────────────┐
           │ Match Stats     │ ← Calculates qualification
           │ Service         │   (historical Over2.5/BTTS %)
           └────────┬────────┘
                    │
                    ▼
           ┌─────────────────┐
           │ Ticket          │ ← Applies rules:
           │ Generation      │   3 per ticket, max 2/league
           └────────┬────────┘
                    │
                    ▼
              API Response
```

---

## Qualification Logic

A match is **qualified** if both teams meet historical thresholds:

**Over 2.5 Qualified:**

- Home team: >55% Over 2.5 in last 9 matches
- Away team: >55% Over 2.5 in last 9 matches

**BTTS Qualified:**

- Home team: >55% BTTS in last 9 matches
- Away team: >55% BTTS in last 9 matches

## Fallback Logic

If the ML model encounters an issue or is missing required model files, the system employs a **safe fallback mechanism**:

- **Probability**: `0.0` (indicates no model prediction available)
- **Confidence**: `LOW`
- **Prediction**: `NO` (or `D` for result)

This ensures that unreliable or estimated predictions are not mistaken for high-confidence model outputs. The `min_confidence` filter in ticket generation will automatically exclude these fallback results.

---

## Key Files

| File | Purpose |
|------|---------|
| `src/api/main.py` | FastAPI app, startup events |
| `src/api/routers/predictions.py` | All prediction endpoints |
| `src/domain/services/prediction_service.py` | ML prediction logic |
| `src/domain/services/match_stats_service.py` | Team stats, qualification |
| `src/domain/services/weekly_ticket_service.py` | Weekly ticket generation |
| `src/data/loaders/excel_sanitizer.py` | Excel/CSV cleanup |
| `src/data/loaders/csv_loader.py` | Load upcoming fixtures |
| `data/raw/upcoming/fixtures.csv` | Input fixture file |
| `data/raw/upcoming/fixtures_clean.csv` | Sanitized fixture file |
| `data/processed/matches.json` | Historical match data |
| `models/` | Trained ML models |

---

## API Documentation UIs

| URL | Type |
|-----|------|
| `/docs` | Swagger UI |
| `/redoc` | ReDoc |
| `/scalar` | Scalar (modern) |

---

## Essential Columns (Used by System)

```
Div, Date, Time, HomeTeam, AwayTeam, 
FTHG, FTAG, HTHG, HTAG, FTR,
B365H, B365D, B365A, B365>2.5, B365<2.5
```

All other columns from football-data.co.uk are automatically removed on startup.
