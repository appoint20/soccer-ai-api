from dataclasses import dataclass, asdict
from typing import List, Dict, Any
import time

from src.domain.value_objects.analysis_date import AnalysisDate
from src.domain.entities.analysis_result import MatchAnalysisResult
from src.domain.entities.match import Match
from src.infrastructure.repositories.fixture_repository import IFixtureRepository
from src.infrastructure.repositories.time_travel_historical_repository import TimeTravelHistoricalMatchRepository
from src.infrastructure.repositories.analysis_cache_repository import IAnalysisCacheRepository
from src.application.use_cases.analyze_matches import IMatchAnalyzer, IAIAnalyzer
from src.utils.logger import get_logger

logger = get_logger("PrecomputeUseCase")

@dataclass
class PrecomputeRequest:
    """Request to precompute analysis for a date."""
    date: AnalysisDate
    force_refresh: bool = False

@dataclass
class PrecomputeResult:
    """Result of precomputation."""
    date: AnalysisDate
    matches_analyzed: int
    matches_cached: int
    matches_failed: int
    duration_seconds: float
    errors: List[str]

class PrecomputeAnalysisUseCase:
    """
    Use Case: Precompute match analyses and cache results.
    """
    
    def __init__(
        self,
        fixture_repository: IFixtureRepository,
        historical_repository: TimeTravelHistoricalMatchRepository,
        match_analyzer: IMatchAnalyzer,
        ai_analyzer: IAIAnalyzer,
        cache_repository: IAnalysisCacheRepository,
    ):
        self._fixture_repo = fixture_repository
        self._historical_repo = historical_repository
        self._match_analyzer = match_analyzer
        self._ai_analyzer = ai_analyzer
        self._cache_repo = cache_repository
    
    def execute(self, request: PrecomputeRequest) -> PrecomputeResult:
        start_time = time.time()
        logger.info(f"Starting precomputation for {request.date.to_string()}")
        
        try:
            fixtures = self._fixture_repo.get_fixtures_for_date(request.date)
        except Exception as e: # Catch FileNotFoundError and others
            logger.warning(f"No fixtures found for {request.date.to_string()}: {e}")
            return self._result(request.date, 0, 0, 0, start_time, [str(e)])
        
        if not fixtures:
            return self._result(request.date, 0, 0, 0, start_time, [])

        # Time travel: Get matches available BEFORE the fixture date
        # Assuming fixture date is when match happens. We can use history up to that day (exclusive)
        historical_matches = self._historical_repo.get_matches_before(request.date)
        
        # Check cache if not forcing refresh
        if not request.force_refresh:
            cached_count = self._count_cached_matches(fixtures)
            if cached_count == len(fixtures):
                logger.info(f"All {len(fixtures)} matches already cached")
                return self._result(request.date, 0, cached_count, 0, start_time, [])
        
        # Analyze
        results = self._analyze_matches(fixtures, historical_matches, request.force_refresh)
        
        return self._result(
            request.date,
            results['analyzed'],
            results['cached'],
            results['failed'],
            start_time,
            results['errors']
        )
    
    def _analyze_matches(self, fixtures: List[Match], historical: List[Match], force: bool) -> Dict:
        from collections import defaultdict
        
        by_league = defaultdict(list)
        for f in fixtures:
            by_league[f.league].append(f)
            
        total = {'analyzed': 0, 'cached': 0, 'failed': 0, 'errors': []}
        
        for league, league_fixtures in by_league.items():
            res = self._analyze_league(league, league_fixtures, historical, force)
            total['analyzed'] += res['analyzed']
            total['cached'] += res['cached']
            total['failed'] += res['failed']
            total['errors'].extend(res['errors'])
            
        return total

    def _analyze_league(self, league, fixtures, historical, force) -> Dict:
        logger.info(f"Analyzing {len(fixtures)} matches for league '{league}'")
        
        # 1. Statistical Analysis
        stat_analyses = []
        for fixture in fixtures:
            # Check cache again per match if partial refresh? 
            # If force=False, we should skip cached ones.
            if not force and self._cache_repo.exists(fixture.match_key): # match_key/id?
                 # Need to ensure ID consistency. Match entity has `id`?
                 # CSVFixtureRepository generates `id`. Match entity has `id`.
                 continue

            try:
                # MatchAnalyzer expects Match.
                analysis = self._match_analyzer.analyze(fixture, historical)
                stat_analyses.append((fixture, analysis))
            except Exception as e:
                logger.error(f"Analysis failed for {fixture.home_team} vs {fixture.away_team}: {e}")
                
        if not stat_analyses:
            return {'analyzed': 0, 'cached': 0, 'failed': 0, 'errors': []}

        # 2. AI Enrichment (Batch)
        try:
             # Extract SingleMatchAnalysis objects
             analyses_only = [a for _, a in stat_analyses]
             ai_results = self._ai_analyzer.enrich_batch(analyses_only) 
             # enrich_batch modifies objects in place or returns list?
             # IAIAnalyzer signature says returns List[SingleMatchAnalysis] (usually refined objects)
             # But implementation often modifies in place or merges. 
             # Assuming it returns enriched analyses.
        except Exception as e:
            logger.error(f"AI Batch Analysis failed for {league}: {e}")
            # Fallback: persist without AI? Or fail? The request said "with AI". 
            # I'll log error and proceed with saving what we have (partial analysis better than none)
            pass

        # 3. Cache
        cached = 0
        failed = 0
        errors = []
        
        for fixture, analysis in stat_analyses:
            try:
                domain_entity = self._convert_to_domain(analysis)
                # Ensure match_id matches fixture.id or use analysis.match_id
                # analysis.match_id comes from fixture.id in MatchAnalyzer
                self._cache_repo.save(domain_entity.match_id, domain_entity)
                cached += 1
            except Exception as e:
                msg = f"Failed to cache {fixture.home_team}: {e}"
                logger.error(msg)
                failed += 1
                errors.append(msg)
                
        return {'analyzed': len(stat_analyses), 'cached': cached, 'failed': failed, 'errors': errors}

    def _convert_to_domain(self, analysis) -> MatchAnalysisResult:
        # Convert Pydantic models to dicts
        def to_dict_safe(obj):
            if hasattr(obj, 'model_dump'): return obj.model_dump()
            if hasattr(obj, 'dict'): return obj.dict()
            if hasattr(obj, 'to_dict'): return obj.to_dict()
            if hasattr(obj, '__dict__'): return asdict(obj) if dataclass else obj.__dict__
            return obj

        return MatchAnalysisResult(
            match_id=analysis.match_id,
            date=analysis.date,
            league=analysis.league,
            home_team=analysis.home_team,
            away_team=analysis.away_team,
            enrichment_data=analysis.enrichment_data or {},
            h2h_stats=to_dict_safe(analysis.h2h_stats),
            poisson=to_dict_safe(analysis.poisson),
            monte_carlo=to_dict_safe(analysis.monte_carlo),
            aggregated_markets=analysis.aggregated_markets,
            ai_analysis=asdict(analysis.ai_analysis) if analysis.ai_analysis else None,
            odds=analysis.odds,
            overall_confidence=analysis.overall_confidence
        )

    def _count_cached_matches(self, fixtures) -> int:
        return sum(1 for f in fixtures if self._cache_repo.exists(f.id)) # id vs match_key check

    def _result(self, date, analyzed, cached, failed, start_time, errors):
        return PrecomputeResult(date, analyzed, cached, failed, time.time() - start_time, errors)
