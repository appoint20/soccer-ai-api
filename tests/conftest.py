"""
Pytest configuration and fixtures for Phase 1 validation tests.

This module provides shared fixtures, sample data, and utilities
for testing the Soccer Predictor foundation components.
"""
import json
import shutil
import tempfile
from datetime import date, time, datetime, timedelta
from pathlib import Path
from typing import Generator

import pandas as pd
import pytest

# Add project root to path
import sys
project_root = Path(__file__).parent.parent
sys.path.insert(0, str(project_root))

from src.domain.entities import Match, Team, Prediction, TeamStats, HomeAwayStats
from src.data.storage import JSONStorage


# =============================================================================
# Directory Fixtures
# =============================================================================

@pytest.fixture(scope="session")
def project_root_dir() -> Path:
    """Return the project root directory."""
    return Path(__file__).parent.parent


@pytest.fixture
def test_data_dir(tmp_path: Path) -> Path:
    """Create and return a temporary directory for test data."""
    data_dir = tmp_path / "test_data"
    data_dir.mkdir(parents=True, exist_ok=True)
    return data_dir


@pytest.fixture
def cleanup_test_data(test_data_dir: Path) -> Generator[Path, None, None]:
    """Fixture that cleans up test data after tests."""
    yield test_data_dir
    if test_data_dir.exists():
        shutil.rmtree(test_data_dir)


# =============================================================================
# Sample Match Data Fixtures
# =============================================================================

@pytest.fixture
def sample_match_home_win() -> dict:
    """Sample match data for a home win scenario."""
    return {
        "id": "match-001",
        "home_team": "Arsenal",
        "away_team": "Chelsea",
        "match_date": date(2024, 9, 15),
        "match_time": time(15, 0),
        "league": "E0",
        "season": "2024-25",
        "fthg": 3,
        "ftag": 1,
        "ftr": "H",
        "hthg": 1,
        "htag": 0,
        "htr": "H",
        "hs": 18,
        "as_": 8,
        "hst": 7,
        "ast": 3,
        "hf": 12,
        "af": 14,
        "hc": 6,
        "ac": 4,
        "hy": 2,
        "ay": 3,
        "hr": 0,
        "ar": 0,
        "referee": "Michael Oliver",
        "b365h": 1.85,
        "b365d": 3.60,
        "b365a": 4.20,
        "b365_over25": 1.72,
        "b365_under25": 2.10,
    }


@pytest.fixture
def sample_match_away_win() -> dict:
    """Sample match data for an away win scenario."""
    return {
        "id": "match-002",
        "home_team": "Burnley",
        "away_team": "Manchester City",
        "match_date": date(2024, 9, 16),
        "match_time": time(14, 0),
        "league": "E0",
        "season": "2024-25",
        "fthg": 0,
        "ftag": 4,
        "ftr": "A",
        "hthg": 0,
        "htag": 2,
        "htr": "A",
        "hs": 5,
        "as_": 22,
        "hst": 1,
        "ast": 10,
        "hf": 15,
        "af": 8,
        "hc": 2,
        "ac": 9,
        "hy": 4,
        "ay": 1,
        "hr": 0,
        "ar": 0,
        "referee": "Anthony Taylor",
        "b365h": 9.00,
        "b365d": 5.00,
        "b365a": 1.28,
        "b365_over25": 1.50,
        "b365_under25": 2.50,
    }


@pytest.fixture
def sample_match_draw() -> dict:
    """Sample match data for a draw scenario."""
    return {
        "id": "match-003",
        "home_team": "Newcastle",
        "away_team": "Everton",
        "match_date": date(2024, 9, 17),
        "match_time": time(20, 0),
        "league": "E0",
        "season": "2024-25",
        "fthg": 1,
        "ftag": 1,
        "ftr": "D",
        "hthg": 0,
        "htag": 1,
        "htr": "A",
        "hs": 14,
        "as_": 10,
        "hst": 5,
        "ast": 4,
        "hf": 11,
        "af": 13,
        "hc": 5,
        "ac": 3,
        "hy": 2,
        "ay": 2,
        "hr": 0,
        "ar": 0,
        "referee": "Chris Kavanagh",
        "b365h": 2.20,
        "b365d": 3.40,
        "b365a": 3.10,
        "b365_over25": 1.90,
        "b365_under25": 1.90,
    }


@pytest.fixture
def sample_match_zero_zero() -> dict:
    """Sample match data for 0-0 draw (edge case)."""
    return {
        "id": "match-004",
        "home_team": "Wolves",
        "away_team": "Crystal Palace",
        "match_date": date(2024, 9, 18),
        "match_time": time(15, 0),
        "league": "E0",
        "season": "2024-25",
        "fthg": 0,
        "ftag": 0,
        "ftr": "D",
        "hthg": 0,
        "htag": 0,
        "htr": "D",
        "hs": 8,
        "as_": 7,
        "hst": 2,
        "ast": 2,
        "hf": 10,
        "af": 12,
        "hc": 4,
        "ac": 3,
        "hy": 1,
        "ay": 2,
        "hr": 0,
        "ar": 0,
        "referee": "Simon Hooper",
        "b365h": 2.50,
        "b365d": 3.20,
        "b365a": 2.80,
        "b365_over25": 2.10,
        "b365_under25": 1.72,
    }


@pytest.fixture
def sample_match_high_scoring() -> dict:
    """Sample match data for high-scoring match (edge case)."""
    return {
        "id": "match-005",
        "home_team": "Liverpool",
        "away_team": "Tottenham",
        "match_date": date(2024, 9, 19),
        "match_time": time(17, 30),
        "league": "E0",
        "season": "2024-25",
        "fthg": 4,
        "ftag": 3,
        "ftr": "H",
        "hthg": 2,
        "htag": 2,
        "htr": "D",
        "hs": 24,
        "as_": 18,
        "hst": 12,
        "ast": 9,
        "hf": 8,
        "af": 10,
        "hc": 8,
        "ac": 6,
        "hy": 1,
        "ay": 2,
        "hr": 0,
        "ar": 0,
        "referee": "Michael Oliver",
        "b365h": 1.65,
        "b365d": 4.00,
        "b365a": 5.00,
        "b365_over25": 1.45,
        "b365_under25": 2.75,
    }


@pytest.fixture
def sample_match_missing_optional() -> dict:
    """Sample match with missing optional fields."""
    return {
        "id": "match-006",
        "home_team": "West Ham",
        "away_team": "Brentford",
        "match_date": date(2024, 9, 20),
        "league": "E0",
        "season": "2024-25",
        "fthg": 2,
        "ftag": 1,
        "ftr": "H",
        # Missing: time, half-time stats, shots, fouls, corners, cards, referee, odds
    }


# =============================================================================
# Sample DataFrame Fixtures
# =============================================================================

@pytest.fixture
def sample_historical_df() -> pd.DataFrame:
    """
    Create a sample historical DataFrame with matches from all 10 leagues.
    Contains 30+ matches with various scenarios.
    """
    data = []
    
    # League configurations
    leagues = {
        "E0": [("Arsenal", "Chelsea"), ("Liverpool", "Man City"), ("Man United", "Tottenham")],
        "E1": [("Leeds", "Leicester"), ("Southampton", "West Brom"), ("Norwich", "Watford")],
        "E2": [("Bolton", "Derby"), ("Peterborough", "Oxford"), ("Barnsley", "Bristol City")],
        "E3": [("Crewe", "Gillingham"), ("Walsall", "Tranmere"), ("Bradford", "Doncaster")],
        "D1": [("Bayern Munich", "Dortmund"), ("Leverkusen", "RB Leipzig"), ("Frankfurt", "Wolfsburg")],
        "F1": [("Paris SG", "Marseille"), ("Lyon", "Monaco"), ("Lille", "Nice")],
        "F2": [("Bordeaux", "Metz"), ("Troyes", "Guingamp"), ("Caen", "Auxerre")],
        "I1": [("Juventus", "Inter"), ("Milan", "Roma"), ("Napoli", "Lazio")],
        "I2": [("Palermo", "Bari"), ("Parma", "Brescia"), ("Genoa", "Sampdoria")],
        "SP1": [("Real Madrid", "Barcelona"), ("Ath Madrid", "Sevilla"), ("Valencia", "Villarreal")],
    }
    
    seasons = ["2022-23", "2023-24", "2024-25"]
    base_date = date(2022, 8, 15)
    match_id = 1
    
    for league, teams in leagues.items():
        for season_idx, season in enumerate(seasons):
            for home, away in teams:
                # Generate realistic scores
                fthg = (match_id % 4)
                ftag = (match_id % 3)
                
                if fthg > ftag:
                    ftr = "H"
                elif ftag > fthg:
                    ftr = "A"
                else:
                    ftr = "D"
                
                match_date = base_date + timedelta(days=match_id * 7 + season_idx * 365)
                
                data.append({
                    "Div": league,
                    "Date": match_date.strftime("%d/%m/%Y"),
                    "Time": "15:00",
                    "HomeTeam": home,
                    "AwayTeam": away,
                    "FTHG": fthg,
                    "FTAG": ftag,
                    "FTR": ftr,
                    "HTHG": fthg // 2,
                    "HTAG": ftag // 2,
                    "HTR": "D" if fthg // 2 == ftag // 2 else ("H" if fthg // 2 > ftag // 2 else "A"),
                    "HS": 12 + match_id % 8,
                    "AS": 10 + match_id % 6,
                    "HST": 5 + match_id % 4,
                    "AST": 4 + match_id % 3,
                    "HF": 10 + match_id % 5,
                    "AF": 11 + match_id % 4,
                    "HC": 4 + match_id % 4,
                    "AC": 3 + match_id % 3,
                    "HY": match_id % 3,
                    "AY": match_id % 4,
                    "HR": 0,
                    "AR": 0,
                    "Referee": f"Referee{match_id % 10}",
                    "B365H": 1.80 + (match_id % 10) / 10,
                    "B365D": 3.40,
                    "B365A": 4.00 - (match_id % 10) / 10,
                    "B365>2.5": 1.75,
                    "B365<2.5": 2.05,
                })
                match_id += 1
    
    return pd.DataFrame(data)


@pytest.fixture
def sample_upcoming_df() -> pd.DataFrame:
    """Create sample upcoming fixtures DataFrame."""
    today = date.today()
    
    data = [
        {"Date": (today + timedelta(days=1)).strftime("%Y-%m-%d"), "Time": "15:00", 
         "HomeTeam": "Arsenal", "AwayTeam": "Liverpool", "League": "E0"},
        {"Date": (today + timedelta(days=1)).strftime("%Y-%m-%d"), "Time": "17:30",
         "HomeTeam": "Chelsea", "AwayTeam": "Man City", "League": "E0"},
        {"Date": (today + timedelta(days=2)).strftime("%Y-%m-%d"), "Time": "14:00",
         "HomeTeam": "Leeds", "AwayTeam": "Leicester", "League": "E1"},
        {"Date": (today + timedelta(days=2)).strftime("%Y-%m-%d"), "Time": "20:00",
         "HomeTeam": "Bayern Munich", "AwayTeam": "Dortmund", "League": "D1"},
        {"Date": (today + timedelta(days=3)).strftime("%Y-%m-%d"), "Time": "21:00",
         "HomeTeam": "Real Madrid", "AwayTeam": "Barcelona", "League": "SP1"},
    ]
    
    return pd.DataFrame(data)


# =============================================================================
# Sample File Fixtures
# =============================================================================

@pytest.fixture
def sample_excel_file(test_data_dir: Path, sample_historical_df: pd.DataFrame) -> Path:
    """Create a sample Excel file with historical data."""
    file_path = test_data_dir / "E0_2425.xlsx"
    # Filter for E0 only
    df_e0 = sample_historical_df[sample_historical_df["Div"] == "E0"].copy()
    df_e0.to_excel(file_path, index=False)
    return file_path


@pytest.fixture
def sample_csv_file(test_data_dir: Path, sample_upcoming_df: pd.DataFrame) -> Path:
    """Create a sample CSV file with upcoming fixtures."""
    file_path = test_data_dir / "upcoming.csv"
    sample_upcoming_df.to_csv(file_path, index=False)
    return file_path


@pytest.fixture
def sample_json_file(test_data_dir: Path) -> Path:
    """Create a sample JSON file with matches."""
    file_path = test_data_dir / "matches.json"
    data = [
        {
            "id": "test-1",
            "home_team": "Arsenal",
            "away_team": "Chelsea",
            "match_date": "2024-09-15",
            "league": "E0",
            "season": "2024-25",
            "fthg": 2,
            "ftag": 1,
            "ftr": "H",
        }
    ]
    with open(file_path, "w") as f:
        json.dump(data, f)
    return file_path


@pytest.fixture
def corrupt_excel_file(test_data_dir: Path) -> Path:
    """Create a corrupt Excel file for error testing."""
    file_path = test_data_dir / "corrupt.xlsx"
    with open(file_path, "wb") as f:
        f.write(b"This is not a valid Excel file content")
    return file_path


@pytest.fixture
def missing_columns_excel(test_data_dir: Path) -> Path:
    """Create an Excel file missing required columns."""
    file_path = test_data_dir / "missing_cols.xlsx"
    df = pd.DataFrame({
        "Date": ["2024-09-15"],
        "HomeTeam": ["Arsenal"],
        # Missing AwayTeam column
    })
    df.to_excel(file_path, index=False)
    return file_path


@pytest.fixture
def empty_excel_file(test_data_dir: Path) -> Path:
    """Create an empty Excel file."""
    file_path = test_data_dir / "empty.xlsx"
    df = pd.DataFrame()
    df.to_excel(file_path, index=False)
    return file_path


@pytest.fixture
def corrupt_json_file(test_data_dir: Path) -> Path:
    """Create a corrupt JSON file for error testing."""
    file_path = test_data_dir / "corrupt.json"
    with open(file_path, "w") as f:
        f.write("{invalid json content")
    return file_path


# =============================================================================
# Entity Fixtures
# =============================================================================

@pytest.fixture
def sample_match(sample_match_home_win: dict) -> Match:
    """Create a sample Match entity."""
    return Match(
        id=sample_match_home_win["id"],
        home_team=sample_match_home_win["home_team"],
        away_team=sample_match_home_win["away_team"],
        match_date=sample_match_home_win["match_date"],
        match_time=sample_match_home_win["match_time"],
        league=sample_match_home_win["league"],
        season=sample_match_home_win["season"],
        fthg=sample_match_home_win["fthg"],
        ftag=sample_match_home_win["ftag"],
        ftr=sample_match_home_win["ftr"],
        hthg=sample_match_home_win["hthg"],
        htag=sample_match_home_win["htag"],
        htr=sample_match_home_win["htr"],
        hs=sample_match_home_win["hs"],
        as_=sample_match_home_win["as_"],
        hst=sample_match_home_win["hst"],
        ast=sample_match_home_win["ast"],
        referee=sample_match_home_win["referee"],
        b365h=sample_match_home_win["b365h"],
        b365d=sample_match_home_win["b365d"],
        b365a=sample_match_home_win["b365a"],
        b365_over25=sample_match_home_win["b365_over25"],
        b365_under25=sample_match_home_win["b365_under25"],
    )


@pytest.fixture
def sample_team() -> Team:
    """Create a sample Team entity."""
    home_stats = HomeAwayStats(
        matches_played=10,
        goals_scored=18,
        goals_conceded=8,
        wins=6,
        draws=2,
        losses=2,
        clean_sheets=4,
        failed_to_score=1,
        over_25_count=6,
        btts_count=5,
    )
    away_stats = HomeAwayStats(
        matches_played=10,
        goals_scored=12,
        goals_conceded=10,
        wins=4,
        draws=3,
        losses=3,
        clean_sheets=2,
        failed_to_score=2,
        over_25_count=5,
        btts_count=6,
    )
    
    return Team(
        name="Arsenal",
        league="E0",
        stats=TeamStats(home=home_stats, away=away_stats),
        last_5_results=["W", "W", "D", "L", "W"],
        current_position=3,
    )


@pytest.fixture
def sample_prediction() -> Prediction:
    """Create a sample Prediction entity."""
    from src.domain.entities.prediction import ResultProbabilities
    
    return Prediction(
        match_id="match-001",
        model_version="1.0.0",
        over25_prediction=True,
        over25_probability=0.72,
        over25_confidence="high",
        btts_prediction=True,
        btts_probability=0.65,
        btts_confidence="medium",
        result_prediction="H",
        result_probabilities=ResultProbabilities(
            home_win=0.55,
            draw=0.25,
            away_win=0.20,
        ),
    )


# =============================================================================
# Edge Case Fixtures
# =============================================================================

@pytest.fixture
def edge_case_matches() -> list[dict]:
    """Collection of edge case match scenarios."""
    return [
        # 0-0 draw
        {
            "home_team": "Team A",
            "away_team": "Team B",
            "match_date": date(2024, 1, 1),
            "league": "E0",
            "season": "2023-24",
            "fthg": 0,
            "ftag": 0,
            "ftr": "D",
        },
        # High scoring
        {
            "home_team": "Team C",
            "away_team": "Team D",
            "match_date": date(2024, 1, 2),
            "league": "E0",
            "season": "2023-24",
            "fthg": 6,
            "ftag": 4,
            "ftr": "H",
        },
        # Missing referee
        {
            "home_team": "Team E",
            "away_team": "Team F",
            "match_date": date(2024, 1, 3),
            "league": "E0",
            "season": "2023-24",
            "fthg": 1,
            "ftag": 0,
            "ftr": "H",
            "referee": None,
        },
        # Special characters in team names
        {
            "home_team": "FC Köln",
            "away_team": "Atlético Madrid",
            "match_date": date(2024, 1, 4),
            "league": "D1",
            "season": "2023-24",
            "fthg": 2,
            "ftag": 2,
            "ftr": "D",
        },
        # Minimal fields only
        {
            "home_team": "Team G",
            "away_team": "Team H",
            "match_date": date(2024, 1, 5),
            "league": "E0",
            "season": "2023-24",
        },
    ]


# =============================================================================
# Utility Fixtures
# =============================================================================

@pytest.fixture
def json_storage() -> JSONStorage:
    """Create a JSONStorage instance."""
    return JSONStorage()


@pytest.fixture
def supported_leagues() -> list[str]:
    """List of supported league codes."""
    return ["E0", "E1", "E2", "E3", "D1", "F1", "F2", "I1", "I2", "SP1"]
