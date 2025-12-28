"""
Integration tests for the complete data pipeline.

Tests cover:
- Complete flow: Excel → Processing → JSON → Loading
- Data integrity through pipeline
- Multi-file processing
- Error recovery
"""
import pytest
import pandas as pd
from pathlib import Path

import sys
sys.path.insert(0, str(Path(__file__).parent.parent.parent))

from src.data.loaders import ExcelLoader, DataProcessor
from src.data.storage import JSONStorage
from src.domain.entities import Match


class TestDataPipeline:
    """Tests for complete data pipeline."""
    
    def test_full_pipeline_excel_to_json(
        self, 
        sample_excel_file, 
        test_data_dir,
        json_storage
    ):
        """Test complete flow from Excel to JSON storage."""
        # Load Excel
        loader = ExcelLoader()
        df = loader.load(sample_excel_file, filter_unsupported_leagues=False)
        
        assert df is not None
        
        # Process data
        processor = DataProcessor()
        processed = processor.process_historical_data(df)
        
        assert len(processed) > 0
        
        # Convert to entities
        matches = processor.convert_to_matches(processed)
        
        assert len(matches) > 0
        
        # Save to JSON
        output_path = test_data_dir / "pipeline_output.json"
        match_dicts = [m.to_dict() for m in matches]
        
        result = json_storage.save(match_dicts, output_path)
        
        assert result is True
        assert output_path.exists()
        
        # Load back and verify
        loaded = json_storage.load(output_path)
        
        assert len(loaded) == len(matches)
    
    def test_pipeline_preserves_data_integrity(
        self,
        sample_excel_file,
        test_data_dir,
        json_storage
    ):
        """Test data integrity is preserved through pipeline."""
        # Original load
        loader = ExcelLoader()
        original_df = loader.load(sample_excel_file, filter_unsupported_leagues=False)
        original_count = len(original_df)
        
        # Process and convert
        processor = DataProcessor()
        processed = processor.process_historical_data(original_df)
        matches = processor.convert_to_matches(processed)
        
        # Save and reload
        output_path = test_data_dir / "integrity_test.json"
        match_dicts = [m.to_dict() for m in matches]
        json_storage.save(match_dicts, output_path)
        
        reloaded = json_storage.load(output_path)
        
        # Verify no data loss
        assert len(reloaded) <= original_count
        
        # Verify data can be converted back to Match
        for data in reloaded:
            match = Match.from_dict(data)
            assert match.home_team is not None
            assert match.away_team is not None
    
    def test_pipeline_handles_multiple_files(
        self,
        test_data_dir,
        sample_historical_df,
        json_storage
    ):
        """Test pipeline with multiple input files."""
        # Create multiple Excel files
        files = []
        for league in ["E0", "E1", "D1"]:
            file_path = test_data_dir / f"{league}_test.xlsx"
            df = sample_historical_df[sample_historical_df["Div"] == league]
            if len(df) > 0:
                df.to_excel(file_path, index=False)
                files.append(file_path)
        
        # Load all files
        loader = ExcelLoader()
        all_matches = []
        
        for file_path in files:
            df = loader.load(file_path, filter_unsupported_leagues=False)
            if df is not None and len(df) > 0:
                processor = DataProcessor()
                processed = processor.process_historical_data(df)
                matches = processor.convert_to_matches(processed)
                all_matches.extend(matches)
        
        # Save combined
        output_path = test_data_dir / "combined.json"
        match_dicts = [m.to_dict() for m in all_matches]
        json_storage.save(match_dicts, output_path)
        
        # Verify
        loaded = json_storage.load(output_path)
        assert len(loaded) == len(all_matches)
    
    def test_pipeline_error_recovery(
        self,
        test_data_dir,
        sample_excel_file,
        corrupt_excel_file,
        json_storage
    ):
        """Test pipeline continues after encountering bad file."""
        loader = ExcelLoader()
        processor = DataProcessor()
        all_matches = []
        
        # Process mix of good and bad files
        files = [corrupt_excel_file, sample_excel_file]
        
        for file_path in files:
            df = loader.load(file_path, filter_unsupported_leagues=False)
            if df is not None and len(df) > 0:
                processed = processor.process_historical_data(df)
                matches = processor.convert_to_matches(processed)
                all_matches.extend(matches)
        
        # Should have matches from good file
        assert len(all_matches) > 0


class TestPipelinePerformance:
    """Tests for pipeline performance."""
    
    def test_pipeline_large_dataset(self, test_data_dir, json_storage):
        """Test pipeline with larger dataset."""
        import time
        
        # Create larger dataset
        data = []
        for i in range(500):
            data.append({
                "Div": "E0",
                "Date": f"2024-{(i % 12) + 1:02d}-{(i % 28) + 1:02d}",
                "Time": "15:00",
                "HomeTeam": f"Team{i % 20}",
                "AwayTeam": f"Team{(i + 10) % 20}",
                "FTHG": i % 4,
                "FTAG": i % 3,
                "FTR": "H" if i % 4 > i % 3 else ("A" if i % 4 < i % 3 else "D"),
            })
        
        df = pd.DataFrame(data)
        file_path = test_data_dir / "large.xlsx"
        df.to_excel(file_path, index=False)
        
        # Time the pipeline
        start_time = time.time()
        
        loader = ExcelLoader()
        raw_df = loader.load(file_path, filter_unsupported_leagues=False)
        
        processor = DataProcessor()
        processed = processor.process_historical_data(raw_df)
        matches = processor.convert_to_matches(processed)
        
        output_path = test_data_dir / "large_output.json"
        match_dicts = [m.to_dict() for m in matches]
        json_storage.save(match_dicts, output_path)
        
        elapsed = time.time() - start_time
        
        # Should complete in reasonable time
        assert elapsed < 30  # 30 seconds max
        assert len(matches) > 400  # Most should convert
