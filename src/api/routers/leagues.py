from fastapi import APIRouter
from typing import List, Dict

router = APIRouter()

LEAGUE_NAMES = {
    "E0": "Premier League (England)",
    "E1": "Championship (England)",
    "E2": "League One (England)",
    "E3": "League Two (England)",
    "D1": "Bundesliga (Germany)",
    "F1": "Ligue 1 (France)",
    "F2": "Ligue 2 (France)",
    "I1": "Serie A (Italy)",
    "I2": "Serie B (Italy)",
    "SP1": "La Liga (Spain)",
}

@router.get("/leagues", response_model=List[Dict[str, str]])
async def get_leagues():
    """
    Get all supported leagues with their codes and names.
    """
    return [
        {"code": code, "name": name}
        for code, name in LEAGUE_NAMES.items()
    ]
