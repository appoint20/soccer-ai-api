"""
Match Enrichment Service.

Orchestrates all enrichment calculators to produce complete EnrichedMatchData.
"""
from datetime import date
from typing import Dict, List, Optional

from src.domain.enrichment.models import EnrichedMatchData, GoalsStats, VenueForm
from src.domain.enrichment.matchday_calculator import MatchdayCalculator
from src.domain.enrichment.form_calculator import FormCalculator
from src.domain.enrichment.table_calculator import TableCalculator
from src.domain.enrichment.goals_aggregator import GoalsAggregator
from src.domain.enrichment.venue_form_calculator import VenueFormCalculator
from src.utils.logger import get_logger

logger = get_logger("MatchEnrichmentService")


class MatchEnrichmentService:
    """
    Orchestrates match data enrichment.
    
    Single entry point for all enrichment calculations.
    All results are pre-calculated for frontend consumption.
    """
    
    def __init__(self):
        self.matchday_calc = MatchdayCalculator()
        self.form_calc = FormCalculator()
        self.table_calc = TableCalculator()
        self.goals_agg = GoalsAggregator()
        self.venue_form_calc = VenueFormCalculator()
    
    def enrich_match(
        self,
        home_team: str,
        away_team: str,
        match_date: date,
        league_code: str,
        season: str,
        historical_matches: List[Dict],
    ) -> EnrichedMatchData:
        """
        Enrich a match with all calculated data.
        
        Args:
            home_team: Home team name
            away_team: Away team name
            match_date: Date of the match
            league_code: League identifier (E0, D1, etc.)
            season: Season identifier (2024-2025)
            historical_matches: All historical matches
            
        Returns:
            EnrichedMatchData with all pre-calculated fields
        """
        # 1. Matchday
        matchday = self.matchday_calc.calculate_matchday(
            match_date=match_date,
            league_code=league_code,
            season=season,
            historical_matches=historical_matches,
        )
        
        # 2. Form (overall last 5)
        home_form = self.form_calc.calculate_form(
            team=home_team,
            matches=historical_matches,
            last_n=5,
            before_date=match_date,
        )
        
        away_form = self.form_calc.calculate_form(
            team=away_team,
            matches=historical_matches,
            last_n=5,
            before_date=match_date,
        )
        
        # 3. Table positions
        home_standing = self.table_calc.get_team_position(
            team=home_team,
            league_code=league_code,
            season=season,
            matches=historical_matches,
            before_date=match_date,
        )
        
        away_standing = self.table_calc.get_team_position(
            team=away_team,
            league_code=league_code,
            season=season,
            matches=historical_matches,
            before_date=match_date,
        )
        
        # 4. Goals stats
        home_goals = self.goals_agg.calculate_goals_stats(
            team=home_team,
            league_code=league_code,
            season=season,
            matches=historical_matches,
            before_date=match_date,
        )
        
        away_goals = self.goals_agg.calculate_goals_stats(
            team=away_team,
            league_code=league_code,
            season=season,
            matches=historical_matches,
            before_date=match_date,
        )
        
        # 5. Venue-specific form
        home_venue_form = self.venue_form_calc.calculate_home_form(
            team=home_team,
            matches=historical_matches,
            last_n=3,
            before_date=match_date,
        )
        
        away_venue_form = self.venue_form_calc.calculate_away_form(
            team=away_team,
            matches=historical_matches,
            last_n=3,
            before_date=match_date,
        )
        
        # Build result
        home_position = home_standing.position if home_standing else 0
        away_position = away_standing.position if away_standing else 0
        home_points = home_standing.points if home_standing else 0
        away_points = away_standing.points if away_standing else 0
        
        return EnrichedMatchData(
            matchday=matchday,
            league_code=league_code,
            season=season,
            home_form=home_form.form_string,
            home_form_points=home_form.points,
            home_position=home_position,
            home_points=home_points,
            home_goals_stats=home_goals,
            home_venue_form=home_venue_form,
            away_form=away_form.form_string,
            away_form_points=away_form.points,
            away_position=away_position,
            away_points=away_points,
            away_goals_stats=away_goals,
            away_venue_form=away_venue_form,
            position_difference=home_position - away_position,
            points_difference=home_points - away_points,
        )
    
    def enrich_matches_batch(
        self,
        matches: List[Dict],
        historical_matches: List[Dict],
    ) -> Dict[str, EnrichedMatchData]:
        """
        Enrich multiple matches.
        
        Args:
            matches: List of upcoming matches
            historical_matches: All historical data
            
        Returns:
            Dict mapping match_id to EnrichedMatchData
        """
        results = {}
        
        for match in matches:
            home_team = match.get("home_team") or match.get("HomeTeam") or ""
            away_team = match.get("away_team") or match.get("AwayTeam") or ""
            match_date = self._parse_date(match.get("date") or match.get("match_date"))
            league_code = match.get("league") or match.get("Div") or ""
            season = match.get("season") or match.get("Season") or ""
            match_id = match.get("id") or match.get("match_id") or f"{home_team}_vs_{away_team}"
            
            if not home_team or not away_team or not match_date:
                continue
            
            try:
                enriched = self.enrich_match(
                    home_team=home_team,
                    away_team=away_team,
                    match_date=match_date,
                    league_code=league_code,
                    season=season,
                    historical_matches=historical_matches,
                )
                results[match_id] = enriched
            except Exception as e:
                logger.error(f"Failed to enrich match {match_id}: {e}")
        
        return results
    
    def _parse_date(self, value) -> Optional[date]:
        """Parse date from various formats."""
        if not value:
            return None
        if isinstance(value, date):
            return value
        if hasattr(value, "date"):
            return value.date()
        if isinstance(value, str):
            try:
                from datetime import datetime
                return datetime.fromisoformat(value[:10]).date()
            except:
                pass
        return None
