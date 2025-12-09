# Soccer-GPT API

FastAPI backend for football betting predictions combining ML, Poisson distribution, and Monte Carlo simulation.

## Quick Start

```bash
cd soccer-gpt-api
source venv/bin/activate
uvicorn main:app --reload --port 8000
```

## API Documentation

- **Swagger UI**: <http://localhost:8000/docs>
- **Scalar Docs**: <http://localhost:8000/scalar>

### 1. GET `/api/v1/leagues`

Get supported leagues.

```bash
curl http://localhost:8000/api/v1/leagues
```

### 2. POST `/api/v1/matches/analyze`

Analyze matches for a date (uses real fixtures data).

```bash
curl -X POST http://localhost:8000/api/v1/matches/analyze \
  -H "Content-Type: application/json" \
  -d '{"date": "2025-12-06"}'
```

**Response includes**:

- ML predictions with reasoning
- Poisson analysis (expected goals)
- Monte Carlo simulation (10k iterations)
- Pattern detection (STRONG_CONSENSUS = 69.3% accuracy)
- Trap detector (warning flags)
- ChatGPT analysis

### 3. GET `/api/v1/tickets/generate`

Generate betting tickets (3 games per ticket, €100 stake).

```bash
curl "http://localhost:8000/api/v1/tickets/generate?date=2025-12-06&min_confidence=0.65&max_tickets=4"
```

### 4. GET `/api/v1/backtest`

Historical performance with ROI calculation.

```bash
curl "http://localhost:8000/api/v1/backtest?weeks=15"
```

## Response Format (Pagination)

```json
{
  "offset": 0,
  "limit": 20,
  "total": 154,
  "items": [...]
}
```

## Model Performance

| Market | Accuracy |
|--------|----------|
| HDW (Consensus) | 69.3% |
| Over 1.5 | 78.5% |
| Over 2.5 | 75.3% |
| BTTS | 77.5% |

## Project Structure

```
soccer-gpt-api/
├── main.py              # FastAPI entry
├── app/
│   ├── api/routes/      # 4 endpoints
│   ├── core/            # Poisson, Monte Carlo, Trap Detector
│   └── schemas/         # Pydantic models
├── models/              # Trained ML models (V3)
└── data/
    ├── historical/      # Excel files
    ├── upcoming/        # Fixtures CSV
    └── leagues.json
```

## Data Files

- `data/upcoming/fixtures.csv` - Real upcoming fixtures with odds
- `data/historical/*.xlsx` - Historical match data (2019-2025)

## License

Private - Appoint. Project
