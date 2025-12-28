"""JSON file storage operations."""
import json
import shutil
from datetime import datetime
from pathlib import Path
from typing import Any, Optional, Union

from src.utils.logger import get_logger


class JSONStorage:
    """
    Handles JSON file operations with error handling and backup support.
    
    Provides methods to save, load, append, and manage JSON files
    with proper error handling and backup functionality.
    """
    
    def __init__(self):
        """Initialize JSON storage with logger."""
        self.logger = get_logger("JSONStorage")
    
    def save(
        self,
        data: Union[dict, list],
        filepath: Union[str, Path],
        indent: int = 2,
        ensure_ascii: bool = False,
    ) -> bool:
        """
        Save data to a JSON file.
        
        Args:
            data: Dictionary or list to save
            filepath: Path to save the file
            indent: JSON indentation level
            ensure_ascii: Whether to escape non-ASCII characters
            
        Returns:
            True if successful, False otherwise
        """
        filepath = Path(filepath)
        
        try:
            # Create parent directories if needed
            filepath.parent.mkdir(parents=True, exist_ok=True)
            
            # Write to a temp file first, then rename for atomicity
            temp_path = filepath.with_suffix('.tmp')
            
            with open(temp_path, 'w', encoding='utf-8') as f:
                json.dump(data, f, indent=indent, ensure_ascii=ensure_ascii)
            
            # Rename temp file to target
            temp_path.replace(filepath)
            
            self.logger.debug(f"Saved JSON to {filepath}")
            return True
            
        except (IOError, json.JSONDecodeError, TypeError) as e:
            self.logger.error(f"Failed to save JSON to {filepath}: {e}")
            # Clean up temp file if it exists
            temp_path = filepath.with_suffix('.tmp')
            if temp_path.exists():
                temp_path.unlink()
            return False
    
    def load(
        self,
        filepath: Union[str, Path],
        default: Optional[Any] = None,
    ) -> Optional[Union[dict, list]]:
        """
        Load data from a JSON file.
        
        Args:
            filepath: Path to the JSON file
            default: Default value if file doesn't exist or is invalid
            
        Returns:
            Loaded data or default value
        """
        filepath = Path(filepath)
        
        if not filepath.exists():
            self.logger.debug(f"File not found: {filepath}")
            return default
        
        try:
            with open(filepath, 'r', encoding='utf-8') as f:
                data = json.load(f)
            
            self.logger.debug(f"Loaded JSON from {filepath}")
            return data
            
        except (IOError, json.JSONDecodeError) as e:
            self.logger.error(f"Failed to load JSON from {filepath}: {e}")
            return default
    
    def append(
        self,
        data: Union[dict, list],
        filepath: Union[str, Path],
    ) -> bool:
        """
        Append data to an existing JSON file.
        
        If the file contains a list, appends to the list.
        If the file contains a dict, merges the dictionaries.
        
        Args:
            data: Data to append
            filepath: Path to the JSON file
            
        Returns:
            True if successful, False otherwise
        """
        filepath = Path(filepath)
        
        # Load existing data
        existing = self.load(filepath)
        
        if existing is None:
            # File doesn't exist, create new
            return self.save(data, filepath)
        
        try:
            if isinstance(existing, list):
                if isinstance(data, list):
                    existing.extend(data)
                else:
                    existing.append(data)
            elif isinstance(existing, dict):
                if isinstance(data, dict):
                    existing.update(data)
                else:
                    self.logger.error("Cannot append non-dict to dict JSON file")
                    return False
            else:
                self.logger.error(f"Unexpected JSON type: {type(existing)}")
                return False
            
            return self.save(existing, filepath)
            
        except Exception as e:
            self.logger.error(f"Failed to append to {filepath}: {e}")
            return False
    
    def exists(self, filepath: Union[str, Path]) -> bool:
        """
        Check if a JSON file exists.
        
        Args:
            filepath: Path to check
            
        Returns:
            True if file exists
        """
        return Path(filepath).exists()
    
    def create_backup(
        self,
        filepath: Union[str, Path],
        backup_dir: Optional[Union[str, Path]] = None,
    ) -> Optional[Path]:
        """
        Create a timestamped backup of a JSON file.
        
        Args:
            filepath: Path to the file to backup
            backup_dir: Directory for backup (default: same as file)
            
        Returns:
            Path to backup file or None if failed
        """
        filepath = Path(filepath)
        
        if not filepath.exists():
            self.logger.warning(f"Cannot backup non-existent file: {filepath}")
            return None
        
        try:
            # Generate backup filename with timestamp
            timestamp = datetime.now().strftime("%Y%m%d_%H%M%S")
            backup_name = f"{filepath.stem}_{timestamp}{filepath.suffix}"
            
            if backup_dir:
                backup_path = Path(backup_dir) / backup_name
                backup_path.parent.mkdir(parents=True, exist_ok=True)
            else:
                backup_path = filepath.parent / backup_name
            
            # Copy file
            shutil.copy2(filepath, backup_path)
            
            self.logger.info(f"Created backup: {backup_path}")
            return backup_path
            
        except IOError as e:
            self.logger.error(f"Failed to create backup of {filepath}: {e}")
            return None
    
    def delete(self, filepath: Union[str, Path]) -> bool:
        """
        Delete a JSON file.
        
        Args:
            filepath: Path to delete
            
        Returns:
            True if successful or file didn't exist
        """
        filepath = Path(filepath)
        
        try:
            if filepath.exists():
                filepath.unlink()
                self.logger.info(f"Deleted: {filepath}")
            return True
        except IOError as e:
            self.logger.error(f"Failed to delete {filepath}: {e}")
            return False
    
    def get_size(self, filepath: Union[str, Path]) -> int:
        """
        Get the size of a JSON file in bytes.
        
        Args:
            filepath: Path to the file
            
        Returns:
            File size in bytes, or 0 if not found
        """
        filepath = Path(filepath)
        
        if filepath.exists():
            return filepath.stat().st_size
        return 0
    
    def list_json_files(
        self,
        directory: Union[str, Path],
        pattern: str = "*.json",
    ) -> list[Path]:
        """
        List all JSON files in a directory.
        
        Args:
            directory: Directory to search
            pattern: Glob pattern for files
            
        Returns:
            List of file paths
        """
        directory = Path(directory)
        
        if not directory.exists():
            return []
        
        return sorted(directory.glob(pattern))
