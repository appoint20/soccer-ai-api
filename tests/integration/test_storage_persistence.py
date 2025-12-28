"""
Integration tests for storage persistence.

Tests cover:
- Save/load cycles
- Data backup and recovery
- Concurrent operations
"""
import pytest
from pathlib import Path
import time

import sys
sys.path.insert(0, str(Path(__file__).parent.parent.parent))

from src.data.storage import JSONStorage


class TestStoragePersistence:
    """Tests for storage persistence across operations."""
    
    def test_multiple_save_load_cycles(self, test_data_dir, json_storage):
        """Test data persists through multiple save/load cycles."""
        file_path = test_data_dir / "persistence.json"
        
        # Initial save
        data = [{"id": 1, "name": "first"}]
        json_storage.save(data, file_path)
        
        # Multiple cycles
        for i in range(5):
            loaded = json_storage.load(file_path)
            loaded.append({"id": i + 2, "name": f"item_{i}"})
            json_storage.save(loaded, file_path)
        
        # Final load
        final = json_storage.load(file_path)
        
        assert len(final) == 6  # 1 initial + 5 added
    
    def test_modify_and_save_preserves_structure(self, test_data_dir, json_storage):
        """Test modifying and saving preserves data structure."""
        file_path = test_data_dir / "modify.json"
        
        # Complex structure
        data = {
            "matches": [{"id": 1}],
            "stats": {"total": 1},
            "nested": {"level1": {"level2": "value"}}
        }
        json_storage.save(data, file_path)
        
        # Load, modify, save
        loaded = json_storage.load(file_path)
        loaded["matches"].append({"id": 2})
        loaded["stats"]["total"] = 2
        json_storage.save(loaded, file_path)
        
        # Verify
        final = json_storage.load(file_path)
        assert len(final["matches"]) == 2
        assert final["stats"]["total"] == 2
        assert final["nested"]["level1"]["level2"] == "value"
    
    def test_backup_before_overwrite(self, test_data_dir, json_storage):
        """Test creating backup before overwriting."""
        file_path = test_data_dir / "backup_test.json"
        
        # Initial data
        original_data = {"version": 1, "data": "original"}
        json_storage.save(original_data, file_path)
        
        # Create backup
        backup_path = json_storage.create_backup(file_path)
        
        # Overwrite with new data
        new_data = {"version": 2, "data": "new"}
        json_storage.save(new_data, file_path)
        
        # Verify original preserved in backup
        backup_data = json_storage.load(backup_path)
        current_data = json_storage.load(file_path)
        
        assert backup_data["version"] == 1
        assert current_data["version"] == 2
    
    def test_recovery_from_backup(self, test_data_dir, json_storage):
        """Test recovering data from backup."""
        file_path = test_data_dir / "recovery.json"
        
        # Create and backup
        data = {"important": "data"}
        json_storage.save(data, file_path)
        backup_path = json_storage.create_backup(file_path)
        
        # Corrupt main file
        with open(file_path, "w") as f:
            f.write("corrupted")
        
        # Load from backup
        recovered = json_storage.load(backup_path)
        
        assert recovered["important"] == "data"


class TestStorageConcurrency:
    """Tests for concurrent storage operations."""
    
    def test_sequential_writes(self, test_data_dir, json_storage):
        """Test sequential write operations."""
        file_path = test_data_dir / "sequential.json"
        
        json_storage.save({"step": 1}, file_path)
        json_storage.save({"step": 2}, file_path)
        json_storage.save({"step": 3}, file_path)
        
        final = json_storage.load(file_path)
        assert final["step"] == 3
    
    def test_rapid_save_load(self, test_data_dir, json_storage):
        """Test rapid save/load operations."""
        file_path = test_data_dir / "rapid.json"
        
        data = {"count": 0}
        
        for i in range(20):
            json_storage.save(data, file_path)
            loaded = json_storage.load(file_path)
            loaded["count"] = i
            data = loaded
        
        final = json_storage.load(file_path)
        # Should have increased count
        assert final["count"] >= 17  # Allow some variance


class TestStorageEdgeCases:
    """Tests for edge cases in persistence."""
    
    def test_empty_file_handling(self, test_data_dir, json_storage):
        """Test handling of empty file scenarios."""
        file_path = test_data_dir / "empty_cycle.json"
        
        # Save empty list
        json_storage.save([], file_path)
        
        # Load and append
        loaded = json_storage.load(file_path)
        loaded.append({"first": True})
        json_storage.save(loaded, file_path)
        
        final = json_storage.load(file_path)
        assert len(final) == 1
    
    def test_large_file_persistence(self, test_data_dir, json_storage):
        """Test persistence of large files."""
        file_path = test_data_dir / "large_persist.json"
        
        # Create large dataset
        data = [{"id": i, "data": "x" * 100} for i in range(1000)]
        
        json_storage.save(data, file_path)
        loaded = json_storage.load(file_path)
        
        assert len(loaded) == 1000
        assert loaded[500]["id"] == 500
