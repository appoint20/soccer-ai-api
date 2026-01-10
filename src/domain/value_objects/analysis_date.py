from dataclasses import dataclass
from datetime import date, datetime
import re

@dataclass(frozen=True)
class AnalysisDate:
    """Value object representing a date for analysis."""
    value: date
    
    @classmethod
    def from_string(cls, date_str: str) -> 'AnalysisDate':
        """Parse date string (YYYY-MM-DD)."""
        try:
            return cls(datetime.strptime(date_str, "%Y-%m-%d").date())
        except ValueError as e:
            raise ValueError(f"Invalid date format '{date_str}': {e}")
    
    @classmethod
    def from_filename(cls, filename: str) -> 'AnalysisDate':
        """Extract date from filename (fixtures_2026-01-01.csv)."""
        # Match only the date part YYYY-MM-DD
        match = re.search(r"(\d{4}-\d{2}-\d{2})", filename)
        if match:
            date_str = match.group(1)
            return cls.from_string(date_str)
        raise ValueError(f"Could not extract date from filename: {filename}")
    
    def to_string(self) -> str:
        """Convert to YYYY-MM-DD string."""
        return self.value.isoformat()
    
    def is_before(self, other: 'AnalysisDate') -> bool:
        """Check if this date is before another."""
        return self.value < other.value
    
    def __str__(self) -> str:
        return self.to_string()
