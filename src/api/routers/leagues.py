"""
Leagues API Router.

Clean controller for league-related endpoints.
"""
from typing import List, Dict
from fastapi import APIRouter
from pydantic import BaseModel

router = APIRouter(prefix="/leagues", tags=["Leagues"])


# ============== Data ==============

SUPPORTED_LEAGUES = {
    "E0": "Premier League",
    "E1": "Championship",
    "E2": "League One",
    "E3": "League Two",
    "D1": "Bundesliga",
    "F1": "Ligue 1",
    "F2": "Ligue 2",
    "I1": "Serie A",
    "I2": "Serie B",
    "SP1": "La Liga",
}

LEAGUE_COUNTRIES = {
    "E0": "England", "E1": "England", "E2": "England", "E3": "England",
    "D1": "Germany",
    "F1": "France", "F2": "France",
    "I1": "Italy", "I2": "Italy",
    "SP1": "Spain",
}


# ============== Schemas ==============

class LeagueInfo(BaseModel):
    """League information."""
    code: str
    name: str
    country: str


class LeaguesResponse(BaseModel):
    """Response for leagues list."""
    total: int
    leagues: List[LeagueInfo]


# ============== Endpoints ==============

@router.get("", response_model=LeaguesResponse)
async def get_leagues():
    """
    Get all supported leagues.
    
    Returns list of leagues with their codes, names, and countries.
    """
    leagues = [
        LeagueInfo(
            code=code,
            name=name,
            country=LEAGUE_COUNTRIES.get(code, ""),
        )
        for code, name in SUPPORTED_LEAGUES.items()
    ]
    
    return LeaguesResponse(
        total=len(leagues),
        leagues=leagues,
    )


@router.get("/{code}")
async def get_league_by_code(code: str):
    """
    Get a specific league by code.
    
    - **code**: League code (E0, E1, D1, etc.)
    """
    code_upper = code.upper()
    
    if code_upper not in SUPPORTED_LEAGUES:
        from fastapi import HTTPException
        raise HTTPException(
            status_code=404,
            detail=f"League '{code}' not found. Valid codes: {list(SUPPORTED_LEAGUES.keys())}"
        )
    
    return LeagueInfo(
        code=code_upper,
        name=SUPPORTED_LEAGUES[code_upper],
        country=LEAGUE_COUNTRIES.get(code_upper, ""),
    )
