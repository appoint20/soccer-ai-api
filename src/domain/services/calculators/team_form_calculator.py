"""
Team Form Calculator.

Calculates O/U2.5, BTTS, W/L/D, 2-3 goals rates with recency weighting
for a specified lookback window and optional venue filtering.
"""
from dataclasses import dataclass
from datetime import date, datetime
from typing import List, Dict, Any, Optional

from src.utils.logger import get_logger


@dataclass
class TeamFormStats:
    """Team form statistics for a specific lookback window."""
    over_25_rate: float = 0.0
    btts_rate: float = 0.0
    win_rate: float = 0.0
    lose_rate: float = 0.0
    draw_rate: float = 0.0
    goals_2_3_rate: float = 0.0
    avg_goals_scored: float = 0.0
    avg_goals_conceded: float = 0.0
    sample_size: int = 0
    effective_sample_size: float = 0.0  # Weighted sample size
    form: Optional[str] = None
    
    def to_dict(self) -> Dict[str, Any]:
        """Convert to dictionary for API response."""
        return {
            "over_25_rate": round(self.over_25_rate, 3),
            "btts_rate": round(self.btts_rate, 3),
            "win_rate": round(self.win_rate, 3),
            "lose_rate": round(self.lose_rate, 3),
            "draw_rate": round(self.draw_rate, 3),
            "goals_2_3_rate": round(self.goals_2_3_rate, 3),
            "avg_goals_scored": round(self.avg_goals_scored, 2),
            "avg_goals_conceded": round(self.avg_goals_conceded, 2),
            "sample_size": self.sample_size,
            "effective_sample_size": round(self.effective_sample_size, 2),
            "form": self.form,
        }


class TeamFormCalculator:
    """
    Calculate team form statistics with recency weighting.
    
    Recent matches are weighted higher using exponential decay:
    weight = decay ^ match_age_index (most recent = index 0)
    """
    
    def __init__(self, default_decay: float = 0.85):
        """
        Initialize calculator.
        
        Args:
            default_decay: Decay factor for exponential weighting (0-1).
                          0.85 means each older match has 85% the weight of previous.
        """
        self.logger = get_logger("TeamFormCalculator")
        self.default_decay = default_decay
    
    def calculate_form_stats(
        self,
        team: str,
        matches: List[Dict[str, Any]],
        last_n: int = 5,
        venue_filter: Optional[str] = None,
        decay: Optional[float] = None,
        as_of_date: Optional[date] = None,
    ) -> TeamFormStats:
        """
        Calculate form statistics for a team.
        
        Args:
            team: Team name to analyze
            matches: List of historical match dictionaries
            last_n: Number of recent matches to consider
            venue_filter: "home" | "away" | None (for all venues)
            decay: Recency decay factor (default: self.default_decay)
            as_of_date: Only consider matches before this date (time-travel)
            
        Returns:
            TeamFormStats with weighted rates
        """
        decay = decay if decay is not None else self.default_decay
        
        # Filter and sort matches
        team_matches = self._get_team_matches(team, matches, venue_filter, as_of_date)
        
        if not team_matches:
            return TeamFormStats()
        
        # Take last N matches
        recent_matches = team_matches[:last_n]
        sample_size = len(recent_matches)
        
        if sample_size == 0:
            return TeamFormStats()
        
        # Calculate weights
        weights = [decay ** i for i in range(sample_size)]
        total_weight = sum(weights)
        effective_sample_size = total_weight
        
        # Initialize accumulators
        over_25_weighted = 0.0
        btts_weighted = 0.0
        win_weighted = 0.0
        lose_weighted = 0.0
        draw_weighted = 0.0
        goals_2_3_weighted = 0.0
        goals_scored_weighted = 0.0
        goals_conceded_weighted = 0.0
        
        for i, match in enumerate(recent_matches):
            weight = weights[i]
            
            # Extract stats from match
            stats = self._extract_match_stats(match, team)
            
            # Accumulate weighted values
            over_25_weighted += weight * (1 if stats["total_goals"] > 2.5 else 0)
            btts_weighted += weight * (1 if stats["btts"] else 0)
            win_weighted += weight * (1 if stats["result"] == "W" else 0)
            lose_weighted += weight * (1 if stats["result"] == "L" else 0)
            draw_weighted += weight * (1 if stats["result"] == "D" else 0)
            goals_2_3_weighted += weight * (1 if stats["total_goals"] in [2, 3] else 0)
            goals_scored_weighted += weight * stats["goals_scored"]
            goals_conceded_weighted += weight * stats["goals_conceded"]
        
        # Normalize by total weight
        return TeamFormStats(
            over_25_rate=over_25_weighted / total_weight,
            btts_rate=btts_weighted / total_weight,
            win_rate=win_weighted / total_weight,
            lose_rate=lose_weighted / total_weight,
            draw_rate=draw_weighted / total_weight,
            goals_2_3_rate=goals_2_3_weighted / total_weight,
            avg_goals_scored=goals_scored_weighted / total_weight,
            avg_goals_conceded=goals_conceded_weighted / total_weight,
            sample_size=sample_size,
            effective_sample_size=effective_sample_size,
        )
    
    def _get_team_matches(
        self,
        team: str,
        matches: List[Dict[str, Any]],
        venue_filter: Optional[str],
        as_of_date: Optional[date],
    ) -> List[Dict[str, Any]]:
        """Filter matches for team, venue, and date, then sort by recency."""
        team_lower = team.lower().strip()
        filtered = []
        
        for match in matches:
            # Check team participation
            home_team = str(match.get("HomeTeam", match.get("home_team", ""))).lower().strip()
            away_team = str(match.get("AwayTeam", match.get("away_team", ""))).lower().strip()
            
            is_home = home_team == team_lower
            is_away = away_team == team_lower
            
            if not is_home and not is_away:
                continue
            
            # Apply venue filter
            if venue_filter == "home" and not is_home:
                continue
            if venue_filter == "away" and not is_away:
                continue
            
            # Apply date filter (time-travel)
            if as_of_date:
                match_date = self._parse_match_date(match)
                if match_date and match_date >= as_of_date:
                    continue
            
            match["_is_home"] = is_home
            filtered.append(match)
        
        # Sort by date descending (most recent first)
        filtered.sort(key=lambda m: self._parse_match_date(m) or date.min, reverse=True)
        
        return filtered
    
    def _extract_match_stats(
        self,
        match: Dict[str, Any],
        team: str,
    ) -> Dict[str, Any]:
        """Extract goals scored/conceded and result from team's perspective."""
        is_home = match.get("_is_home", False)
        
        # Get goals - handle both uppercase (Excel) and lowercase (JSON) keys
        home_goals = int(match.get("FTHG") or match.get("fthg") or match.get("home_goals") or 0)
        away_goals = int(match.get("FTAG") or match.get("ftag") or match.get("away_goals") or 0)
        
        if is_home:
            goals_scored = home_goals
            goals_conceded = away_goals
        else:
            goals_scored = away_goals
            goals_conceded = home_goals
        
        total_goals = home_goals + away_goals
        btts = home_goals > 0 and away_goals > 0
        
        # Determine result from team perspective
        if goals_scored > goals_conceded:
            result = "W"
        elif goals_scored < goals_conceded:
            result = "L"
        else:
            result = "D"
        
        return {
            "goals_scored": goals_scored,
            "goals_conceded": goals_conceded,
            "total_goals": total_goals,
            "btts": btts,
            "result": result,
        }
    
    def _parse_match_date(self, match: Dict[str, Any]) -> Optional[date]:
        """Parse match date from various formats."""
        date_val = match.get("Date") or match.get("date") or match.get("match_date") or match.get("parsed_date")
        
        if date_val is None:
            return None
        
        if isinstance(date_val, date):
            return date_val
        
        if isinstance(date_val, datetime):
            return date_val.date()
        
        if isinstance(date_val, str):
            # Try common formats
            for fmt in ["%Y-%m-%d", "%d/%m/%Y", "%d/%m/%y"]:
                try:
                    return datetime.strptime(date_val[:10], fmt).date()
                except ValueError:
                    continue
        
        return None
