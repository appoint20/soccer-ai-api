#!/usr/bin/env python3
"""
Initial Data Load Script

Loads all historical Excel data files from data/raw/historical/,
processes them, and saves to data/processed/matches.json.

Usage:
    python scripts/initial_data_load.py

The script will:
1. Scan the historical data directory for Excel/CSV files
2. Parse league code and season from filenames
3. Load and process each file
4. Combine all data and save to JSON
5. Output statistics about the loaded data
"""
import sys
from pathlib import Path

# Add project root to path for imports
project_root = Path(__file__).parent.parent
sys.path.insert(0, str(project_root))

from src.utils.config import get_config
from src.utils.logger import setup_logging, get_logger
from src.data.loaders import ExcelLoader, DataProcessor
from src.data.storage import JSONStorage


def parse_filename(filename: str) -> tuple[str, str]:
    """
    Parse league code and season from filename.
    
    Expected formats:
    - E0_2324.xlsx -> league='E0', season='2023-24'
    - E0.xlsx -> league='E0', season='Unknown'
    - Premier_League_2023-24.xlsx -> league='E0', season='2023-24'
    
    Args:
        filename: Name of the file (without path)
        
    Returns:
        Tuple of (league_code, season)
    """
    name = Path(filename).stem  # Remove extension
    
    # Try pattern: E0_2324 or SP1_2425
    parts = name.split('_')
    if len(parts) >= 2:
        league = parts[0].upper()
        season_part = parts[-1]
        
        # Convert 2324 -> 2023-24
        if len(season_part) == 4 and season_part.isdigit():
            year1 = f"20{season_part[:2]}"
            year2 = season_part[2:]
            season = f"{year1}-{year2}"
        elif '-' in season_part:
            season = season_part
        else:
            season = "Unknown"
        
        return league, season
    
    # Just league code
    return name.upper(), "Unknown"


def find_data_files(directory: Path) -> list[Path]:
    """Find all Excel and CSV files in directory."""
    files = []
    
    if not directory.exists():
        return files
    
    for pattern in ['*.xlsx', '*.xls', '*.csv']:
        files.extend(directory.glob(pattern))
    
    # Sort by name for consistent ordering
    return sorted(files)


def main():
    """Main entry point for initial data loading."""
    # Setup
    config = get_config()
    config.ensure_directories()
    setup_logging(
        log_level=config.log_level,
        log_file=str(config.logs_path / "initial_load.log"),
    )
    logger = get_logger("InitialDataLoad")
    
    logger.info("=" * 60)
    logger.info("Starting Initial Data Load")
    logger.info("=" * 60)
    
    # Initialize components
    excel_loader = ExcelLoader()
    data_processor = DataProcessor()
    json_storage = JSONStorage()
    
    # Find data files
    historical_dir = config.data_raw_path / "historical"
    files = find_data_files(historical_dir)
    
    if not files:
        logger.warning(f"No data files found in {historical_dir}")
        logger.info("Please add Excel/CSV files to the historical directory")
        logger.info(f"Expected path: {historical_dir}")
        return
    
    logger.info(f"Found {len(files)} data files")
    
    # Process each file
    all_matches = []
    league_stats = {}
    season_stats = {}
    
    for file_path in files:
        logger.info(f"Processing: {file_path.name}")
        
        # Parse filename
        league_code, season = parse_filename(file_path.name)
        logger.debug(f"  League: {league_code}, Season: {season}")
        
        # Load data
        df = excel_loader.load(file_path, league_code, season)
        
        if df is None or df.empty:
            logger.warning(f"  Skipped: No valid data")
            continue
        
        # Process data
        processed_df = data_processor.process_historical_data(df)
        
        if processed_df.empty:
            logger.warning(f"  Skipped: Processing resulted in no data")
            continue
        
        # Convert to Match entities
        matches = data_processor.convert_to_matches(processed_df)
        
        if not matches:
            logger.warning(f"  Skipped: No matches converted")
            continue
        
        # Update statistics
        league_stats[league_code] = league_stats.get(league_code, 0) + len(matches)
        season_stats[season] = season_stats.get(season, 0) + len(matches)
        
        all_matches.extend(matches)
        logger.info(f"  Loaded: {len(matches)} matches")
    
    # Convert matches to dictionaries for JSON storage
    matches_data = [match.to_dict() for match in all_matches]
    
    # Save to JSON
    output_path = config.data_processed_path / "matches.json"
    success = json_storage.save(matches_data, output_path)
    
    if success:
        logger.info(f"Saved {len(matches_data)} matches to {output_path}")
    else:
        logger.error(f"Failed to save matches to {output_path}")
        return
    
    # Print summary
    logger.info("")
    logger.info("=" * 60)
    logger.info("LOAD COMPLETE - SUMMARY")
    logger.info("=" * 60)
    logger.info(f"Total Matches: {len(all_matches)}")
    logger.info("")
    
    logger.info("Matches by League:")
    for league, count in sorted(league_stats.items()):
        league_name = config.get_league_name(league)
        logger.info(f"  {league} ({league_name}): {count}")
    
    logger.info("")
    logger.info("Matches by Season:")
    for season, count in sorted(season_stats.items()):
        logger.info(f"  {season}: {count}")
    
    # Calculate date range
    if all_matches:
        dates = [m.match_date for m in all_matches if m.match_date]
        if dates:
            min_date = min(dates)
            max_date = max(dates)
            logger.info("")
            logger.info(f"Date Range: {min_date} to {max_date}")
    
    # Over 2.5 and BTTS rates
    completed_matches = [m for m in all_matches if m.is_completed]
    if completed_matches:
        over25_count = sum(1 for m in completed_matches if m.is_over_25)
        btts_count = sum(1 for m in completed_matches if m.is_btts)
        
        logger.info("")
        logger.info("Overall Statistics:")
        logger.info(f"  Over 2.5 Rate: {over25_count/len(completed_matches)*100:.1f}%")
        logger.info(f"  BTTS Rate: {btts_count/len(completed_matches)*100:.1f}%")
    
    logger.info("")
    logger.info("=" * 60)
    logger.info("Initial data load complete!")
    logger.info("=" * 60)


if __name__ == "__main__":
    main()
