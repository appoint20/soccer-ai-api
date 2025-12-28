"""
Unit tests for JSON storage operations.

Tests cover:
- Save and load operations
- Append functionality
- Backup creation
- Error handling
- Edge cases
"""
import json
import pytest
from pathlib import Path

import sys
sys.path.insert(0, str(Path(__file__).parent.parent.parent))

from src.data.storage import JSONStorage


class TestJSONStorageSave:
    """Tests for JSONStorage save operations."""
    
    def test_save_creates_file(self, test_data_dir, json_storage):
        """Test save() creates file with correct data."""
        file_path = test_data_dir / "test.json"
        data = {"name": "test", "value": 123}
        
        result = json_storage.save(data, file_path)
        
        assert result is True
        assert file_path.exists()
    
    def test_save_dict_data(self, test_data_dir, json_storage):
        """Test saving dictionary data."""
        file_path = test_data_dir / "dict.json"
        data = {"key1": "value1", "key2": [1, 2, 3]}
        
        json_storage.save(data, file_path)
        
        with open(file_path) as f:
            loaded = json.load(f)
        
        assert loaded == data
    
    def test_save_list_data(self, test_data_dir, json_storage):
        """Test saving list data."""
        file_path = test_data_dir / "list.json"
        data = [1, 2, 3, {"nested": True}]
        
        json_storage.save(data, file_path)
        
        with open(file_path) as f:
            loaded = json.load(f)
        
        assert loaded == data
    
    def test_save_creates_directories(self, test_data_dir, json_storage):
        """Test save creates parent directories."""
        file_path = test_data_dir / "nested" / "deep" / "test.json"
        data = {"test": True}
        
        result = json_storage.save(data, file_path)
        
        assert result is True
        assert file_path.exists()
    
    def test_save_with_indentation(self, test_data_dir, json_storage):
        """Test saving with proper JSON formatting."""
        file_path = test_data_dir / "formatted.json"
        data = {"key": "value"}
        
        json_storage.save(data, file_path, indent=4)
        
        with open(file_path) as f:
            content = f.read()
        
        # Should have newlines from indentation
        assert "\n" in content
    
    def test_save_unicode(self, test_data_dir, json_storage):
        """Test saving Unicode characters."""
        file_path = test_data_dir / "unicode.json"
        data = {"team": "FC Köln", "city": "München"}
        
        json_storage.save(data, file_path, ensure_ascii=False)
        
        with open(file_path, encoding="utf-8") as f:
            loaded = json.load(f)
        
        assert loaded["team"] == "FC Köln"


class TestJSONStorageLoad:
    """Tests for JSONStorage load operations."""
    
    def test_load_reads_correctly(self, sample_json_file, json_storage):
        """Test load() reads file correctly."""
        data = json_storage.load(sample_json_file)
        
        assert data is not None
        assert isinstance(data, list)
        assert data[0]["home_team"] == "Arsenal"
    
    def test_load_returns_default_for_missing(self, test_data_dir, json_storage):
        """Test loading non-existent file returns default."""
        result = json_storage.load(test_data_dir / "missing.json", default=[])
        
        assert result == []
    
    def test_load_returns_none_for_corrupt(self, corrupt_json_file, json_storage):
        """Test loading corrupt JSON returns default."""
        result = json_storage.load(corrupt_json_file, default=None)
        
        assert result is None
    
    def test_load_complex_structure(self, test_data_dir, json_storage):
        """Test loading complex nested structures."""
        file_path = test_data_dir / "complex.json"
        data = {
            "level1": {
                "level2": {
                    "level3": [1, 2, 3]
                }
            }
        }
        
        with open(file_path, "w") as f:
            json.dump(data, f)
        
        loaded = json_storage.load(file_path)
        
        assert loaded["level1"]["level2"]["level3"] == [1, 2, 3]


class TestJSONStorageAppend:
    """Tests for JSONStorage append operations."""
    
    def test_append_to_list(self, test_data_dir, json_storage):
        """Test appending to a JSON list."""
        file_path = test_data_dir / "append_list.json"
        
        json_storage.save([1, 2, 3], file_path)
        json_storage.append([4, 5], file_path)
        
        loaded = json_storage.load(file_path)
        assert loaded == [1, 2, 3, 4, 5]
    
    def test_append_single_item_to_list(self, test_data_dir, json_storage):
        """Test appending single item to list."""
        file_path = test_data_dir / "append_single.json"
        
        json_storage.save([1, 2], file_path)
        json_storage.append(3, file_path)
        
        loaded = json_storage.load(file_path)
        assert loaded == [1, 2, 3]
    
    def test_append_to_dict(self, test_data_dir, json_storage):
        """Test appending/merging to a JSON dict."""
        file_path = test_data_dir / "append_dict.json"
        
        json_storage.save({"a": 1}, file_path)
        json_storage.append({"b": 2}, file_path)
        
        loaded = json_storage.load(file_path)
        assert loaded == {"a": 1, "b": 2}
    
    def test_append_creates_new_file(self, test_data_dir, json_storage):
        """Test append creates file if doesn't exist."""
        file_path = test_data_dir / "new_append.json"
        
        result = json_storage.append([1, 2], file_path)
        
        assert result is True
        assert file_path.exists()


class TestJSONStorageExists:
    """Tests for JSONStorage exists operation."""
    
    def test_exists_true(self, sample_json_file, json_storage):
        """Test exists returns True for existing file."""
        assert json_storage.exists(sample_json_file) is True
    
    def test_exists_false(self, test_data_dir, json_storage):
        """Test exists returns False for missing file."""
        assert json_storage.exists(test_data_dir / "missing.json") is False


class TestJSONStorageBackup:
    """Tests for JSONStorage backup operations."""
    
    def test_create_backup(self, sample_json_file, json_storage):
        """Test backup creation."""
        backup_path = json_storage.create_backup(sample_json_file)
        
        assert backup_path is not None
        assert backup_path.exists()
        assert "matches_" in backup_path.name
    
    def test_backup_content_matches(self, sample_json_file, json_storage):
        """Test backup contains same data."""
        backup_path = json_storage.create_backup(sample_json_file)
        
        original = json_storage.load(sample_json_file)
        backup = json_storage.load(backup_path)
        
        assert original == backup
    
    def test_backup_nonexistent_file(self, test_data_dir, json_storage):
        """Test backup of non-existent file returns None."""
        result = json_storage.create_backup(test_data_dir / "missing.json")
        
        assert result is None
    
    def test_backup_to_custom_dir(self, sample_json_file, test_data_dir, json_storage):
        """Test backup to custom directory."""
        backup_dir = test_data_dir / "backups"
        backup_path = json_storage.create_backup(sample_json_file, backup_dir)
        
        assert backup_path is not None
        assert backup_path.parent == backup_dir


class TestJSONStorageDelete:
    """Tests for JSONStorage delete operations."""
    
    def test_delete_existing_file(self, test_data_dir, json_storage):
        """Test deleting an existing file."""
        file_path = test_data_dir / "to_delete.json"
        json_storage.save({"test": True}, file_path)
        
        result = json_storage.delete(file_path)
        
        assert result is True
        assert not file_path.exists()
    
    def test_delete_nonexistent_file(self, test_data_dir, json_storage):
        """Test deleting non-existent file returns True."""
        result = json_storage.delete(test_data_dir / "missing.json")
        
        assert result is True


class TestJSONStorageUtilities:
    """Tests for JSONStorage utility methods."""
    
    def test_get_size(self, sample_json_file, json_storage):
        """Test getting file size."""
        size = json_storage.get_size(sample_json_file)
        
        assert size > 0
    
    def test_get_size_missing_file(self, test_data_dir, json_storage):
        """Test getting size of missing file returns 0."""
        size = json_storage.get_size(test_data_dir / "missing.json")
        
        assert size == 0
    
    def test_list_json_files(self, test_data_dir, json_storage):
        """Test listing JSON files in directory."""
        # Create some JSON files
        json_storage.save({}, test_data_dir / "file1.json")
        json_storage.save({}, test_data_dir / "file2.json")
        json_storage.save({}, test_data_dir / "file3.json")
        
        files = json_storage.list_json_files(test_data_dir)
        
        assert len(files) >= 3
        assert all(f.suffix == ".json" for f in files)
    
    def test_list_json_files_empty_dir(self, test_data_dir, json_storage):
        """Test listing JSON files in empty directory."""
        empty_dir = test_data_dir / "empty_subdir"
        empty_dir.mkdir()
        
        files = json_storage.list_json_files(empty_dir)
        
        assert files == []


class TestJSONStorageEdgeCases:
    """Tests for JSONStorage edge cases."""
    
    def test_save_empty_dict(self, test_data_dir, json_storage):
        """Test saving empty dictionary."""
        file_path = test_data_dir / "empty_dict.json"
        result = json_storage.save({}, file_path)
        
        assert result is True
        assert json_storage.load(file_path) == {}
    
    def test_save_empty_list(self, test_data_dir, json_storage):
        """Test saving empty list."""
        file_path = test_data_dir / "empty_list.json"
        result = json_storage.save([], file_path)
        
        assert result is True
        assert json_storage.load(file_path) == []
    
    def test_large_data(self, test_data_dir, json_storage):
        """Test handling large JSON data."""
        file_path = test_data_dir / "large.json"
        
        # Create large dataset
        data = [{"id": i, "data": "x" * 100} for i in range(1000)]
        
        result = json_storage.save(data, file_path)
        loaded = json_storage.load(file_path)
        
        assert result is True
        assert len(loaded) == 1000
