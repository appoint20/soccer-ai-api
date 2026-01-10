from dataclasses import dataclass
from typing import List
import time

from src.domain.value_objects.analysis_date import AnalysisDate
from src.application.use_cases.precompute_analysis import PrecomputeAnalysisUseCase, PrecomputeRequest, PrecomputeResult
from src.utils.logger import get_logger

logger = get_logger("PrecomputeBatch")

@dataclass
class PrecomputeDateRangeRequest:
    """Request to precompute multiple dates."""
    dates: List[AnalysisDate]
    force_refresh: bool = False
    stop_on_error: bool = False

@dataclass
class PrecomputeDateRangeResult:
    """Aggregate result for date range."""
    total_dates: int
    successful_dates: int
    failed_dates: int
    total_matches_analyzed: int
    total_matches_cached: int
    total_duration_seconds: float
    date_results: List[PrecomputeResult]

class PrecomputeDateRangeUseCase:
    """
    Use Case: Precompute analyses for multiple dates.
    """
    
    def __init__(self, precompute_use_case: PrecomputeAnalysisUseCase):
        self._uc = precompute_use_case
    
    def execute(self, request: PrecomputeDateRangeRequest) -> PrecomputeDateRangeResult:
        start_time = time.time()
        
        logger.info(f"Starting batch precomputation for {len(request.dates)} dates")
        
        results = []
        successful = 0
        failed = 0
        total_analyzed = 0
        total_cached = 0
        
        for date in request.dates:
            try:
                # Execute single date precomputation
                res = self._uc.execute(PrecomputeRequest(
                    date=date, 
                    force_refresh=request.force_refresh
                ))
                
                results.append(res)
                
                if not res.errors:
                    successful += 1
                else:
                    failed += 1
                
                total_analyzed += res.matches_analyzed
                total_cached += res.matches_cached
                
            except Exception as e:
                logger.error(f"Failed to precompute {date}: {e}")
                failed += 1
                # Create detailed error result if needed, or just continue
                if request.stop_on_error:
                    break
        
        duration = time.time() - start_time
        
        logger.info(
            f"Batch precomputation complete: "
            f"{successful}/{len(request.dates)} successful, "
            f"{total_cached} matches cached, "
            f"{duration:.2f}s"
        )
        
        return PrecomputeDateRangeResult(
            total_dates=len(request.dates),
            successful_dates=successful,
            failed_dates=failed,
            total_matches_analyzed=total_analyzed,
            total_matches_cached=total_cached,
            total_duration_seconds=duration,
            date_results=results,
        )
