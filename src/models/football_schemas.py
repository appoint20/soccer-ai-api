"""
Football API Schemas.

Pydantic models for fixtures and odds responses.
"""
from typing import Optional, List, Dict, Any
from pydantic import BaseModel, Field
from enum import Enum


class LeagueEnum(str, Enum):
    """Supported leagues."""
    PREMIER_LEAGUE = "premier_league"
    CHAMPIONSHIP = "championship"
    LA_LIGA = "la_liga"
    BUNDESLIGA = "bundesliga"
    SERIE_A = "serie_a"
    SERIE_B = "serie_b"
    LIGUE_1 = "ligue_1"
    LEAGUE_ONE = "league_one"
    LEAGUE_TWO = "league_two"


class TeamInfo(BaseModel):
    """Team information."""
    id: Optional[int] = None
    name: Optional[str] = None
    logo: Optional[str] = None


class MatchWinnerOdds(BaseModel):
    """Match winner odds (1X2)."""
    home: Optional[float] = None
    draw: Optional[float] = None
    away: Optional[float] = None


class OverUnderOdds(BaseModel):
    """Over/Under 2.5 goals odds."""
    over_25: Optional[float] = None
    under_25: Optional[float] = None


class BTTSOdds(BaseModel):
    """Both Teams to Score odds."""
    yes: Optional[float] = None
    no: Optional[float] = None


class Goals23Odds(BaseModel):
    """Goals 2-3 range odds."""
    over_2: Optional[float] = None
    under_3: Optional[float] = None


class Bet365Odds(BaseModel):
    """Complete Bet365 odds for a fixture."""
    match_winner: Optional[MatchWinnerOdds] = None
    over_under: Optional[OverUnderOdds] = None
    btts: Optional[BTTSOdds] = None
    goals_2_3: Optional[Goals23Odds] = None


class FixtureWithOdds(BaseModel):
    """Fixture with Bet365 odds."""
    fixture_id: Optional[int] = None
    league_name: Optional[str] = None
    league_id: Optional[int] = None
    match_date: Optional[str] = None
    home_team: TeamInfo = Field(default_factory=TeamInfo)
    away_team: TeamInfo = Field(default_factory=TeamInfo)
    venue: Optional[str] = None
    bet365_odds: Optional[Dict[str, Any]] = None


class FixturesResponse(BaseModel):
    """Response for fixtures endpoint."""
    league: str
    league_id: int
    total_fixtures: int
    from_date: str
    to_date: str
    fixtures: List[FixtureWithOdds] = Field(default_factory=list)


class APIUsageStats(BaseModel):
    """API usage statistics."""
    total_calls_today: int = 0
    remaining_calls: int = 100
    max_daily_calls: int = 100
    usage_percentage: float = 0.0


class ErrorResponse(BaseModel):
    """Error response."""
    error: str
    detail: Optional[str] = None
