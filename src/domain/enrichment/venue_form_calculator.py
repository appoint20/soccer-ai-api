"""
Venue Form Calculator.

Calculates venue-specific form (last N home or away matches).
"""
from datetime import date, datetime
from typing import Dict, List, Optional

from src.domain.enrichment.models import VenueForm, FormResult, FormResultType
from src.utils.logger import get_logger

logger = get_logger("VenueFormCalculator")


class VenueFormCalculator:
    """
    Calculates venue-specific form.
    
    Home team: last N home matches
    Away team: last N away matches
    """
    
    def __init__(self, team_matcher=None):
        """
        Initialize calculator.
        
        Args:
            team_matcher: Optional TeamNameMatcher for fuzzy matching
        """
        self.matcher = team_matcher
    
    def calculate_home_form(
        self,
        team: str,
        matches: List[Dict],
        last_n: int = 3,
        before_date: Optional[date] = None,
    ) -> VenueForm:
        """Calculate form from last N home matches."""
        return self._calculate_venue_form(team, matches, last_n, before_date, "home")
    
    def calculate_away_form(
        self,
        team: str,
        matches: List[Dict],
        last_n: int = 3,
        before_date: Optional[date] = None,
    ) -> VenueForm:
        """Calculate form from last N away matches."""
        return self._calculate_venue_form(team, matches, last_n, before_date, "away")
    
    def _calculate_venue_form(
        self,
        team: str,
        matches: List[Dict],
        last_n: int,
        before_date: Optional[date],
        venue: str,
    ) -> VenueForm:
        """Calculate venue-specific form."""
        team_lower = team.lower()
        
        # Filter matches for this team at this venue
        venue_matches = []
        
        for match in matches:
            home_team = (match.get("home_team") or match.get("HomeTeam") or "").lower()
            away_team = (match.get("away_team") or match.get("AwayTeam") or "").lower()
            
            # Check team and venue
            is_home = False
            is_away = False
            
            if self.matcher:
                is_home = self.matcher.matches(home_team, team)
                is_away = self.matcher.matches(away_team, team)
            else:
                is_home = home_team == team_lower
                is_away = away_team == team_lower
            
            if venue == "home" and not is_home:
                continue
            if venue == "away" and not is_away:
                continue
            
            # Check date
            if before_date:
                match_date = self._get_date(match)
                if match_date and match_date >= before_date:
                    continue
            
            # Must have result
            if self._get_goals(match, "home") is None:
                continue
            
            venue_matches.append(match)
        
        # Sort by date descending
        venue_matches.sort(key=lambda m: self._get_date(m) or date.min, reverse=True)
        
        # Take last N
        recent = venue_matches[:last_n]
        
        # Calculate stats
        form_chars = []
        results: List[FormResult] = []
        wins = draws = losses = 0
        goals_scored = goals_conceded = 0
        points = 0
        
        for match in recent:
            home_goals = self._get_goals(match, "home")
            away_goals = self._get_goals(match, "away")
            home_team = match.get("home_team") or match.get("HomeTeam") or ""
            away_team = match.get("away_team") or match.get("AwayTeam") or ""
            
            if venue == "home":
                gf, ga = home_goals, away_goals
                opponent = away_team
            else:
                gf, ga = away_goals, home_goals
                opponent = home_team
            
            goals_scored += gf
            goals_conceded += ga
            
            if gf > ga:
                form_chars.append("W")
                wins += 1
                points += 3
                result_type = FormResultType.WIN
            elif gf < ga:
                form_chars.append("L")
                losses += 1
                result_type = FormResultType.LOSS
            else:
                form_chars.append("D")
                draws += 1
                points += 1
                result_type = FormResultType.DRAW
            
            match_date = self._get_date(match)
            results.append(FormResult(
                result=result_type,
                goals_for=gf,
                goals_against=ga,
                opponent=opponent,
                date=match_date.isoformat() if match_date else "",
                was_home=(venue == "home"),
            ))
        
        return VenueForm(
            form_string="".join(form_chars),
            matches_played=len(recent),
            wins=wins,
            draws=draws,
            losses=losses,
            goals_scored=goals_scored,
            goals_conceded=goals_conceded,
            points=points,
            last_results=results,
        )
    
    def _get_date(self, match: Dict) -> Optional[date]:
        """Extract date from match."""
        for key in ["date", "match_date", "Date"]:
            if key in match and match[key]:
                val = match[key]
                if isinstance(val, date):
                    return val
                if isinstance(val, datetime):
                    return val.date()
                if isinstance(val, str):
                    try:
                        return datetime.fromisoformat(val[:10]).date()
                    except:
                        pass
        return None
    
    def _get_goals(self, match: Dict, side: str) -> Optional[int]:
        """Get goals for home or away."""
        if side == "home":
            keys = ["fthg", "FTHG", "home_goals"]
        else:
            keys = ["ftag", "FTAG", "away_goals"]
        
        for key in keys:
            if key in match and match[key] is not None:
                try:
                    return int(match[key])
                except:
                    pass
        return None
