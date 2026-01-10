from typing import Dict, Any, Optional
from dataclasses import asdict
from src.api.schemas import (
    MatchAnalysis,
    TeamStats,
    TeamFormStats,
    VenueFormStats,
    H2HStats,
    PoissonProbabilities,
    MonteCarloResults,
    AIAnalysis,
    MatchAnalysisResult,
    MatchAnalysisMarket,
    BacktestResult,
)
from src.application.use_cases.analyze_matches import SingleMatchAnalysis


class MatchAnalysisPresenter:
    """
    Presenter: Convert domain objects to API response schemas.
    
    Responsibilities:
    Presenter for Match Analysis.
    
    Responsible for mapping domain entity (SingleMatchAnalysis) 
    to API response (MatchAnalysis).
    
    Since we now use a Canonical Schema, this is mostly a pass-through
    with type mapping.
    """
    
    @staticmethod
    def to_response(
        analysis: SingleMatchAnalysis,
    ) -> MatchAnalysis:
        """Map canonical domain object to API schema."""
        
        # Extract backtest result if available
        api_backtest_res = None
        if analysis.backtest_result:
             # Convert dict to BacktestResult schema
             api_backtest_res = BacktestResult(**analysis.backtest_result)
        
        return MatchAnalysis(
            match_id=analysis.match_id,
            home_team=analysis.home_team,
            away_team=analysis.away_team,
            date=analysis.date,
            time=analysis.time,
            league=analysis.league,
            
            # Enrichment (Flattened)
            matchday=analysis.matchday,
            position_difference=analysis.position_difference,
            points_difference=analysis.points_difference,
            
            # Canonical Stats (Convert dataclasses to dicts)
            homeStats=asdict(analysis.homeStats),
            awayStats=asdict(analysis.awayStats),
            h2h_last_5=asdict(analysis.h2h_last_5),
            
            # Models (Convert dataclasses to dicts)
            poisson=asdict(analysis.poisson),
            monte_carlo=asdict(analysis.monte_carlo),
            overall_confidence=analysis.overall_confidence,
            ai_analysis=MatchAnalysisPresenter._build_ai_analysis(analysis),
            match_analysis=MatchAnalysisPresenter._build_match_analysis(analysis),
            odds=analysis.odds,
            backtest_result=api_backtest_res,
        )
    
    @staticmethod
    def _build_ai_analysis(analysis: SingleMatchAnalysis) -> Optional[AIAnalysis]:
        """Pass through AI analysis."""
        return analysis.ai_analysis

    @staticmethod
    def _build_match_analysis(analysis: SingleMatchAnalysis) -> Optional[MatchAnalysisResult]:
        """Pass through match analysis result."""
        return analysis.match_analysis
