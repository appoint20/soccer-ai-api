"""
Form Calculator.

Calculates recent form string and points for a team.

Output:
- "WWDLL" style form string
- Points from last N matches
- Streaks (unbeaten, winless)
"""
from datetime import date, datetime
from typing import Dict, List, Optional, Tuple
from dataclasses import dataclass

from src.domain.enrichment.models import FormResult, FormResultType
from src.utils.logger import get_logger

logger = get_logger("FormCalculator")


@dataclass
class FormStats:
    """Form calculation result."""
    form_string: str
    points: int
    wins: int
    draws: int
    losses: int
    goals_scored: int
    goals_conceded: int
    unbeaten_streak: int
    winless_streak: int
    results: List[FormResult]
    
    def to_dict(self) -> Dict:
        return {
            "form_string": self.form_string,
            "points": self.points,
            "wins": self.wins,
            "draws": self.draws,
            "losses": self.losses,
            "goals_scored": self.goals_scored,
            "goals_conceded": self.goals_conceded,
            "unbeaten_streak": self.unbeaten_streak,
            "winless_streak": self.winless_streak,
            "ppg": round(self.points / max(len(self.results), 1), 2),
        }


class FormCalculator:
    """
    Calculates team form from recent matches.
    
    Form is calculated from most recent to oldest,
    so "WWDLL" means: last match W, 2nd last W, etc.
    """
    
    def calculate_form(
        self,
        team: str,
        matches: List[Dict],
        last_n: int = 5,
        before_date: Optional[date] = None,
    ) -> FormStats:
        """
        Calculate form for a team.
        
        Args:
            team: Team name
            matches: Historical matches
            last_n: Number of recent matches to consider
            before_date: Only consider matches before this date
            
        Returns:
            FormStats with form string and statistics
        """
        # Get team's matches
        team_matches = self._get_team_matches(team, matches, before_date)
        
        # Sort by date descending (most recent first)
        team_matches.sort(key=lambda m: self._get_date(m) or date.min, reverse=True)
        
        # Take last N
        recent = team_matches[:last_n]
        
        # Calculate form
        results: List[FormResult] = []
        form_chars = []
        total_points = 0
        wins = draws = losses = 0
        goals_scored = goals_conceded = 0
        
        for match in recent:
            result = self._get_result_for_team(team, match)
            if result:
                results.append(result)
                form_chars.append(result.result.value)
                
                if result.result == FormResultType.WIN:
                    total_points += 3
                    wins += 1
                elif result.result == FormResultType.DRAW:
                    total_points += 1
                    draws += 1
                else:
                    losses += 1
                
                goals_scored += result.goals_for
                goals_conceded += result.goals_against
        
        # Calculate streaks
        unbeaten = self._calculate_unbeaten_streak(results)
        winless = self._calculate_winless_streak(results)
        
        return FormStats(
            form_string="".join(form_chars),
            points=total_points,
            wins=wins,
            draws=draws,
            losses=losses,
            goals_scored=goals_scored,
            goals_conceded=goals_conceded,
            unbeaten_streak=unbeaten,
            winless_streak=winless,
            results=results,
        )
    
    def _get_team_matches(
        self,
        team: str,
        matches: List[Dict],
        before_date: Optional[date],
    ) -> List[Dict]:
        """Get all matches for a team."""
        result = []
        team_lower = team.lower()
        
        for match in matches:
            home = (match.get("home_team") or match.get("HomeTeam") or "").lower()
            away = (match.get("away_team") or match.get("AwayTeam") or "").lower()
            
            if team_lower not in (home, away):
                continue
            
            # Check date filter
            if before_date:
                match_date = self._get_date(match)
                if match_date and match_date >= before_date:
                    continue
            
            # Must have result
            if self._get_goals(match, "home") is None:
                continue
            
            result.append(match)
        
        return result
    
    def _get_result_for_team(self, team: str, match: Dict) -> Optional[FormResult]:
        """Get result from team's perspective."""
        home_team = match.get("home_team") or match.get("HomeTeam") or ""
        away_team = match.get("away_team") or match.get("AwayTeam") or ""
        
        home_goals = self._get_goals(match, "home")
        away_goals = self._get_goals(match, "away")
        
        if home_goals is None or away_goals is None:
            return None
        
        team_lower = team.lower()
        is_home = home_team.lower() == team_lower
        
        if is_home:
            goals_for = home_goals
            goals_against = away_goals
            opponent = away_team
        else:
            goals_for = away_goals
            goals_against = home_goals
            opponent = home_team
        
        # Determine result
        if goals_for > goals_against:
            result_type = FormResultType.WIN
        elif goals_for < goals_against:
            result_type = FormResultType.LOSS
        else:
            result_type = FormResultType.DRAW
        
        match_date = self._get_date(match)
        
        return FormResult(
            result=result_type,
            goals_for=goals_for,
            goals_against=goals_against,
            opponent=opponent,
            date=match_date.isoformat() if match_date else "",
            was_home=is_home,
        )
    
    def _calculate_unbeaten_streak(self, results: List[FormResult]) -> int:
        """Count consecutive matches without a loss (from most recent)."""
        streak = 0
        for r in results:
            if r.result == FormResultType.LOSS:
                break
            streak += 1
        return streak
    
    def _calculate_winless_streak(self, results: List[FormResult]) -> int:
        """Count consecutive matches without a win (from most recent)."""
        streak = 0
        for r in results:
            if r.result == FormResultType.WIN:
                break
            streak += 1
        return streak
    
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
            keys = ["fthg", "FTHG", "home_goals", "HomeGoals"]
        else:
            keys = ["ftag", "FTAG", "away_goals", "AwayGoals"]
        
        for key in keys:
            if key in match and match[key] is not None:
                try:
                    return int(match[key])
                except:
                    pass
        return None
