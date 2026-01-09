import asyncio
import sys
import os

# Add project root to path
sys.path.append(os.getcwd())

from src.api.routers.backtest import run_backtest, BacktestRequest
from src.api.dependencies import ServiceContainer, get_match_analyzer

async def main():
    print("Initializing services...")
    ServiceContainer.init_services()
    
    analyzer = get_match_analyzer()
    hist_matches = ServiceContainer.historical_matches
    
    req = BacktestRequest(
        start_date="2025-09-15",
        end_date="2025-12-29"
    )
    
    print(f"Running backtest for {req.start_date} to {req.end_date}...")
    try:
        # Import directly to avoid dependency injection complexity of FastAPI wrapper if possible
        # But run_backtest expects dependencies passed in.
        result = await run_backtest(
            request=req,
            match_analyzer=analyzer,
            historical_matches=hist_matches
        )
        
        print("\n=== Backtest Report ===")
        # Write to file to avoid truncation
        with open("backtest_output.json", "w") as f:
            f.write(result.json())
        print("Report written to backtest_output.json")
        
    except Exception as e:
        print(f"Error: {e}")
        import traceback
        traceback.print_exc()

if __name__ == "__main__":
    asyncio.run(main())
