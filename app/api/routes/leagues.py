"""
Leagues API Route
"""
from fastapi import APIRouter, Query
from typing import List
import json
from pathlib import Path

router = APIRouter()

# Load leagues data
DATA_DIR = Path(__file__).parent.parent.parent.parent / "data"

LEAGUES = [
    {"id": "E0", "name": "Premier League", "country": "England", "flag": "🏴󠁧󠁢󠁥󠁮󠁧󠁿", "teams_count": 20},
    {"id": "E1", "name": "Championship", "country": "England", "flag": "🏴󠁧󠁢󠁥󠁮󠁧󠁿", "teams_count": 24},
    {"id": "E2", "name": "League One", "country": "England", "flag": "🏴󠁧󠁢󠁥󠁮󠁧󠁿", "teams_count": 24},
    {"id": "E3", "name": "League Two", "country": "England", "flag": "🏴󠁧󠁢󠁥󠁮󠁧󠁿", "teams_count": 24},
    {"id": "D1", "name": "Bundesliga", "country": "Germany", "flag": "🇩🇪", "teams_count": 18},
    {"id": "D2", "name": "2. Bundesliga", "country": "Germany", "flag": "🇩🇪", "teams_count": 18},
    {"id": "I1", "name": "Serie A", "country": "Italy", "flag": "🇮🇹", "teams_count": 20},
    {"id": "I2", "name": "Serie B", "country": "Italy", "flag": "🇮🇹", "teams_count": 20},
    {"id": "F1", "name": "Ligue 1", "country": "France", "flag": "🇫🇷", "teams_count": 18},
    {"id": "F2", "name": "Ligue 2", "country": "France", "flag": "🇫🇷", "teams_count": 20},
    {"id": "SP1", "name": "La Liga", "country": "Spain", "flag": "🇪🇸", "teams_count": 20},
]


@router.get("/leagues")
async def get_leagues(
    offset: int = Query(0, ge=0),
    limit: int = Query(20, ge=1, le=100)
):
    """Get all supported leagues with pagination"""
    total = len(LEAGUES)
    items = LEAGUES[offset:offset + limit]
    
    return {
        "offset": offset,
        "limit": limit,
        "total": total,
        "items": items
    }


@router.get("/leagues/{league_id}")
async def get_league(league_id: str):
    """Get single league by ID"""
    for league in LEAGUES:
        if league["id"] == league_id:
            return league
    return {"error": "League not found"}
