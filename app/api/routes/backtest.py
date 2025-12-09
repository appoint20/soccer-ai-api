"""
Backtest API Route
"""
from fastapi import APIRouter, Query
from typing import Optional, List

router = APIRouter()


@router.get("/backtest")
async def get_backtest(
    weeks: int = Query(15, ge=1, le=52),
    league: Optional[str] = Query(None, description="Filter by league ID"),
    offset: int = Query(0, ge=0),
    limit: int = Query(50, ge=1, le=200)
):
    """Get comprehensive backtesting results with ROI calculation"""
    
    # Mock backtest data (in production, run actual backtest)
    
    # Summary
    summary = {
        "period": "2025-02-10 to 2025-05-25",
        "total_matches": 1614,
        "overall_accuracy": 0.693,
        "pattern_breakdown": {
            "strong_consensus": {"matches": 293, "accuracy": 0.693},
            "partial_consensus": {"matches": 467, "accuracy": 0.499},
            "divergent": {"matches": 4, "accuracy": 0.250}
        }
    }
    
    # Mock game results
    mock_games = [
        {"date": "2025-02-10", "match": "Man City vs Liverpool", "league": "Premier League", "prediction": "H", "actual": "H", "correct": True, "confidence": 0.72, "pattern": "STRONG_CONSENSUS", "odds": 2.20, "message": "✅ Correct - Home Win"},
        {"date": "2025-02-10", "match": "Arsenal vs Chelsea", "league": "Premier League", "prediction": "H", "actual": "D", "correct": False, "confidence": 0.68, "pattern": "PARTIAL_CONSENSUS", "odds": 1.80, "message": "❌ Incorrect - Drew instead"},
        {"date": "2025-02-11", "match": "Bayern vs Dortmund", "league": "Bundesliga", "prediction": "H", "actual": "H", "correct": True, "confidence": 0.75, "pattern": "STRONG_CONSENSUS", "odds": 1.65, "message": "✅ Correct - Home Win"},
        {"date": "2025-02-11", "match": "Inter vs AC Milan", "league": "Serie A", "prediction": "D", "actual": "D", "correct": True, "confidence": 0.55, "pattern": "PARTIAL_CONSENSUS", "odds": 3.40, "message": "✅ Correct - Draw"},
        {"date": "2025-02-12", "match": "Real Madrid vs Barcelona", "league": "La Liga", "prediction": "H", "actual": "H", "correct": True, "confidence": 0.71, "pattern": "STRONG_CONSENSUS", "odds": 2.10, "message": "✅ Correct - Home Win"},
        {"date": "2025-02-12", "match": "PSG vs Lyon", "league": "Ligue 1", "prediction": "H", "actual": "H", "correct": True, "confidence": 0.82, "pattern": "STRONG_CONSENSUS", "odds": 1.55, "message": "✅ Correct - Home Win"},
    ]
    
    # Filter by league if specified
    if league:
        league_map = {"E0": "Premier League", "D1": "Bundesliga", "I1": "Serie A", "SP1": "La Liga", "F1": "Ligue 1"}
        league_name = league_map.get(league, league)
        mock_games = [g for g in mock_games if g['league'] == league_name]
    
    # League performance
    league_performance = [
        {"league_id": "E0", "league_name": "Premier League", "matches": 141, "accuracy": 0.71, "roi": 22.5, "best_market": "Over 1.5", "worst_market": "Draw"},
        {"league_id": "D1", "league_name": "Bundesliga", "matches": 117, "accuracy": 0.68, "roi": 18.2, "best_market": "BTTS", "worst_market": "Away"},
        {"league_id": "I1", "league_name": "Serie A", "matches": 142, "accuracy": 0.72, "roi": 25.1, "best_market": "Over 2.5", "worst_market": "Draw"},
        {"league_id": "SP1", "league_name": "La Liga", "matches": 151, "accuracy": 0.69, "roi": 19.8, "best_market": "Home", "worst_market": "Draw"},
        {"league_id": "F1", "league_name": "Ligue 1", "matches": 117, "accuracy": 0.70, "roi": 20.5, "best_market": "Over 1.5", "worst_market": "Away"},
        {"league_id": "E1", "league_name": "Championship", "matches": 184, "accuracy": 0.65, "roi": 12.3, "best_market": "Over 2.5", "worst_market": "Draw"},
    ]
    
    # ROI calculation (3 games/ticket, €100/ticket, max 4/fixture)
    roi_calculation = {
        "rules": {
            "games_per_ticket": 3,
            "stake_per_ticket": 100,
            "max_tickets_per_fixture": 4
        },
        "results": {
            "total_tickets": 56,
            "winning_tickets": 32,
            "losing_tickets": 24,
            "total_staked": 5600,
            "total_returns": 8750,
            "profit": 3150,
            "roi_percentage": 56.25,
            "win_rate": 0.571
        }
    }
    
    # Paginate games analysis
    total_games = len(mock_games)
    paginated_games = mock_games[offset:offset + limit]
    
    return {
        "summary": summary,
        "games_analysis": {
            "offset": offset,
            "limit": limit,
            "total": total_games,
            "items": paginated_games
        },
        "league_performance": league_performance,
        "roi_calculation": roi_calculation
    }
