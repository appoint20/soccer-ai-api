"""
H2H Stats Calculator.

Calculates head-to-head statistics between two teams with
reliability scoring based on sample size and recency.
"""
from dataclasses import dataclass
from datetime import date, datetime
from typing import List, Dict, Any, Optional

from src.utils.logger import get_logger


@dataclass
class H2HStats:
    """Head-to-head statistics with reliability score."""
    over_25_rate: float = 0.0
    btts_rate: float = 0.0
    home_win_rate: float = 0.0
    away_win_rate: float = 0.0
    draw_rate: float = 0.0
    goals_2_3_rate: float = 0.0
    avg_total_goals: float = 0.0
    avg_home_goals: float = 0.0
    avg_away_goals: float = 0.0
    total_matches: int = 0
    h2h_reliability: float = 0.0  # 0-1, how much to trust H2H signal
    
    def to_dict(self) -> Dict[str, Any]:
        """Convert to dictionary for API response."""
        return {
            "over_25_rate": round(self.over_25_rate, 3),
            "btts_rate": round(self.btts_rate, 3),
            "home_win_rate": round(self.home_win_rate, 3),
            "away_win_rate": round(self.away_win_rate, 3),
            "draw_rate": round(self.draw_rate, 3),
            "goals_2_3_rate": round(self.goals_2_3_rate, 3),
            "avg_total_goals": round(self.avg_total_goals, 2),
            "avg_home_goals": round(self.avg_home_goals, 2),
            "avg_away_goals": round(self.avg_away_goals, 2),
            "total_matches": self.total_matches,
            "h2h_reliability": round(self.h2h_reliability, 3),
        }


class H2HStatsCalculator:
    """
    Calculate H2H statistics with reliability scoring.
    
    Reliability is dampened when:
    - Sample size is small (< 3 matches)
    - Matches are old (> max_age_seasons)
    """
    
    def __init__(
        self,
        max_age_seasons: int = 3,
        min_reliable_matches: int = 3,
    ):
        """
        Initialize calculator.
        
        Args:
            max_age_seasons: Exclude H2H matches older than this
            min_reliable_matches: Below this, reliability is dampened
        """
        self.logger = get_logger("H2HStatsCalculator")
        self.max_age_seasons = max_age_seasons
        self.min_reliable_matches = min_reliable_matches
    
    def calculate_h2h_stats(
        self,
        home_team: str,
        away_team: str,
        matches: List[Dict[str, Any]],
        last_n: int = 5,
        as_of_date: Optional[date] = None,
    ) -> H2HStats:
        """
        Calculate H2H statistics between two teams.
        
        Args:
            home_team: Current home team
            away_team: Current away team
            matches: All historical matches
            last_n: Maximum H2H matches to consider
            as_of_date: Only consider matches before this date
            
        Returns:
            H2HStats with rates and reliability score
        """
        h2h_matches = self._extract_h2h_matches(
            home_team, away_team, matches, last_n, as_of_date
        )
        
        if not h2h_matches:
            return H2HStats(h2h_reliability=0.0)
        
        total = len(h2h_matches)
        
        # Calculate stats
        over_25_count = 0
        btts_count = 0
        home_win_count = 0
        away_win_count = 0
        draw_count = 0
        goals_2_3_count = 0
        total_home_goals = 0
        total_away_goals = 0
        
        for match in h2h_matches:
            # Handle both uppercase (Excel) and lowercase (JSON) keys
            home_goals = int(match.get("FTHG") or match.get("fthg") or match.get("home_goals") or 0)
            away_goals = int(match.get("FTAG") or match.get("ftag") or match.get("away_goals") or 0)
            total_goals = home_goals + away_goals
            
            # Determine which team was home in this H2H match
            match_home = str(match.get("HomeTeam", match.get("home_team", ""))).lower().strip()
            current_home_was_home = match_home == home_team.lower().strip()
            
            if current_home_was_home:
                perspective_home_goals = home_goals
                perspective_away_goals = away_goals
            else:
                perspective_home_goals = away_goals
                perspective_away_goals = home_goals
            
            total_home_goals += perspective_home_goals
            total_away_goals += perspective_away_goals
            
            if total_goals > 2.5:
                over_25_count += 1
            if home_goals > 0 and away_goals > 0:
                btts_count += 1
            if total_goals in [2, 3]:
                goals_2_3_count += 1
            
            # Result from current home team's perspective
            if perspective_home_goals > perspective_away_goals:
                home_win_count += 1
            elif perspective_home_goals < perspective_away_goals:
                away_win_count += 1
            else:
                draw_count += 1
        
        # Calculate reliability
        reliability = self._calculate_reliability(h2h_matches, as_of_date)
        
        return H2HStats(
            over_25_rate=over_25_count / total,
            btts_rate=btts_count / total,
            home_win_rate=home_win_count / total,
            away_win_rate=away_win_count / total,
            draw_rate=draw_count / total,
            goals_2_3_rate=goals_2_3_count / total,
            avg_total_goals=(total_home_goals + total_away_goals) / total,
            avg_home_goals=total_home_goals / total,
            avg_away_goals=total_away_goals / total,
            total_matches=total,
            h2h_reliability=reliability,
        )
    
    def _extract_h2h_matches(
        self,
        home_team: str,
        away_team: str,
        matches: List[Dict[str, Any]],
        last_n: int,
        as_of_date: Optional[date],
    ) -> List[Dict[str, Any]]:
        """Extract H2H matches between the two teams."""
        home_lower = home_team.lower().strip()
        away_lower = away_team.lower().strip()
        
        h2h_matches = []
        cutoff_date = self._get_cutoff_date(as_of_date)
        
        for match in matches:
            match_home = str(match.get("HomeTeam", match.get("home_team", ""))).lower().strip()
            match_away = str(match.get("AwayTeam", match.get("away_team", ""))).lower().strip()
            
            # Check if it's an H2H match (either direction)
            is_h2h = (
                (match_home == home_lower and match_away == away_lower) or
                (match_home == away_lower and match_away == home_lower)
            )
            
            if not is_h2h:
                continue
            
            # Apply date filters
            match_date = self._parse_match_date(match)
            
            if as_of_date and match_date and match_date >= as_of_date:
                continue
            
            if cutoff_date and match_date and match_date < cutoff_date:
                continue
            
            h2h_matches.append(match)
        
        # Sort by date descending and take last_n
        h2h_matches.sort(
            key=lambda m: self._parse_match_date(m) or date.min,
            reverse=True
        )
        
        return h2h_matches[:last_n]
    
    def _calculate_reliability(
        self,
        h2h_matches: List[Dict[str, Any]],
        as_of_date: Optional[date],
    ) -> float:
        """
        Calculate reliability score (0-1) for H2H data.
        
        Factors:
        - Sample size: < min_reliable_matches dampens by 50%
        - Recency: Older matches reduce reliability
        """
        if not h2h_matches:
            return 0.0
        
        total = len(h2h_matches)
        
        # Base reliability from sample size
        if total >= self.min_reliable_matches:
            size_factor = 1.0
        elif total >= 2:
            size_factor = 0.7
        else:
            size_factor = 0.4
        
        # Recency factor: average age of matches
        reference_date = as_of_date or date.today()
        ages_days = []
        
        for match in h2h_matches:
            match_date = self._parse_match_date(match)
            if match_date:
                age = (reference_date - match_date).days
                ages_days.append(age)
        
        if ages_days:
            avg_age_days = sum(ages_days) / len(ages_days)
            # 1 year = 365 days. Decay factor for age.
            # At 3 years (1095 days), factor = 0.5
            recency_factor = max(0.3, 1.0 - (avg_age_days / 2190))
        else:
            recency_factor = 0.5
        
        return min(1.0, size_factor * recency_factor)
    
    def _get_cutoff_date(self, as_of_date: Optional[date]) -> Optional[date]:
        """Get cutoff date based on max_age_seasons."""
        reference = as_of_date or date.today()
        # Approximate: 1 season = 1 year
        cutoff = date(
            reference.year - self.max_age_seasons,
            reference.month,
            reference.day
        )
        return cutoff
    
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
            for fmt in ["%Y-%m-%d", "%d/%m/%Y", "%d/%m/%y"]:
                try:
                    return datetime.strptime(date_val[:10], fmt).date()
                except ValueError:
                    continue
        
        return None
