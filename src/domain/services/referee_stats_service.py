"""
Referee statistics service for analyzing referee influence on matches.

Calculates referee tendencies for goals, cards, and match outcomes.
"""
from datetime import datetime
from typing import Optional, List, Dict, Any
from collections import defaultdict

from src.domain.services.base_service import BaseService
from src.data.cache.cache_manager import CacheManager
from src.utils.stats_utils import calculate_rate, round_to_precision


class RefereeStatsService(BaseService):
    """
    Analyze referee statistics and tendencies.
    
    Features:
    - Goal statistics per referee
    - Disciplinary tendencies (cards)
    - Match outcome distributions
    """
    
    def __init__(
        self,
        cache_manager: Optional[CacheManager] = None,
        min_matches: int = 10,  # Minimum matches for reliable stats
    ):
        """
        Initialize referee stats service.
        
        Args:
            cache_manager: Optional cache manager
            min_matches: Minimum matches for reliable statistics
        """
        super().__init__(cache_manager)
        self.min_matches = min_matches
    
    def calculate_referee_stats(
        self,
        referee_name: str,
        matches: List,
    ) -> Dict[str, Any]:
        """
        Calculate all statistics for a referee.
        
        Args:
            referee_name: Name of referee
            matches: All matches
            
        Returns:
            Dict with referee statistics
        """
        referee_name = self.validate_team_name(referee_name)  # Reuse validation
        if not referee_name:
            return self._empty_stats()
        
        cache_key = self.generate_cache_key("referee", referee_name)
        cached = self.get_cached(cache_key)
        if cached is not None:
            return cached
        
        with self.track_performance("calculate_referee_stats"):
            # Filter matches for this referee
            ref_matches = [
                m for m in matches
                if self._get_referee(m) == referee_name
            ]
            
            if not ref_matches:
                return self._empty_stats()
            
            stats = {
                "referee": referee_name,
                "matches_officiated": len(ref_matches),
                "leagues": self._get_referee_leagues(ref_matches),
                "goals_statistics": self._calculate_goal_stats(ref_matches),
                "disciplinary": self._calculate_disciplinary_stats(ref_matches),
                "match_outcomes": self._calculate_outcome_stats(ref_matches),
                "reliable": len(ref_matches) >= self.min_matches,
                "last_updated": datetime.now().isoformat()[:10],
            }
            
            self.set_cached(cache_key, stats)
            return stats
    
    def get_referee_influence_features(
        self,
        referee_name: str,
        matches: List,
    ) -> Dict[str, Any]:
        """
        Get features indicating referee's influence on matches.
        
        Args:
            referee_name: Referee name
            matches: All matches
            
        Returns:
            Influence features
        """
        stats = self.calculate_referee_stats(referee_name, matches)
        
        if not stats or stats["matches_officiated"] == 0:
            return self._default_influence()
        
        goals_stats = stats.get("goals_statistics", {})
        disc_stats = stats.get("disciplinary", {})
        
        # Classify tendencies
        avg_goals = goals_stats.get("avg_goals_per_match", 2.5)
        over25_rate = goals_stats.get("over25_rate", 0.5)
        cards_per_match = disc_stats.get("cards_per_match", 4.0)
        
        if avg_goals > 2.8:
            goals_tendency = "high"
        elif avg_goals < 2.3:
            goals_tendency = "low"
        else:
            goals_tendency = "medium"
        
        if cards_per_match > 5.0:
            card_tendency = "strict"
        elif cards_per_match < 3.0:
            card_tendency = "lenient"
        else:
            card_tendency = "moderate"
        
        return {
            "referee": referee_name,
            "goals_tendency": goals_tendency,
            "card_tendency": card_tendency,
            "over25_rate": over25_rate,
            "btts_rate": goals_stats.get("btts_rate", 0.5),
            "avg_goals": avg_goals,
            "avg_cards": cards_per_match,
            "reliable": stats.get("reliable", False),
        }
    
    def calculate_all_referees_stats(
        self,
        matches: List,
    ) -> Dict[str, Dict[str, Any]]:
        """
        Calculate stats for all referees in dataset.
        
        Args:
            matches: All matches
            
        Returns:
            Dict mapping referee name to stats
        """
        # Get unique referees
        referees = set()
        for match in matches:
            ref = self._get_referee(match)
            if ref:
                referees.add(ref)
        
        self.logger.info(f"Calculating stats for {len(referees)} referees")
        
        all_stats = {}
        for ref in referees:
            stats = self.calculate_referee_stats(ref, matches)
            if stats["matches_officiated"] >= self.min_matches:
                all_stats[ref] = stats
        
        return all_stats
    
    def _calculate_goal_stats(self, matches: List) -> Dict[str, Any]:
        """Calculate goal-related statistics."""
        total_goals = 0
        home_goals = 0
        away_goals = 0
        over25_count = 0
        btts_count = 0
        n_valid = 0
        
        for match in matches:
            fthg = self._get_field(match, "fthg")
            ftag = self._get_field(match, "ftag")
            
            if fthg is None or ftag is None:
                continue
            
            n_valid += 1
            total_goals += fthg + ftag
            home_goals += fthg
            away_goals += ftag
            
            if fthg + ftag > 2.5:
                over25_count += 1
            
            if fthg > 0 and ftag > 0:
                btts_count += 1
        
        if n_valid == 0:
            return {
                "avg_goals_per_match": 0.0,
                "over25_rate": 0.0,
                "btts_rate": 0.0,
                "avg_home_goals": 0.0,
                "avg_away_goals": 0.0,
            }
        
        return {
            "avg_goals_per_match": round_to_precision(total_goals / n_valid),
            "over25_rate": round_to_precision(calculate_rate(over25_count, n_valid)),
            "btts_rate": round_to_precision(calculate_rate(btts_count, n_valid)),
            "avg_home_goals": round_to_precision(home_goals / n_valid),
            "avg_away_goals": round_to_precision(away_goals / n_valid),
        }
    
    def _calculate_disciplinary_stats(self, matches: List) -> Dict[str, Any]:
        """Calculate disciplinary statistics."""
        total_yellows = 0
        total_reds = 0
        total_penalties = 0
        n_valid = 0
        
        for match in matches:
            hy = self._get_field(match, "hy")
            ay = self._get_field(match, "ay")
            hr = self._get_field(match, "hr")
            ar = self._get_field(match, "ar")
            
            n_valid += 1
            
            if hy is not None:
                total_yellows += hy
            if ay is not None:
                total_yellows += ay
            if hr is not None:
                total_reds += hr
            if ar is not None:
                total_reds += ar
        
        if n_valid == 0:
            return {
                "avg_yellow_cards": 0.0,
                "avg_red_cards": 0.0,
                "cards_per_match": 0.0,
                "style": "unknown",
                "penalties_per_match": 0.0,
            }
        
        avg_yellows = total_yellows / n_valid
        avg_reds = total_reds / n_valid
        cards_per_match = avg_yellows + avg_reds
        
        # Classify style
        if cards_per_match > 5.0:
            style = "strict"
        elif cards_per_match < 3.0:
            style = "lenient"
        else:
            style = "moderate"
        
        return {
            "avg_yellow_cards": round_to_precision(avg_yellows),
            "avg_red_cards": round_to_precision(avg_reds),
            "cards_per_match": round_to_precision(cards_per_match),
            "style": style,
            "penalties_per_match": 0.0,  # Would need penalty data
        }
    
    def _calculate_outcome_stats(self, matches: List) -> Dict[str, Any]:
        """Calculate match outcome statistics."""
        home_wins = 0
        draws = 0
        away_wins = 0
        n_valid = 0
        
        for match in matches:
            fthg = self._get_field(match, "fthg")
            ftag = self._get_field(match, "ftag")
            
            if fthg is None or ftag is None:
                continue
            
            n_valid += 1
            
            if fthg > ftag:
                home_wins += 1
            elif fthg < ftag:
                away_wins += 1
            else:
                draws += 1
        
        if n_valid == 0:
            return {
                "home_win_rate": 0.0,
                "draw_rate": 0.0,
                "away_win_rate": 0.0,
            }
        
        return {
            "home_win_rate": round_to_precision(calculate_rate(home_wins, n_valid)),
            "draw_rate": round_to_precision(calculate_rate(draws, n_valid)),
            "away_win_rate": round_to_precision(calculate_rate(away_wins, n_valid)),
        }
    
    def _get_referee(self, match: Any) -> Optional[str]:
        """Get referee from match."""
        ref = self._get_field(match, "referee")
        if ref and str(ref).strip() and str(ref).lower() not in ["nan", "none", ""]:
            return str(ref).strip()
        return None
    
    def _get_referee_leagues(self, matches: List) -> List[str]:
        """Get list of leagues referee has officiated."""
        leagues = set()
        for match in matches:
            league = self._get_field(match, "league")
            if league:
                leagues.add(league)
        return list(leagues)
    
    def _get_field(self, match: Any, field: str) -> Any:
        """Get field from match."""
        if isinstance(match, dict):
            return match.get(field)
        return getattr(match, field, None)
    
    def _empty_stats(self) -> Dict[str, Any]:
        """Return empty stats structure."""
        return {
            "referee": "",
            "matches_officiated": 0,
            "leagues": [],
            "goals_statistics": {
                "avg_goals_per_match": 0.0,
                "over25_rate": 0.0,
                "btts_rate": 0.0,
            },
            "disciplinary": {
                "avg_yellow_cards": 0.0,
                "avg_red_cards": 0.0,
                "cards_per_match": 0.0,
                "style": "unknown",
            },
            "match_outcomes": {
                "home_win_rate": 0.0,
                "draw_rate": 0.0,
                "away_win_rate": 0.0,
            },
            "reliable": False,
            "last_updated": datetime.now().isoformat()[:10],
        }
    
    def _default_influence(self) -> Dict[str, Any]:
        """Return default influence features."""
        return {
            "referee": "",
            "goals_tendency": "medium",
            "card_tendency": "moderate",
            "over25_rate": 0.5,
            "btts_rate": 0.5,
            "avg_goals": 2.5,
            "avg_cards": 4.0,
            "reliable": False,
        }
