import asyncio
import sys
import os
from pathlib import Path
from datetime import datetime

# Add project root to path
sys.path.append(os.getcwd())

from src.api.dependencies import ServiceContainer, get_time_travel_backtest_service
from src.utils.logger import get_logger

logger = get_logger("VerifyTimeTravel")

async def main():
    logger.info("Initializing services...")
    ServiceContainer.init_services()
    
    service = get_time_travel_backtest_service()
    
    # 1. Manually construct a match that happened recently (e.g., yesterday or last week)
    # We need a match that EXISTS in our historical DB so we can verify the result.
    # Let's pick a date from last season or a few weeks ago.
    # We can inspect the historical repo for a candidate.
    
    matches = service.historical_repo.get_all()
    if not matches:
        logger.error("No historical matches found!")
        return

    # Sort by date desc
    matches.sort(key=lambda x: x.match_date, reverse=True)
    
    # Pick 5 matches from a month ago
    # Filter matches older than 14 days to be safe
    cutoff = datetime.now().date()
    
    # Pick matches from e.g. 2024 or late 2025
    target_matches = matches[100:105] # Grab a slice
    
    logger.info(f"Selected {len(target_matches)} matches for backtest verification.")
    for m in target_matches:
        logger.info(f" - {m.match_date}: {m.home_team} vs {m.away_team} [{m.ftr}]")
    
    # Adapt to Dict format expected by service
    adapted = []
    for m in target_matches:
        adapted.append({
            "Date": m.match_date,
            "HomeTeam": m.home_team,
            "AwayTeam": m.away_team,
            "Div": m.league,
            "FTR": m.ftr,
            "FTHG": m.fthg,
            "FTAG": m.ftag
        })
        
    logger.info("Running Time Travel Backtest...")
    results = await service.run_backtest(adapted)
    
    logger.info("Backtest Complete!")
    logger.info(f"Summary: {results}")
    
    # Check chart data
    if "chart_data" in results:
        logger.info(f"Chart Data Points: {len(results['chart_data'])}")
        logger.info(f"First 3 points: {results['chart_data'][:3]}")
    else:
        logger.error("Missing chart_data in response!")

if __name__ == "__main__":
    asyncio.run(main())
