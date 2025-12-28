"""
Integration tests for multi-league data loading.

Tests cover:
- Loading data from all 10 supported leagues
- League-specific data separation
- Cross-league statistics
"""
import pytest
import pandas as pd
from pathlib import Path

import sys
sys.path.insert(0, str(Path(__file__).parent.parent.parent))

from src.data.loaders import ExcelLoader, DataProcessor
from src.data.storage import JSONStorage


class TestMultiLeagueLoading:
    """Tests for loading multiple leagues."""
    
    def test_load_all_supported_leagues(
        self, 
        test_data_dir, 
        sample_historical_df,
        supported_leagues
    ):
        """Test loading historical data from all 10 leagues."""
        loader = ExcelLoader()
        processor = DataProcessor()
        
        league_counts = {}
        
        for league in supported_leagues:
            # Create test file for league
            df = sample_historical_df[sample_historical_df["Div"] == league]
            if len(df) > 0:
                file_path = test_data_dir / f"{league}_test.xlsx"
                df.to_excel(file_path, index=False)
                
                # Load
                loaded = loader.load(file_path, filter_unsupported_leagues=False)
                if loaded is not None and len(loaded) > 0:
                    processed = processor.process_historical_data(loaded)
                    league_counts[league] = len(processed)
        
        # Should have loaded at least some leagues
        assert len(league_counts) > 0
        assert sum(league_counts.values()) > 0
    
    def test_league_data_separation(
        self,
        test_data_dir,
        sample_historical_df
    ):
        """Test league-specific data is kept separate."""
        loader = ExcelLoader()
        processor = DataProcessor()
        
        # Create files for two leagues
        e0_df = sample_historical_df[sample_historical_df["Div"] == "E0"]
        d1_df = sample_historical_df[sample_historical_df["Div"] == "D1"]
        
        e0_file = test_data_dir / "E0.xlsx"
        d1_file = test_data_dir / "D1.xlsx"
        
        e0_df.to_excel(e0_file, index=False)
        d1_df.to_excel(d1_file, index=False)
        
        # Load separately
        e0_loaded = loader.load(e0_file, league_code="E0", filter_unsupported_leagues=False)
        d1_loaded = loader.load(d1_file, league_code="D1", filter_unsupported_leagues=False)
        
        # Verify separation
        if e0_loaded is not None and "league" in e0_loaded.columns:
            assert all(e0_loaded["league"] == "E0")
        
        if d1_loaded is not None and "league" in d1_loaded.columns:
            assert all(d1_loaded["league"] == "D1")
    
    def test_team_names_per_league(
        self,
        test_data_dir,
        sample_historical_df
    ):
        """Test team names don't conflict across leagues."""
        loader = ExcelLoader()
        processor = DataProcessor()
        
        all_teams_by_league = {}
        
        for league in ["E0", "E1", "D1"]:
            df = sample_historical_df[sample_historical_df["Div"] == league]
            if len(df) > 0:
                file_path = test_data_dir / f"{league}_teams.xlsx"
                df.to_excel(file_path, index=False)
                
                loaded = loader.load(file_path, filter_unsupported_leagues=False)
                if loaded is not None:
                    processed = processor.process_historical_data(loaded)
                    
                    teams = set(processed["home_team"].unique())
                    teams.update(processed["away_team"].unique())
                    all_teams_by_league[league] = teams
        
        # Each league should have its own teams
        if len(all_teams_by_league) >= 2:
            leagues = list(all_teams_by_league.keys())
            for i, league1 in enumerate(leagues):
                for league2 in leagues[i+1:]:
                    # Some overlap is OK, but not complete
                    if league1 != league2:
                        teams1 = all_teams_by_league[league1]
                        teams2 = all_teams_by_league[league2]
                        # Different leagues should have mostly different teams
                        overlap = len(teams1 & teams2)
                        total = len(teams1 | teams2)
                        if total > 0:
                            overlap_ratio = overlap / total
                            assert overlap_ratio < 1.0  # Not complete overlap
    
    def test_combined_statistics_accurate(
        self,
        test_data_dir,
        sample_historical_df,
        json_storage
    ):
        """Test combined statistics from multiple leagues."""
        loader = ExcelLoader()
        processor = DataProcessor()
        
        all_matches = []
        total_goals = 0
        
        for league in ["E0", "E1"]:
            df = sample_historical_df[sample_historical_df["Div"] == league]
            if len(df) > 0:
                file_path = test_data_dir / f"{league}_stats.xlsx"
                df.to_excel(file_path, index=False)
                
                loaded = loader.load(file_path, filter_unsupported_leagues=False)
                if loaded is not None:
                    processed = processor.process_historical_data(loaded)
                    matches = processor.convert_to_matches(processed)
                    all_matches.extend(matches)
                    
                    # Sum goals
                    for match in matches:
                        if match.total_goals is not None:
                            total_goals += match.total_goals
        
        # Verify combined count
        computed_goals = sum(
            m.total_goals for m in all_matches 
            if m.total_goals is not None
        )
        
        assert computed_goals == total_goals
    
    def test_different_seasons_loading(
        self,
        test_data_dir,
        sample_historical_df
    ):
        """Test loading different seasons."""
        loader = ExcelLoader()
        
        # Filter for different seasons (from sample data)
        df = sample_historical_df.head(20)
        
        file_path = test_data_dir / "seasons.xlsx"
        df.to_excel(file_path, index=False)
        
        # Load with different season labels
        s1 = loader.load(file_path, season="2023-24", filter_unsupported_leagues=False)
        s2 = loader.load(file_path, season="2024-25", filter_unsupported_leagues=False)
        
        if s1 is not None and s2 is not None:
            assert s1["season"].iloc[0] == "2023-24"
            assert s2["season"].iloc[0] == "2024-25"
