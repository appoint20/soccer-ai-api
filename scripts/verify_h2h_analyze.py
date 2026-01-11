import sys
from pathlib import Path
import json

# Add project root to path
project_root = Path(__file__).parent.parent
sys.path.append(str(project_root))

from app.services.h2h_service import H2HService
from app.services.team_stats import TeamStatsService

def test_h2h():
    print("Testing H2H Service...")
    service = H2HService()
    
    # Test with known teams (Example: Liverpool vs Man City)
    stats = service.get_h2h_stats("Liverpool", "Man City")
    
    print(json.dumps(stats, indent=2, default=str))
    
    if stats['total_matches'] >= 0:
         print("✅ H2H Service returned valid structure")
    else:
         print("❌ H2H Service failed")

def test_team_stats():
    print("\nTesting Team Stats Service...")
    service = TeamStatsService()
    
    # Test with known team (Example: Arsenal)
    stats = service.get_team_stats("Arsenal", "E0")
    
    print(json.dumps(stats, indent=2))
    
    if stats.get('team_name'):
        print(f"✅ Found stats for {stats['team_name']}")
    else:
        print("⚠️ Team stats not found (might be missing JSON file)")

if __name__ == "__main__":
    test_h2h()
    test_team_stats()
