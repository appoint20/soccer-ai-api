"""
Analyze Matches Use Case.

This use case orchestrates the match analysis workflow:
1. Load upcoming matches
2. Apply pagination
3. Analyze each match with calculators
4. Optionally enrich with AI analysis
5. Return structured results

Single Responsibility: Orchestrate match analysis workflow.
Dependencies are injected, making this fully testable.
"""
from dataclasses import dataclass, asdict
from datetime import datetime, date
from typing import List, Optional, Protocol, Dict, Any

from src.domain.entities.match import Match
from src.domain.services.calculators.team_form_calculator import TeamFormCalculator, TeamFormStats
from src.domain.services.calculators.h2h_stats_calculator import H2HStatsCalculator, H2HStats
from src.domain.services.calculators.poisson_goal_calculator import PoissonGoalCalculator, PoissonProbabilities
from src.domain.services.calculators.monte_carlo_uncertainty_adjuster import (
    MonteCarloUncertaintyAdjuster,
    MonteCarloResults,
)
from src.domain.services.calculators.match_confidence_calculator import MatchConfidenceCalculator
from src.utils.logger import get_logger
from src.api.schemas import MatchAnalysisResult
# Use dict or Any for BacktestResult to avoid circular imports if needed, or strict typing if possible
from src.api.schemas import BacktestReportResponse, MarketAccuracyReport

logger = get_logger("AnalyzeMatchesUseCase")


# ============== Request/Result DTOs ==============

@dataclass(frozen=True)
class AnalyzeMatchesRequest:
    """Input for analyze matches use case."""
    date: Optional[date] = None
    page: int = 1
    limit: int = 50
    refresh: bool = False  # Force refresh AI cache
    

@dataclass
class TeamStats:
    """Canonical team statistics (matches schema)."""
    last_5: TeamFormStats
    venue_last_3: Optional[TeamFormStats] = None
    form: Optional[str] = None
    position: Optional[int] = None
    points: Optional[int] = None
    goals_scored: Optional[int] = None
    goals_conceded: Optional[int] = None


@dataclass
class AIAnalysis:
    """AI-generated analysis for a match."""
    best_prediction: str
    reason: str
    short_analysis: str
    confidence_level: str


@dataclass
class SingleMatchAnalysis:
    """Canonical analysis result for a single match (matches schema)."""
    match_id: str
    home_team: str
    away_team: str
    date: str
    time: Optional[str]
    league: str
    
    # Enrichment Flattened
    matchday: int
    position_difference: int
    points_difference: int
    
    # Canonical Stats Objects
    homeStats: TeamStats
    awayStats: TeamStats
    h2h_last_5: H2HStats
    
    # Probabilities
    poisson: PoissonProbabilities
    monte_carlo: MonteCarloResults
    
    # Confidence
    overall_confidence: float
    
    # Aggregated Markets (renamed from aggregated_markets)
    match_analysis: Optional[MatchAnalysisResult] = None
    
    # AI Analysis
    ai_analysis: Optional[AIAnalysis] = None
    
    # Odds
    odds: Optional[dict] = None
    
    # Backtest
    backtest_result: Optional[dict] = None  # Using dict to simulate BacktestResult structure


@dataclass
class AnalyzeMatchesResult:
    """Output from analyze matches use case."""
    analyses: List[SingleMatchAnalysis]
    total: int
    page: int
    limit: int
    generated_at: str
    backtest_stats: Optional[dict] = None  # Overall backtest stats


# ============== Match Analyzer Protocol ==============

class IMatchAnalyzer(Protocol):
    """Interface for match analysis."""
    
    def analyze(
        self,
        match: Match,
        historical_matches: List[Match],
    ) -> SingleMatchAnalysis:
        """Analyze a single match."""
        ...


class IAIAnalyzer(Protocol):
    """Interface for AI analysis enrichment."""
    
    def enrich_batch(
        self,
        analyses: List[SingleMatchAnalysis],
        date: Optional[str] = None,
        refresh: bool = False, 
    ) -> List[SingleMatchAnalysis]:
        """Enrich analyses with AI predictions."""
        ...

class IBacktestService(Protocol):
    """Interface for backtesting service."""
    
    def calculate_stats(self, analyses: List[SingleMatchAnalysis]) -> Dict[str, Any]:
        """Calculate backtest specific stats."""
        ...


# ============== Match Analyzer Implementation ==============

class MatchAnalyzer:
    """
    Coordinates all statistical calculators to analyze a match.
    
    Single Responsibility: Orchestrate calculations for one match.
    All calculators are injected for testability.
    """
    
    def __init__(
        self,
        form_calculator: TeamFormCalculator,
        h2h_calculator: H2HStatsCalculator,
        poisson_calculator: PoissonGoalCalculator,
        monte_carlo_adjuster: MonteCarloUncertaintyAdjuster,
        confidence_calculator: MatchConfidenceCalculator,
        league_names: dict,
    ):
        self._form_calc = form_calculator
        self._h2h_calc = h2h_calculator
        self._poisson_calc = poisson_calculator
        self._mc_adjuster = monte_carlo_adjuster
        self._confidence_calc = confidence_calculator
        self._league_names = league_names
        self.logger = get_logger("MatchAnalyzer")
    
    def analyze(
        self,
        match: Match,
        historical_matches: List[Match],
    ) -> SingleMatchAnalysis:
        """Analyze a single match with all calculators."""
        
        # Convert Match entities to dicts for calculators (temporary bridge)
        if historical_matches and isinstance(historical_matches[0], dict):
            hist_dicts = historical_matches
        else:
            hist_dicts = [self._match_to_dict(m) for m in historical_matches]
        
        # 1. Team Form Stats
        home_last_5 = self._form_calc.calculate_form_stats(
            team=match.home_team,
            matches=hist_dicts,
            last_n=5,
            venue_filter=None,
            as_of_date=match.match_date,
        )
        
        away_last_5 = self._form_calc.calculate_form_stats(
            team=match.away_team,
            matches=hist_dicts,
            last_n=5,
            venue_filter=None,
            as_of_date=match.match_date,
        )
        
        home_last_3 = self._form_calc.calculate_form_stats(
            team=match.home_team,
            matches=hist_dicts,
            last_n=3,
            venue_filter="home",
            as_of_date=match.match_date,
        )
        
        away_last_3 = self._form_calc.calculate_form_stats(
            team=match.away_team,
            matches=hist_dicts,
            last_n=3,
            venue_filter="away",
            as_of_date=match.match_date,
        )
        
        # 2. H2H Stats
        h2h_stats = self._h2h_calc.calculate_h2h_stats(
            home_team=match.home_team,
            away_team=match.away_team,
            matches=hist_dicts,
            last_n=5,
            as_of_date=match.match_date,
        )
        
        # 3. Poisson Probabilities
        poisson_probs = self._poisson_calc.calculate_probabilities(
            home_team=match.home_team,
            away_team=match.away_team,
            home_stats=home_last_5,
            away_stats=away_last_5,
            league_code=match.league,
            league_avg_goals=2.7,
        )
        # Populate expected_score as Integer x-y (e.g. "1-2")
        home_exp = int(round(poisson_probs.expected_home_goals))
        away_exp = int(round(poisson_probs.expected_away_goals))
        poisson_probs.expected_score = f"{home_exp}-{away_exp}"
        
        # 4. Monte Carlo Adjustment
        recent_outcomes = self._build_recent_outcomes(
            match.home_team,
            match.away_team,
            hist_dicts,
            as_of_date=match.match_date,
        )
        
        mc_results = self._mc_adjuster.calculate_all_markets(
            poisson_probs={
                "over_25": poisson_probs.over_25,
                "btts": poisson_probs.btts,
                "home_win": poisson_probs.home_win,
                "away_win": poisson_probs.away_win,
                "draw": poisson_probs.draw,
                "goals_2_3": poisson_probs.goals_2_3,
            },
            recent_outcomes=recent_outcomes,
        )
        
        # 5. Overall Confidence
        overall_confidence = self._confidence_calc.calculate_confidence_index(
            home_stats=home_last_5,
            away_stats=away_last_5,
            h2h_stats=h2h_stats,
            poisson_probs=poisson_probs,
            mc_results=mc_results,
            league_code=match.league,
        )
        
        # 6. Aggregate probabilities (chart-ready)
        match_analysis = self._aggregate_probabilities(
            home_form=home_last_5,
            away_form=away_last_5,
            h2h=h2h_stats,
            poisson=poisson_probs,
            mc=mc_results,
        )
        
        # 7. Calculate enrichment data
        enrichment_data = self._calculate_enrichment(
            match=match,
            historical_matches=historical_matches,
        )
        
        # 8. Extract odds from match
        odds_data = None
        if match.b365h or match.b365d or match.b365a or match.b365_over25:
            odds_data = {
                "home_win": match.b365h,
                "draw": match.b365d,
                "away_win": match.b365a,
                "over_25": match.b365_over25,
                "under_25": match.b365_under25,
            }
        
        # 9. Build result
        league_name = self._league_names.get(match.league, match.league)
        
        # Construct Canonical TeamStats
        # Use enrichment data for season stats
        home_enrich = enrichment_data.get("home", {})
        away_enrich = enrichment_data.get("away", {})
        
        home_goals = home_enrich.get("goals", {})
        away_goals = away_enrich.get("goals", {})
        
        # Update form in stats objects (Side Effect)
        if home_last_5: home_last_5.form = home_enrich.get("form", "")
        if home_last_3: home_last_3.form = home_enrich.get("venue_form", {}).get("form", "")
        
        if away_last_5: away_last_5.form = away_enrich.get("form", "")
        if away_last_3: away_last_3.form = away_enrich.get("venue_form", {}).get("form", "")

        home_stats = TeamStats(
            last_5=home_last_5,
            venue_last_3=home_last_3,
            form=None, # Deprecated/Moved to last_5
            position=home_enrich.get("position"),
            points=home_enrich.get("points"),
            goals_scored=home_goals.get("total_scored", 0),
            goals_conceded=home_goals.get("total_conceded", 0),
        )
        
        away_stats = TeamStats(
            last_5=away_last_5,
            venue_last_3=away_last_3,
            form=None, # Deprecated/Moved to last_5
            position=away_enrich.get("position"),
            points=away_enrich.get("points"),
            goals_scored=away_goals.get("total_scored", 0),
            goals_conceded=away_goals.get("total_conceded", 0),
        )
        
        return SingleMatchAnalysis(
            match_id=match.id,
            home_team=match.home_team,
            away_team=match.away_team,
            date=match.match_date.isoformat() if match.match_date else "",
            time=match.match_time.strftime("%H:%M") if match.match_time else None,
            league=league_name,
            
            # Flattened Enrichment
            matchday=enrichment_data.get("matchday", 0),
            position_difference=enrichment_data.get("position_difference", 0),
            points_difference=enrichment_data.get("points_difference", 0),
            
            # Canonical Stats
            homeStats=home_stats,
            awayStats=away_stats,
            h2h_last_5=h2h_stats,
            
            # Core models
            poisson=poisson_probs,
            monte_carlo=mc_results,
            overall_confidence=overall_confidence,
            
            # Additional
            match_analysis=match_analysis,
            odds=odds_data,
        )

    def _match_to_dict(self, match: Match) -> dict:
        """Convert Match entity to dict for legacy calculators."""
        return {
            "home_team": match.home_team,
            "away_team": match.away_team,
            "HomeTeam": match.home_team,
            "AwayTeam": match.away_team,
            "FTHG": match.fthg,
            "FTAG": match.ftag,
            "fthg": match.fthg,
            "ftag": match.ftag,
            "Date": match.match_date.isoformat() if match.match_date else None,
            "date": match.match_date.isoformat() if match.match_date else None,
            "match_date": match.match_date.isoformat() if match.match_date else None,
            "Div": match.league,
            "league": match.league,
            "season": self._get_current_season(),
            "Season": self._get_current_season(),
        }
    
    def _build_recent_outcomes(
        self,
        home_team: str,
        away_team: str,
        historical_matches: List[dict],
        last_n: int = 5,
        as_of_date: Optional[date] = None,
    ) -> dict:
        """Build recent outcome lists for Monte Carlo adjustment."""
        home_lower = home_team.lower().strip()
        away_lower = away_team.lower().strip()
        
        combined_matches = []
        
        for match in historical_matches:
            match_home = str(match.get("home_team", "")).lower().strip()
            match_away = str(match.get("away_team", "")).lower().strip()
            
            if home_lower in [match_home, match_away] or away_lower in [match_home, match_away]:
                if as_of_date:
                    d_val = match.get("date") or match.get("match_date") or match.get("Date")
                    if d_val:
                        try:
                            if isinstance(d_val, str):
                                m_date = datetime.fromisoformat(d_val[:10]).date()
                            elif isinstance(d_val, datetime):
                                m_date = d_val.date()
                            elif isinstance(d_val, date):
                                m_date = d_val
                            else:
                                m_date = None
                            
                            if m_date and m_date >= as_of_date:
                                continue
                        except:
                            pass

                combined_matches.append(match)
        
        # Sort by date descending
        combined_matches.sort(
            key=lambda m: m.get("date", ""),
            reverse=True
        )
        combined_matches = combined_matches[:last_n * 2]
        
        outcomes = {
            "over_25": [],
            "btts": [],
            "home_win": [],
            "away_win": [],
            "draw": [],
            "goals_2_3": [],
        }
        
        for match in combined_matches[:last_n]:
            home_goals = int(match.get("fthg") or match.get("FTHG") or 0)
            away_goals = int(match.get("ftag") or match.get("FTAG") or 0)
            total = home_goals + away_goals
            
            outcomes["over_25"].append(total > 2.5)
            outcomes["btts"].append(home_goals > 0 and away_goals > 0)
            outcomes["home_win"].append(home_goals > away_goals)
            outcomes["away_win"].append(away_goals > home_goals)
            outcomes["draw"].append(home_goals == away_goals)
            outcomes["goals_2_3"].append(total in [2, 3])
        
        return outcomes
    
    def _aggregate_probabilities(
        self,
        home_form: TeamFormStats,
        away_form: TeamFormStats,
        h2h: H2HStats,
        poisson: PoissonProbabilities,
        mc: MonteCarloResults,
    ) -> MatchAnalysisResult:
        """Aggregate probabilities from all sources into chart-ready format."""
        from src.domain.aggregation import ProbabilityAggregator, SourceProbabilities
        from src.api.schemas import MatchAnalysisMarket, MatchAnalysisResult
        
        sources = SourceProbabilities(
            poisson_over_25=poisson.over_25,
            poisson_btts=poisson.btts,
            poisson_goals_2_3=poisson.goals_2_3,
            poisson_home_win=poisson.home_win,
            poisson_away_win=poisson.away_win,
            poisson_draw=poisson.draw,
            mc_over_25=mc.over_25.adjusted_probability,
            mc_btts=mc.btts.adjusted_probability,
            mc_goals_2_3=mc.goals_2_3.adjusted_probability,
            mc_home_win=mc.home_win.adjusted_probability,
            mc_away_win=mc.away_win.adjusted_probability,
            mc_draw=mc.draw.adjusted_probability,
            home_form_over_25=home_form.over_25_rate,
            home_form_btts=home_form.btts_rate,
            home_form_goals_2_3=home_form.goals_2_3_rate,
            away_form_over_25=away_form.over_25_rate,
            away_form_btts=away_form.btts_rate,
            away_form_goals_2_3=away_form.goals_2_3_rate,
            h2h_over_25=h2h.over_25_rate,
            h2h_btts=h2h.btts_rate,
            h2h_goals_2_3=h2h.goals_2_3_rate,
            h2h_home_win=h2h.home_win_rate,
            h2h_away_win=h2h.away_win_rate,
            h2h_draw=h2h.draw_rate,
            h2h_reliability=h2h.h2h_reliability,
        )
        
        result = ProbabilityAggregator().aggregate(sources)
        
        # Helper to map Domain AggregatedMarket -> API MatchAnalysisMarket
        def to_api(market) -> MatchAnalysisMarket:
            return MatchAnalysisMarket(
                probability=market.final_probability,
                probability_pct=f"{int(market.final_probability * 100)}%",
                confidence=market.confidence.value,
                verdict=market.verdict.value,
            )

        return MatchAnalysisResult(
            over_25=to_api(result.over_25),
            btts=to_api(result.btts),
            goals_2_3=to_api(result.goals_2_3),
            home_win=to_api(result.home_win),
            away_win=to_api(result.away_win),
            draw=to_api(result.draw),
            confidence_index=result.overall_confidence_index,
        )
    
    def _calculate_enrichment(
        self,
        match: Match,
        historical_matches: List[Match],
    ) -> dict:
        """Calculate enrichment data (matchday, standings, form strings)."""
        from src.domain.enrichment import MatchEnrichmentService
        
        # Load raw historical data from JSON storage (more reliable than entity conversion)
        from src.data.storage.json_storage import JSONStorage
        storage = JSONStorage()
        history_dicts = storage.load("data/processed/matches.json") or []
        
        # Get current season
        season = match.season if hasattr(match, 'season') and match.season else self._get_current_season()
        
        try:
            service = MatchEnrichmentService()
            result = service.enrich_match(
                home_team=match.home_team,
                away_team=match.away_team,
                match_date=match.match_date,
                league_code=match.league,
                season=season,
                historical_matches=history_dicts,
            )
            return result.to_dict()
        except Exception as e:
            self.logger.error(f"Enrichment failed: {e}")
            return {}
    
    def _get_current_season(self) -> str:
        """Get current season string in format matching data (e.g., 2024-25)."""
        from datetime import date
        today = date.today()
        if today.month >= 8:
            start_year = today.year
            end_year = (today.year + 1) % 100  # Last 2 digits
            return f"{start_year}-{end_year:02d}"
        else:
            start_year = today.year - 1
            end_year = today.year % 100  # Last 2 digits
            return f"{start_year}-{end_year:02d}"


# ============== Use Case ==============

class AnalyzeMatchesUseCase:
    """
    Use Case: Analyze upcoming matches with statistical calculations.
    
    Responsibilities:
    - Orchestrate match analysis workflow
    - Coordinate between repository and analyzer
    - Handle pagination
    - Enrich with AI analysis if requested
    
    All dependencies are injected for testability.
    """
    
    def __init__(
        self,
        upcoming_repository,  # IUpcomingMatchRepository
        historical_repository,  # IHistoricalMatchRepository
        match_analyzer: MatchAnalyzer,
        ai_analyzer: Optional[IAIAnalyzer] = None,
        backtest_service: Optional[IBacktestService] = None,
    ):
        self._upcoming_repo = upcoming_repository
        self._historical_repo = historical_repository
        self._match_analyzer = match_analyzer
        self._ai_analyzer = ai_analyzer
        self._backtest_service = backtest_service
    
    def execute(self, request: AnalyzeMatchesRequest) -> AnalyzeMatchesResult:
        """Execute the analyze matches use case."""
        logger.info(f"Analyzing matches: date={request.date}, page={request.page}")
        
        # 1. Load matches - check if we should use daily CSV for past dates
        upcoming_matches = self._load_matches_for_date(request.date)
        
        if not upcoming_matches:
            logger.info("No matches found")
            return AnalyzeMatchesResult(
                analyses=[],
                total=0,
                page=request.page,
                limit=request.limit,
                generated_at=datetime.now().isoformat(),
                backtest_stats={},  # Empty stats
            )
        
        total = len(upcoming_matches)
        logger.info(f"Found {total} matches to analyze")
        
        # 2. Apply pagination BEFORE analysis (efficiency)
        offset = (request.page - 1) * request.limit
        paginated = upcoming_matches[offset:offset + request.limit]
        
        # 3. Load historical data once
        historical = self._historical_repo.get_all()
        
        # 4. Analyze each match
        analyses = []
        for match in paginated:
            try:
                analysis = self._match_analyzer.analyze(
                    match=match,
                    historical_matches=historical,
                )
                analyses.append(analysis)
            except Exception as e:
                logger.error(f"Failed to analyze match {match.id}: {e}")
                # Continue with other matches
        
        logger.info(f"Analyzed {len(analyses)} matches successfully")
        
        # 5. Enrich with AI if requested
        if self._ai_analyzer:
            try:
                analyses = self._ai_analyzer.enrich_batch(
                    analyses,
                    date=request.date.isoformat() if request.date else None,
                    refresh=request.refresh,
                )
            except Exception as e:
                logger.error(f"AI analysis failed: {e}")
                
        # 6. Calculate backtest stats (if service available)
        backtest_stats = {}
        if self._backtest_service:
            try:
                # Assuming service returns summary stats
                backtest_stats = self._backtest_service.calculate_stats(analyses)
            except Exception as e:
                logger.error(f"Backtest calculation failed: {e}")

        return AnalyzeMatchesResult(
            analyses=analyses,
            total=total,
            page=request.page,
            limit=request.limit,
            generated_at=datetime.now().isoformat(),
            backtest_stats=backtest_stats,
        )
    
    def _load_matches_for_date(self, target_date: Optional[date]) -> List[Match]:
        """
        Load matches for a given date.
        
        For past dates, attempts to load from daily CSV files first.
        Falls back to the standard upcoming repository.
        """
        import os
        from datetime import datetime as dt
        
        if not target_date:
            # No date filter, use standard repo
            return self._upcoming_repo.get_by_date(None)
        
        # Check if this is a past date
        today = dt.now().date()
        is_past = target_date < today
        
        date_str = target_date.isoformat()
        
        if is_past:
            # Try to load from daily CSV
            daily_csv_path = f"data/raw/upcoming/daily/fixtures_{date_str}.csv"
            if os.path.exists(daily_csv_path):
                logger.info(f"Loading from daily CSV: {daily_csv_path}")
                return self._load_matches_from_csv(daily_csv_path, date_str)
        
        # Fall back to standard repository
        return self._upcoming_repo.get_by_date(date_str)
    
    def _load_matches_from_csv(self, csv_path: str, date_str: str) -> List[Match]:
        """Load Match entities from a daily CSV file."""
        import csv
        from datetime import datetime as dt
        
        matches = []
        
        try:
            with open(csv_path, 'r', encoding='utf-8') as f:
                reader = csv.DictReader(f)
                for row in reader:
                    home_team = row.get("HomeTeam", "")
                    away_team = row.get("AwayTeam", "")
                    
                    match_id = f"{date_str}_{home_team}_{away_team}".replace(" ", "_")
                    
                    # Parse date from CSV Date column (more accurate than filename)
                    csv_date = row.get("Date", date_str)
                    try:
                        match_date = dt.strptime(csv_date, "%Y-%m-%d").date()
                    except:
                        match_date = dt.strptime(date_str, "%Y-%m-%d").date()
                    
                    # Parse time
                    match_time = None
                    time_str = row.get("Time", "")
                    if time_str:
                        try:
                            match_time = dt.strptime(time_str, "%H:%M").time()
                        except:
                            pass
                    
                    # Parse odds columns
                    def safe_float(val):
                        try:
                            return float(val) if val else None
                        except (ValueError, TypeError):
                            return None
                    
                    match = Match(
                        id=match_id,
                        home_team=home_team,
                        away_team=away_team,
                        match_date=match_date,
                        match_time=match_time,
                        league=row.get("Div", ""),
                        season="2025-26",
                        fthg=None,
                        ftag=None,
                        ftr=None,
                        # Odds from fixture CSV
                        b365h=safe_float(row.get("B365H")),
                        b365d=safe_float(row.get("B365D")),
                        b365a=safe_float(row.get("B365A")),
                        b365_over25=safe_float(row.get("B365>2.5")),
                        b365_under25=safe_float(row.get("B365<2.5")),
                    )
                    matches.append(match)
            
            logger.info(f"Loaded {len(matches)} matches from {csv_path}")
            
        except Exception as e:
            logger.error(f"Error loading CSV {csv_path}: {e}")
        
        return matches
