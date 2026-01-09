"""
Data models for match enrichment.

All models are:
- Immutable (frozen=True)
- Type-hinted
- JSON-serializable via to_dict()
"""
from dataclasses import dataclass, field
from typing import Dict, List, Optional, Any
from enum import Enum


class FormResultType(str, Enum):
    """Match result for form calculation."""
    WIN = "W"
    DRAW = "D"
    LOSS = "L"


@dataclass(frozen=True)
class FormResult:
    """Single match result for form string."""
    result: FormResultType
    goals_for: int
    goals_against: int
    opponent: str
    date: str
    was_home: bool


@dataclass(frozen=True)
class VenueForm:
    """Venue-specific form stats (home or away)."""
    form_string: str  # "WWD" or "LDW"
    matches_played: int
    wins: int
    draws: int
    losses: int
    goals_scored: int
    goals_conceded: int
    points: int
    last_results: List[FormResult] = field(default_factory=list)
    
    def to_dict(self) -> Dict[str, Any]:
        return {
            "form_string": self.form_string,
            "matches_played": self.matches_played,
            "wins": self.wins,
            "draws": self.draws,
            "losses": self.losses,
            "goals_scored": self.goals_scored,
            "goals_conceded": self.goals_conceded,
            "points": self.points,
            "avg_goals_scored": round(self.goals_scored / max(self.matches_played, 1), 2),
            "avg_goals_conceded": round(self.goals_conceded / max(self.matches_played, 1), 2),
        }


@dataclass(frozen=True)
class TeamStanding:
    """Team's position in league table."""
    position: int
    team: str
    played: int
    wins: int
    draws: int
    losses: int
    goals_for: int
    goals_against: int
    goal_difference: int
    points: int
    form: str = ""  # Last 5 results "WWDLL"
    
    def to_dict(self) -> Dict[str, Any]:
        return {
            "position": self.position,
            "team": self.team,
            "played": self.played,
            "wins": self.wins,
            "draws": self.draws,
            "losses": self.losses,
            "goals_for": self.goals_for,
            "goals_against": self.goals_against,
            "goal_difference": self.goal_difference,
            "points": self.points,
            "form": self.form,
            "ppg": round(self.points / max(self.played, 1), 2),
        }


@dataclass(frozen=True)
class GoalsStats:
    """Goals statistics for a team."""
    total_scored: int
    total_conceded: int
    home_scored: int
    home_conceded: int
    away_scored: int
    away_conceded: int
    matches_played: int
    home_matches: int
    away_matches: int
    
    def to_dict(self) -> Dict[str, Any]:
        return {
            "total_scored": self.total_scored,
            "total_conceded": self.total_conceded,
            "home_scored": self.home_scored,
            "home_conceded": self.home_conceded,
            "away_scored": self.away_scored,
            "away_conceded": self.away_conceded,
            "avg_scored": round(self.total_scored / max(self.matches_played, 1), 2),
            "avg_conceded": round(self.total_conceded / max(self.matches_played, 1), 2),
            "avg_home_scored": round(self.home_scored / max(self.home_matches, 1), 2),
            "avg_away_scored": round(self.away_scored / max(self.away_matches, 1), 2),
        }


@dataclass(frozen=True)
class EnrichedMatchData:
    """
    Complete enriched data for a match.
    
    This is the main output sent to SwiftUI.
    All calculations are done server-side.
    """
    # Match identification
    matchday: int
    league_code: str
    season: str
    
    # Home team data
    home_form: str  # "WWDLL"
    home_form_points: int  # 0-15
    home_position: int
    home_points: int
    home_goals_stats: GoalsStats
    home_venue_form: VenueForm  # Last 3 home matches
    
    # Away team data
    away_form: str  # "LDWWW"
    away_form_points: int
    away_position: int
    away_points: int
    away_goals_stats: GoalsStats
    away_venue_form: VenueForm  # Last 3 away matches
    
    # Derived metrics
    position_difference: int = 0  # home_position - away_position
    points_difference: int = 0
    
    def to_dict(self) -> Dict[str, Any]:
        return {
            "matchday": self.matchday,
            "league_code": self.league_code,
            "season": self.season,
            "home": {
                "form": self.home_form,
                "form_points": self.home_form_points,
                "position": self.home_position,
                "points": self.home_points,
                "goals": self.home_goals_stats.to_dict(),
                "venue_form": self.home_venue_form.to_dict(),
            },
            "away": {
                "form": self.away_form,
                "form_points": self.away_form_points,
                "position": self.away_position,
                "points": self.away_points,
                "goals": self.away_goals_stats.to_dict(),
                "venue_form": self.away_venue_form.to_dict(),
            },
            "position_difference": self.position_difference,
            "points_difference": self.points_difference,
        }
