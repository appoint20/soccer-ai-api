#!/usr/bin/env python3
"""
Precompute Service Entry Point.

This script coordinates the nightly precomputation of match analyses.
It uses Clean Architecture principles with dependency injection.
"""
import sys
import os

# Add src to path
sys.path.append(os.getcwd())

from src.utils.logger import setup_logging, get_logger
from src.api.dependencies import ServiceContainer, get_match_analyzer, _create_ai_analyzer
from src.infrastructure.repositories.fixture_repository import CSVFixtureRepository
from src.infrastructure.repositories.historical_match_repository import CSVHistoricalMatchRepository
from src.infrastructure.repositories.time_travel_historical_repository import TimeTravelHistoricalMatchRepository
from src.infrastructure.repositories.analysis_cache_repository import FirestoreAnalysisCacheRepository
from src.application.use_cases.precompute_analysis import PrecomputeAnalysisUseCase
from src.application.use_cases.precompute_date_range import PrecomputeDateRangeUseCase, PrecomputeDateRangeRequest, PrecomputeDateRangeResult

def main():
    setup_logging()
    logger = get_logger("PrecomputeService")
    
    logger.info("Initializing services...")
    
    # 1. Initialize core services (needed for MatchAnalyzer dependencies like Dixon-Coles)
    ServiceContainer.init_services()
    
    # 2. Wire Dependencies
    try:
        # Fixture Repository (Source of truth for what to analyze)
        fixture_repo = CSVFixtureRepository()
        
        # Historical Repository (Source of truth for past data)
        # Using CSV based repo as it contains the most up-to-date data including 2026
        base_hist_repo = CSVHistoricalMatchRepository()
        
        # Time Travel Decorator (Prevents data leakage)
        time_travel_repo = TimeTravelHistoricalMatchRepository(base_hist_repo)
        
        # Analyzers
        # MatchAnalyzer (Statistical models) - Dependencies auto-wired by get_match_analyzer
        match_analyzer = get_match_analyzer()
        
        # AI Analyzer (Gemini)
        ai_analyzer = _create_ai_analyzer()
        
        # Cache Repository (Persistence)
        cache_repo = FirestoreAnalysisCacheRepository()
        
        # 3. Create Use Cases
        precompute_uc = PrecomputeAnalysisUseCase(
            fixture_repository=fixture_repo,
            historical_repository=time_travel_repo,
            match_analyzer=match_analyzer,
            ai_analyzer=ai_analyzer,
            cache_repository=cache_repo
        )
        
        batch_uc = PrecomputeDateRangeUseCase(precompute_uc)
        
        # 4. Determine scope (Next N days)
        # For now, analyze all available upcoming fixtures
        available_dates = fixture_repo.get_available_dates()
        
        if not available_dates:
            logger.warning("No future fixtures found to analyze.")
            return

        logger.info(f"Found {len(available_dates)} dates with fixtures.")
        
        # Filter for future/recent dates only? 
        # For now analyze all found fixture files (backlog catchup + future)
        
        request = PrecomputeDateRangeRequest(
            dates=available_dates,
            force_refresh=False, # Use cache if available
            stop_on_error=False
        )
        
        # 5. Execute
        result = batch_uc.execute(request)
        
        # 6. Report
        _print_summary(result)
        
    except Exception as e:
        logger.critical(f"Precompute service failed: {e}", exc_info=True)
        sys.exit(1)

def _print_summary(result: PrecomputeDateRangeResult):
    print("\n" + "="*60)
    print("PRECOMPUTE SUMMARY")
    print("="*60)
    print(f"Dates Processed: {result.successful_dates}/{result.total_dates}")
    print(f"Total Matches Analyzed: {result.total_matches_analyzed}")
    print(f"Total Matches Cached: {result.total_matches_cached}")
    print(f"Duration: {result.total_duration_seconds:.2f}s")
    print("-" * 60)
    
    if result.failed_dates > 0:
        print(f"WARNING: {result.failed_dates} dates failed.")
        
    for res in result.date_results:
        status = "✅" if not res.errors else "❌"
        print(f"{status} {res.date.to_string()}: "
              f"{res.matches_cached} cached, "
              f"{res.matches_failed} failed")
        if res.errors:
            for err in res.errors[:3]: # Show first 3 errors
                print(f"    - {err}")
    print("="*60 + "\n")

if __name__ == "__main__":
    main()
