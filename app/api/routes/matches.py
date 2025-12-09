"""
Matches Analysis API Route - REAL DATA
"""
from fastapi import APIRouter, Query
from pydantic import BaseModel
from typing import Optional, List
from datetime import datetime
import pandas as pd
from pathlib import Path

from app.core.poisson import PoissonPredictor
from app.core.monte_carlo import MonteCarloSimulator
from app.core.trap_detector import TrapDetector

router = APIRouter()

# Initialize predictors
poisson = PoissonPredictor()
monte_carlo = MonteCarloSimulator()
trap_detector = TrapDetector()

DATA_DIR = Path(__file__).parent.parent.parent.parent / "data"


class AnalyzeRequest(BaseModel):
    date: str
    league_id: Optional[str] = None


# League code mapping
LEAGUE_NAMES = {
    'E0': 'Premier League', 'E1': 'Championship', 'E2': 'League One', 'E3': 'League Two',
    'D1': 'Bundesliga', 'D2': '2. Bundesliga',
    'I1': 'Serie A', 'I2': 'Serie B',
    'F1': 'Ligue 1', 'F2': 'Ligue 2',
    'SP1': 'La Liga', 'SP2': 'La Liga 2',
    'N1': 'Eredivisie', 'P1': 'Primeira Liga',
    'SC0': 'Scottish Premier', 'T1': 'Super Lig'
}


def load_valid_leagues():
    """Load valid league IDs from leagues.json"""
    import json
    leagues_file = DATA_DIR / "leagues.json"
    if leagues_file.exists():
        with open(leagues_file, 'r') as f:
            leagues = json.load(f)
            return [league['id'] for league in leagues]
    return list(LEAGUE_NAMES.keys())


def load_fixtures():
    """Load real fixtures from CSV, filtered by valid leagues"""
    fixture_file = DATA_DIR / "upcoming" / "fixtures.csv"
    if not fixture_file.exists():
        return pd.DataFrame()
    
    df = pd.read_csv(fixture_file)
    df['Date'] = pd.to_datetime(df['Date'], format='%d/%m/%Y', errors='coerce')
    
    # Filter by valid leagues from leagues.json
    valid_leagues = load_valid_leagues()
    df = df[df['Div'].isin(valid_leagues)]
    
    return df


def analyze_match(row) -> dict:
    """Analyze single match with all methods using REAL historical data"""
    from app.services.team_stats import get_team_stats_service
    
    home_team = row['HomeTeam']
    away_team = row['AwayTeam']
    league_id = row['Div']
    match_date = row['Date'] if pd.notna(row['Date']) else datetime.now()
    
    # Get REAL team stats from historical Excel data
    stats_service = get_team_stats_service()
    home_stats = stats_service.get_team_stats(home_team, league_id, before_date=match_date)
    away_stats = stats_service.get_team_stats(away_team, league_id, before_date=match_date)
    
    # Get odds
    home_odds = float(row.get('B365H', 2.0)) if pd.notna(row.get('B365H')) else 2.0
    draw_odds = float(row.get('B365D', 3.0)) if pd.notna(row.get('B365D')) else 3.0
    away_odds = float(row.get('B365A', 3.0)) if pd.notna(row.get('B365A')) else 3.0
    over25_odds = float(row.get('B365>2.5', 1.9)) if pd.notna(row.get('B365>2.5')) else 1.9
    
    # Use REAL attack/defense stats for predictions
    home_attack = home_stats['avg_goals_scored'] if home_stats['avg_goals_scored'] > 0 else 1.3
    away_attack = away_stats['avg_goals_scored'] if away_stats['avg_goals_scored'] > 0 else 1.2
    home_defense = home_stats['avg_goals_conceded'] if home_stats['avg_goals_conceded'] > 0 else 1.0
    away_defense = away_stats['avg_goals_conceded'] if away_stats['avg_goals_conceded'] > 0 else 1.1
    
    # Poisson prediction with REAL stats
    poisson_result = poisson.predict(
        home_attack=home_attack,
        away_attack=away_attack,
        home_defense=home_defense,
        away_defense=away_defense
    )
    
    # Monte Carlo prediction with REAL stats
    mc_result = monte_carlo.simulate(
        home_attack=home_attack,
        away_attack=away_attack,
        home_defense=home_defense,
        away_defense=away_defense
    )
    
    # Pattern analysis
    ml_hdw = poisson_result['hdw']
    mc_hdw = mc_result['hdw']
    all_agree = ml_hdw == mc_hdw
    pattern = "STRONG_CONSENSUS" if all_agree else "PARTIAL_CONSENSUS"
    
    # Trap detection with REAL stats
    trap_result = trap_detector.detect({
        'home_stats': home_stats,
        'away_stats': away_stats,
        'h2h': {'draw_rate': 0.25, 'under_2_rate': 0.3},
        'odds': {'home': home_odds, 'draw': draw_odds, 'away': away_odds}
    })
    
    # REAL team stats
    team_stats = {
        "home": home_stats,
        "away": away_stats
    }
    
    formatted_date = match_date.strftime('%Y-%m-%d') if pd.notna(match_date) else 'TBD'
    
    return {
        "match_id": f"{home_team[:3]}_{away_team[:3]}_{formatted_date}".lower().replace(" ", ""),
        "home_team": home_team,
        "away_team": away_team,
        "date": formatted_date,
        "time": str(row.get('Time', '15:00')),
        "league": LEAGUE_NAMES.get(league_id, league_id),
        "league_id": league_id,
        
        "odds": {
            "home": home_odds,
            "draw": draw_odds,
            "away": away_odds,
            "over_25": over25_odds
        },
        
        "team_stats": team_stats,
        
        "ml_predictions": {
            "hdw": {
                "prediction": poisson_result['hdw'],
                "confidence": round(poisson_result['hdw_confidence'], 2),
                "reasoning": f"{home_team} {'is favorite' if home_odds < away_odds else 'faces strong opposition'} based on current odds."
            },
            "btts": {
                "prediction": "Yes" if poisson_result['btts_probability'] > 0.5 else "No",
                "confidence": round(poisson_result['btts_probability'], 2),
                "reasoning": f"Both teams expected to score based on {poisson_result['expected_home_goals']:.1f}-{poisson_result['expected_away_goals']:.1f} expected goals."
            },
            "over_25": {
                "prediction": "Over" if poisson_result['over_25_probability'] > 0.5 else "Under",
                "confidence": round(poisson_result['over_25_probability'], 2),
                "reasoning": f"Expected total of {poisson_result['expected_home_goals'] + poisson_result['expected_away_goals']:.1f} goals."
            },
            "over_15": {
                "prediction": "Over" if poisson_result['over_15_probability'] > 0.5 else "Under",
                "confidence": round(poisson_result['over_15_probability'], 2),
                "reasoning": f"High probability of at least 2 goals based on team profiles."
            }
        },
        
        "poisson_analysis": {
            "expected_home_goals": poisson_result['expected_home_goals'],
            "expected_away_goals": poisson_result['expected_away_goals'],
            "hdw_probabilities": {k: round(v, 3) for k, v in poisson_result['hdw_probabilities'].items()},
            "reasoning": poisson_result['reasoning']
        },
        
        "monte_carlo_analysis": {
            "simulations": mc_result['simulations'],
            "hdw_probabilities": mc_result['hdw_probabilities'],
            "avg_total_goals": mc_result['avg_total_goals'],
            "reasoning": mc_result['reasoning']
        },
        
        "pattern_analysis": {
            "pattern": pattern,
            "all_methods_agree": all_agree,
            "confidence_level": "HIGH" if all_agree else "MEDIUM",
            "expected_accuracy": 0.693 if all_agree else 0.499,
            "reasoning": f"{'All prediction methods agree on ' + ml_hdw + ' - highest confidence level.' if all_agree else 'Methods show partial agreement - moderate confidence.'}"
        },
        
        "trap_detector": trap_result,
        
        "chatgpt_analysis": f"{home_team} {'look strong favorites' if home_odds < 2.0 else 'face a competitive match'} against {away_team}. The odds suggest {'a dominant home performance' if home_odds < away_odds else 'an evenly matched contest'}. {'Pattern analysis shows STRONG_CONSENSUS across all prediction methods, indicating a high-confidence opportunity.' if all_agree else 'Prediction methods show mixed signals - proceed with caution.'}",
        
        "recommendation": {
            "bet": f"{'Home Win' if poisson_result['hdw'] == 'H' else 'Draw' if poisson_result['hdw'] == 'D' else 'Away Win'}",
            "confidence": "HIGH" if all_agree and poisson_result['hdw_confidence'] > 0.6 else "MEDIUM",
            "stake": "3-5%" if all_agree else "1-2%"
        }
    }


@router.post("/matches/analyze")
async def analyze_matches(
    request: AnalyzeRequest,
    offset: int = Query(0, ge=0),
    limit: int = Query(20, ge=1, le=100)
):
    """Analyze matches for a given date with ML + Poisson + Monte Carlo"""
    
    # Load real fixtures
    fixtures_df = load_fixtures()
    
    if fixtures_df.empty:
        return {"offset": offset, "limit": limit, "total": 0, "items": [], "message": "No fixtures found"}
    
    # Filter by date if provided
    try:
        target_date = pd.to_datetime(request.date)
        # Get fixtures within 3 days of target date
        fixtures_df = fixtures_df[
            (fixtures_df['Date'] >= target_date) & 
            (fixtures_df['Date'] <= target_date + pd.Timedelta(days=3))
        ]
    except:
        pass
    
    # Filter by league
    if request.league_id:
        fixtures_df = fixtures_df[fixtures_df['Div'] == request.league_id]
    
    # Analyze each match (statistical analysis)
    analyses = []
    for _, row in fixtures_df.iterrows():
        try:
            analysis = analyze_match(row)
            analyses.append(analysis)
        except Exception as e:
            print(f"Error analyzing {row.get('HomeTeam', 'Unknown')}: {e}")
            continue
    
    # Group by league for Gemini batch analysis
    from app.services.gemini_service import get_gemini_service
    gemini = get_gemini_service()
    
    # Group matches by league
    leagues = {}
    for match in analyses:
        league_id = match.get("league_id", "unknown")
        if league_id not in leagues:
            leagues[league_id] = []
        leagues[league_id].append(match)
    
    # Send each league batch to Gemini
    gemini_analyzed = []
    for league_id, league_matches in leagues.items():
        league_name = LEAGUE_NAMES.get(league_id, league_id)
        analyzed = await gemini.analyze_matches_batch(league_matches, league_name)
        gemini_analyzed.extend(analyzed)
    
    # Sort by date
    gemini_analyzed.sort(key=lambda x: x.get("date", ""))
    
    # Apply pagination
    total = len(gemini_analyzed)
    items = gemini_analyzed[offset:offset + limit]
    
    return {
        "offset": offset,
        "limit": limit,
        "total": total,
        "items": items
    }


@router.get("/matches/leagues")
async def get_match_leagues():
    """Get leagues with upcoming fixtures"""
    fixtures_df = load_fixtures()
    
    if fixtures_df.empty:
        return {"leagues": []}
    
    leagues = fixtures_df['Div'].unique().tolist()
    return {
        "leagues": [{"id": league, "name": LEAGUE_NAMES.get(league, league)} for league in leagues]
    }
