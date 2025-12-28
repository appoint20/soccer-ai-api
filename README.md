# 🎯 Soccer Predictor API

European Soccer Match Prediction System (MVP)

Predicts match outcomes for Over 2.5 Goals, BTTS, and Match Results using statistical analysis and machine learning.

## 🚀 Features

- **Primary Predictions**: Over 2.5 Goals (75% target), BTTS (80% target)
- **Secondary Predictions**: Win/Draw match results
- **10 Supported Leagues**: E0, E1, E2, E3 (England), D1 (Germany), F1, F2 (France), I1, I2 (Italy), SP1 (Spain)
- **Clean Architecture**: Domain-driven design with clear separation of concerns
- **JSON Storage**: Simple file-based storage for MVP

## 📁 Project Structure

```
soccer-predictor/
├── src/
│   ├── domain/
│   │   ├── entities/          # Match, Team, Prediction dataclasses
│   │   └── services/          # Business logic services
│   ├── data/
│   │   ├── loaders/           # Excel & CSV data loaders
│   │   └── storage/           # JSON file operations
│   └── utils/                 # Config, logging, helpers
├── data/
│   ├── raw/
│   │   ├── historical/        # Excel files with historical data
│   │   └── upcoming/          # CSV files with fixtures
│   ├── processed/             # JSON processed data
│   └── predictions/           # Generated predictions
├── models/                    # ML models (future phases)
├── config/                    # Configuration files
├── logs/                      # Application logs
├── tests/                     # Unit tests
├── scripts/                   # Utility scripts
├── requirements.txt
└── setup.py
```

## 🛠️ Installation

### Prerequisites

- Python 3.10+
- pip

### Setup

1. **Clone the repository**

   ```bash
   git clone <repository-url>
   cd soccer-predictor
   ```

2. **Create virtual environment**

   ```bash
   python -m venv venv
   source venv/bin/activate  # On Windows: venv\Scripts\activate
   ```

3. **Install dependencies**

   ```bash
   pip install -r requirements.txt
   ```

4. **Install package in development mode**

   ```bash
   pip install -e .
   ```

## 📊 Data Requirements

### Historical Data Format (Excel/CSV)

Place historical match data in `data/raw/historical/`. Files should follow football-data.co.uk format:

**Required columns:**

- `Date` - Match date
- `HomeTeam` - Home team name
- `AwayTeam` - Away team name

**Optional columns:**

- `Time` - Match time
- `FTHG`, `FTAG` - Full-time goals
- `FTR` - Full-time result (H/D/A)
- `HTHG`, `HTAG` - Half-time goals
- `HS`, `AS` - Shots
- `HST`, `AST` - Shots on target
- `HF`, `AF` - Fouls
- `HC`, `AC` - Corners
- `HY`, `AY` - Yellow cards
- `HR`, `AR` - Red cards
- `B365H`, `B365D`, `B365A` - Betting odds

**File naming convention:**

- `E0_2324.xlsx` → Premier League 2023-24
- `D1_2425.csv` → Bundesliga 2024-25

### Upcoming Fixtures Format (CSV)

Place fixture data in `data/raw/upcoming/`:

```csv
Date,Time,HomeTeam,AwayTeam,League
2024-12-28,15:00,Arsenal,Chelsea,E0
2024-12-28,17:30,Man City,Liverpool,E0
```

## 🚀 Usage

### Load Historical Data

```bash
python scripts/initial_data_load.py
```

This will:

1. Scan `data/raw/historical/` for Excel/CSV files
2. Process and validate data
3. Save to `data/processed/matches.json`
4. Print statistics about loaded data

### Run Tests

```bash
# Run all tests
pytest tests/ -v

# Run with coverage
pytest tests/ -v --cov=src
```

### Code Quality

```bash
# Format code
black src/ tests/ scripts/

# Lint code
flake8 src/ tests/ scripts/

# Type check
mypy src/
```

## 📋 Supported Leagues

| Code | League | Country |
|------|--------|---------|
| E0 | Premier League | England |
| E1 | Championship | England |
| E2 | League One | England |
| E3 | League Two | England |
| D1 | Bundesliga | Germany |
| F1 | Ligue 1 | France |
| F2 | Ligue 2 | France |
| I1 | Serie A | Italy |
| I2 | Serie B | Italy |
| SP1 | La Liga | Spain |

## 🔮 Next Steps (Phase 2)

- [ ] Team statistics calculation service
- [ ] Head-to-head analysis
- [ ] Poisson distribution model
- [ ] Over 2.5 and BTTS prediction
- [ ] API endpoints (FastAPI)

## 📝 Configuration

### Environment Variables (.env)

```env
ENVIRONMENT=development
LOG_LEVEL=DEBUG
DATA_RAW_PATH=data/raw
DATA_PROCESSED_PATH=data/processed
```

### Settings (config/settings.yaml)

```yaml
features:
  lookback_matches: 5
  min_matches_for_stats: 3

thresholds:
  over25:
    high_confidence: 0.70
    medium_confidence: 0.55
```

## 🧪 Testing

The test suite covers:

- Entity creation and serialization
- JSON storage operations
- Data loading and processing
- Helper utility functions

```bash
# Quick test
pytest tests/test_data_loading.py -v

# Full test with coverage
pytest tests/ -v --cov=src --cov-report=html
```

## 📄 License

MIT License

## 🤝 Contributing

1. Fork the repository
2. Create a feature branch
3. Make your changes
4. Run tests and linting
5. Submit a pull request
