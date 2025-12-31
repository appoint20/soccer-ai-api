"""
Match statistics service for BTTS and Over 2.5 calculations.

Provides detailed statistics based on:
- Last N overall matches
- Last N venue-specific matches (home/away)
- Qualification flags based on thresholds

Clean Code Principles Applied:
- Single Responsibility: Each method does one thing
- Open/Closed: Easy to add new stats without modifying existing
- DRY: Reusable calculation methods
- Self-documenting: Clear method names and types
"""
from dataclasses import dataclass
from typing import Dict, List, Any, Optional
from datetime import date, datetime

from src.domain.services.base_service import BaseService
from src.data.cache.cache_manager import CacheManager


# ============== Data Classes (Clean Types) ==============

@dataclass
class MatchCount:
    """Count of matches meeting a criteria."""
    matches: int
    count: int
    pct: float


@dataclass
class TeamBTTSStats:
    """BTTS statistics for a single team."""
    overall: MatchCount
    venue_specific: MatchCount
    scored_overall: MatchCount
    scored_venue: MatchCount


@dataclass
class TeamOver25Stats:
    """Over 2.5 statistics for a single team."""
    overall: MatchCount
    venue_specific: MatchCount


@dataclass
class TeamLowScoringStats:
    """Low scoring (0-0, 1-0, 0-1) statistics for a single team."""
    overall: MatchCount
    venue_specific: MatchCount


@dataclass
class QualificationFlags:
    """Qualification flags based on thresholds."""
    over25_qualified: bool
    btts_qualified: bool
    over25_reason: str
    btts_reason: str


# ============== Constants ==============

class StatsConfig:
    """Configuration for statistics calculation."""
    OVERALL_MATCHES = 9
    VENUE_MATCHES = 6
    OVER25_THRESHOLD = 55.0  # Both teams > 55% → qualified
    BTTS_THRESHOLD = 55.0    # Both teams > 55% → qualified
    
    # Combined weighting
    OVERALL_WEIGHT = 0.4
    VENUE_WEIGHT = 0.6


# ============== Service ==============

class MatchStatsService(BaseService):
    """
    Calculate BTTS and Over 2.5 statistics for matches.
    
    Uses configurable match counts and thresholds.
    """
    
    def __init__(
        self,
        cache_manager: Optional[CacheManager] = None,
        config: Optional[StatsConfig] = None,
    ):
        super().__init__(cache_manager)
        self.config = config or StatsConfig()
    
    def calculate_match_stats(
        self,
        home_team: str,
        away_team: str,
        matches: List[Dict],
        as_of_date: Optional[date] = None,
        league: Optional[str] = None,
    ) -> Dict[str, Any]:
        """
        Calculate comprehensive BTTS and Over 2.5 statistics.
        
        Args:
            home_team: Home team name
            away_team: Away team name
            matches: Historical matches
            as_of_date: Calculate as of this date
            league: Optional league filter
            
        Returns:
            Dict with btts, over25 stats and qualification flags
        """
        # Get team matches
        home_matches = self._get_team_matches(home_team, matches, as_of_date, league)
        away_matches = self._get_team_matches(away_team, matches, as_of_date, league)
        
        # Calculate BTTS stats
        home_btts = self._calculate_team_btts(home_team, home_matches, is_home=True)
        away_btts = self._calculate_team_btts(away_team, away_matches, is_home=False)
        
        # Calculate Over 2.5 stats
        home_over25 = self._calculate_team_over25(home_team, home_matches, is_home=True)
        away_over25 = self._calculate_team_over25(away_team, away_matches, is_home=False)
        
        # Calculate combined percentages
        combined_btts = self._calculate_combined(
            home_btts.overall.pct, home_btts.venue_specific.pct,
            away_btts.overall.pct, away_btts.venue_specific.pct,
        )
        
        combined_over25 = self._calculate_combined(
            home_over25.overall.pct, home_over25.venue_specific.pct,
            away_over25.overall.pct, away_over25.venue_specific.pct,
        )
        
        # Calculate qualification flags
        qualification = self._check_qualification(
            home_over25.overall.pct, away_over25.overall.pct,
            home_btts.overall.pct, away_btts.overall.pct,
        )
        
        return {
            "btts": {
                "combined_pct": round(combined_btts, 1),
                "home_team": self._btts_to_dict(home_btts, "home"),
                "away_team": self._btts_to_dict(away_btts, "away"),
            },
            "over25": {
                "combined_pct": round(combined_over25, 1),
                "home_team": self._over25_to_dict(home_over25, "home"),
                "away_team": self._over25_to_dict(away_over25, "away"),
            },
            "low_scoring": {
                "home_team": self._low_scoring_to_dict(
                    self._calculate_team_low_scoring(home_team, home_matches, is_home=True), "home"
                ),
                "away_team": self._low_scoring_to_dict(
                    self._calculate_team_low_scoring(away_team, away_matches, is_home=False), "away"
                ),
            },
            "qualification": {
                "over25_qualified": qualification.over25_qualified,
                "over25_reason": qualification.over25_reason,
                "btts_qualified": qualification.btts_qualified,
                "btts_reason": qualification.btts_reason,
            },
        }
    
    # ============== Team Match Retrieval ==============
    
    def _get_team_matches(
        self,
        team: str,
        matches: List[Dict],
        as_of_date: Optional[date],
        league: Optional[str],
    ) -> List[Dict]:
        """Get team's matches sorted by date (most recent first)."""
        team_matches = []
        
        for m in matches:
            if m.get("home_team") != team and m.get("away_team") != team:
                continue
            
            match_date = self._parse_date(m.get("match_date"))
            if match_date and as_of_date and match_date >= as_of_date:
                continue
            
            if league and m.get("league") != league:
                continue
            
            if m.get("fthg") is None or m.get("ftag") is None:
                continue
            
            team_matches.append(m)
        
        team_matches.sort(key=lambda x: self._parse_date(x.get("match_date")) or date(1900, 1, 1), reverse=True)
        return team_matches
    
    # ============== BTTS Calculations ==============
    
    def _calculate_team_btts(
        self,
        team: str,
        matches: List[Dict],
        is_home: bool,
    ) -> TeamBTTSStats:
        """Calculate BTTS statistics for a team."""
        # Split by venue
        if is_home:
            venue_matches = [m for m in matches if m.get("home_team") == team]
        else:
            venue_matches = [m for m in matches if m.get("away_team") == team]
        
        # Get slices
        overall_matches = matches[:self.config.OVERALL_MATCHES]
        venue_only = venue_matches[:self.config.VENUE_MATCHES]
        
        return TeamBTTSStats(
            overall=self._count_btts(overall_matches),
            venue_specific=self._count_btts(venue_only),
            scored_overall=self._count_team_scored(overall_matches, team),
            scored_venue=self._count_team_scored(venue_only, team),
        )
    
    def _count_btts(self, matches: List[Dict]) -> MatchCount:
        """Count BTTS occurrences."""
        if not matches:
            return MatchCount(matches=0, count=0, pct=0.0)
        
        count = sum(1 for m in matches if self._is_btts(m))
        return MatchCount(
            matches=len(matches),
            count=count,
            pct=round((count / len(matches)) * 100, 1),
        )
    
    def _count_team_scored(self, matches: List[Dict], team: str) -> MatchCount:
        """Count matches where team scored."""
        if not matches:
            return MatchCount(matches=0, count=0, pct=0.0)
        
        count = sum(1 for m in matches if self._team_scored(m, team))
        return MatchCount(
            matches=len(matches),
            count=count,
            pct=round((count / len(matches)) * 100, 1),
        )
    
    # ============== Over 2.5 Calculations ==============
    
    def _calculate_team_over25(
        self,
        team: str,
        matches: List[Dict],
        is_home: bool,
    ) -> TeamOver25Stats:
        """Calculate Over 2.5 statistics for a team."""
        if is_home:
            venue_matches = [m for m in matches if m.get("home_team") == team]
        else:
            venue_matches = [m for m in matches if m.get("away_team") == team]
        
        overall_matches = matches[:self.config.OVERALL_MATCHES]
        venue_only = venue_matches[:self.config.VENUE_MATCHES]
        
        return TeamOver25Stats(
            overall=self._count_over25(overall_matches),
            venue_specific=self._count_over25(venue_only),
        )
    
    def _count_over25(self, matches: List[Dict]) -> MatchCount:
        """Count Over 2.5 occurrences."""
        if not matches:
            return MatchCount(matches=0, count=0, pct=0.0)
        
        count = sum(1 for m in matches if self._is_over25(m))
        return MatchCount(
            matches=len(matches),
            count=count,
            pct=round((count / len(matches)) * 100, 1),
        )

    # ============== Low Scoring Calculations ==============

    def _calculate_team_low_scoring(
        self,
        team: str,
        matches: List[Dict],
        is_home: bool,
    ) -> TeamLowScoringStats:
        """Calculate Low Scoring (0-0, 1-0, 0-1) stats."""
        if is_home:
            venue_matches = [m for m in matches if m.get("home_team") == team]
        else:
            venue_matches = [m for m in matches if m.get("away_team") == team]
        
        overall_matches = matches[:self.config.OVERALL_MATCHES]
        venue_only = venue_matches[:self.config.VENUE_MATCHES]
        
        return TeamLowScoringStats(
            overall=self._count_low_scoring(overall_matches),
            venue_specific=self._count_low_scoring(venue_only),
        )

    def _count_low_scoring(self, matches: List[Dict]) -> MatchCount:
        """Count 0-0, 1-0, 0-1 occurrences."""
        if not matches:
            return MatchCount(matches=0, count=0, pct=0.0)
        
        count = sum(1 for m in matches if self._is_low_scoring(m))
        return MatchCount(
            matches=len(matches),
            count=count,
            pct=round((count / len(matches)) * 100, 1),
        )
    
    # ============== Qualification Logic ==============
    
    def _check_qualification(
        self,
        home_over25_pct: float,
        away_over25_pct: float,
        home_btts_pct: float,
        away_btts_pct: float,
    ) -> QualificationFlags:
        """Check if match qualifies for Over25/BTTS based on thresholds."""
        # Over 2.5 qualification: Both teams > 55%
        over25_qualified = (
            home_over25_pct > self.config.OVER25_THRESHOLD and
            away_over25_pct > self.config.OVER25_THRESHOLD
        )
        
        if over25_qualified:
            over25_reason = f"Both teams > {self.config.OVER25_THRESHOLD}% (H:{home_over25_pct}% A:{away_over25_pct}%)"
        else:
            below = []
            if home_over25_pct <= self.config.OVER25_THRESHOLD:
                below.append(f"Home {home_over25_pct}%")
            if away_over25_pct <= self.config.OVER25_THRESHOLD:
                below.append(f"Away {away_over25_pct}%")
            over25_reason = f"Below threshold: {', '.join(below)}"
        
        # BTTS qualification: Both teams > 60%
        btts_qualified = (
            home_btts_pct > self.config.BTTS_THRESHOLD and
            away_btts_pct > self.config.BTTS_THRESHOLD
        )
        
        if btts_qualified:
            btts_reason = f"Both teams > {self.config.BTTS_THRESHOLD}% (H:{home_btts_pct}% A:{away_btts_pct}%)"
        else:
            below = []
            if home_btts_pct <= self.config.BTTS_THRESHOLD:
                below.append(f"Home {home_btts_pct}%")
            if away_btts_pct <= self.config.BTTS_THRESHOLD:
                below.append(f"Away {away_btts_pct}%")
            btts_reason = f"Below threshold: {', '.join(below)}"
        
        return QualificationFlags(
            over25_qualified=over25_qualified,
            btts_qualified=btts_qualified,
            over25_reason=over25_reason,
            btts_reason=btts_reason,
        )
    
    # ============== Combined Calculation ==============
    
    def _calculate_combined(
        self,
        team1_overall: float,
        team1_venue: float,
        team2_overall: float,
        team2_venue: float,
    ) -> float:
        """Calculate weighted combined percentage."""
        team1_avg = (
            team1_overall * self.config.OVERALL_WEIGHT +
            team1_venue * self.config.VENUE_WEIGHT
        )
        team2_avg = (
            team2_overall * self.config.OVERALL_WEIGHT +
            team2_venue * self.config.VENUE_WEIGHT
        )
        return (team1_avg + team2_avg) / 2
    
    # ============== Match Predicates ==============
    
    def _is_btts(self, match: Dict) -> bool:
        """Check if match had both teams scoring."""
        return match.get("fthg", 0) > 0 and match.get("ftag", 0) > 0
    
    def _is_over25(self, match: Dict) -> bool:
        """Check if match had over 2.5 goals."""
        return (match.get("fthg", 0) + match.get("ftag", 0)) > 2.5
    
    def _is_low_scoring(self, match: Dict) -> bool:
        """Check if match was 0-0, 1-0, or 0-1."""
        h = match.get("fthg", 0)
        a = match.get("ftag", 0)
        return (h == 0 and a == 0) or (h == 1 and a == 0) or (h == 0 and a == 1)
    
    def _team_scored(self, match: Dict, team: str) -> bool:
        """Check if team scored in match."""
        if match.get("home_team") == team:
            return match.get("fthg", 0) > 0
        return match.get("ftag", 0) > 0
    
    # ============== Utilities ==============
    
    def _parse_date(self, d: Any) -> Optional[date]:
        """Parse date from various formats."""
        if d is None:
            return None
        if isinstance(d, str):
            return datetime.fromisoformat(d[:10]).date()
        if isinstance(d, datetime):
            return d.date()
        if isinstance(d, date):
            return d
        return None
    
    # ============== Serialization ==============
    
    def _btts_to_dict(self, stats: TeamBTTSStats, venue: str) -> Dict:
        """Convert BTTS stats to dict."""
        venue_key = f"{venue}_6" if venue == "home" else f"{venue}_6"
        scored_venue_key = f"scored_{venue}_6"
        
        return {
            "overall_9": {
                "matches": stats.overall.matches,
                "btts_count": stats.overall.count,
                "pct": stats.overall.pct,
            },
            venue_key: {
                "matches": stats.venue_specific.matches,
                "btts_count": stats.venue_specific.count,
                "pct": stats.venue_specific.pct,
            },
            "scored_overall_9": {
                "matches": stats.scored_overall.matches,
                "scored": stats.scored_overall.count,
                "pct": stats.scored_overall.pct,
            },
            scored_venue_key: {
                "matches": stats.scored_venue.matches,
                "scored": stats.scored_venue.count,
                "pct": stats.scored_venue.pct,
            },
        }
    
    def _over25_to_dict(self, stats: TeamOver25Stats, venue: str) -> Dict:
        """Convert Over25 stats to dict."""
        venue_key = f"{venue}_6"
        
        return {
            "overall_9": {
                "matches": stats.overall.matches,
                "over25_count": stats.overall.count,
                "pct": stats.overall.pct,
            },
            venue_key: {
                "matches": stats.venue_specific.matches,
                "over25_count": stats.venue_specific.count,
                "pct": stats.venue_specific.pct,
            },
        }

    def _low_scoring_to_dict(self, stats: TeamLowScoringStats, venue: str) -> Dict:
        """Convert LowScoring stats to dict."""
        venue_key = f"{venue}_6"
        
        return {
            "overall_9": {
                "matches": stats.overall.matches,
                "low_scoring_count": stats.overall.count,
                "pct": stats.overall.pct,
            },
            venue_key: {
                "matches": stats.venue_specific.matches,
                "low_scoring_count": stats.venue_specific.count,
                "pct": stats.venue_specific.pct,
            },
        }
