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

def load_leagues() -> List[dict]:
    """Load leagues from data/leagues.json"""
    leagues_file = DATA_DIR / "leagues.json"
    try:
        with open(leagues_file, 'r') as f:
            return json.load(f)
    except (FileNotFoundError, json.JSONDecodeError):
        # Fallback to empty list if file doesn't exist
        return []

# Load leagues on startup
LEAGUES = load_leagues()


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