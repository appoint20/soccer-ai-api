
import sqlite3
from datetime import datetime, timedelta
import collections

DB_PATH = "soccer.db"

# League Mapping (Reuse from previous scripts if needed, or just use ID)
LEAGUES = {
    39: "Premier League",
    40: "Championship",
    41: "League One",
    42: "League Two",
    140: "La Liga",
    141: "La Liga 2",
    78: "Bundesliga",
    79: "2. Bundesliga",
    135: "Serie A",
    136: "Serie B",
    61: "Ligue 1",
    62: "Ligue 2"
}

def analyze_odds():
    conn = sqlite3.connect(DB_PATH)
    cursor = conn.cursor()
    
    today = datetime.now()
    start_date = (today - timedelta(days=7)).strftime('%Y-%m-%d')
    end_date = (today + timedelta(days=5)).strftime('%Y-%m-%d')
    
    print(f"Auditing Odds from {start_date} to {end_date}")
    print("-" * 60)

    # Fetch fixtures
    query = """
    SELECT Id, LeagueId, Date, HomeTeamId, AwayTeamId, 
           HomeWinOdds, DrawOdds, AwayWinOdds, Over25Odds, BttsYesOdds
    FROM Fixtures
    WHERE Date >= ? AND Date <= ?
    ORDER BY Date
    """
    
    cursor.execute(query, (start_date, end_date))
    rows = cursor.fetchall()
    
    total_fixtures = len(rows)
    missing_odds_count = 0
    missing_by_league = collections.defaultdict(int)
    
    print(f"Total Fixtures Found: {total_fixtures}\n")
    
    if total_fixtures == 0:
        print("No fixtures found in this date range.")
        return

    print(f"{'Date':<12} | {'League':<20} | {'Status':<10} | {'Missing Markets'}")
    print("-" * 80)

    for row in rows:
        fid, lid, date_str, hid, aid, h_odds, d_odds, a_odds, o25_odds, btts_odds = row
        
        # Check for missing odds (Null or 0)
        missing = []
        if not h_odds: missing.append("1x2")
        if not o25_odds: missing.append("Over2.5")
        if not btts_odds: missing.append("BTTS")
        
        if missing:
            missing_odds_count += 1
            league_name = LEAGUES.get(lid, f"League {lid}")
            missing_by_league[league_name] += 1
            
            # Simplified Date
            d_short = date_str[:10]
            print(f"{d_short:<12} | {league_name:<20} | ❌ MISSING | {', '.join(missing)}")
    
    print("-" * 80)
    print(f"Fixtures with Missing Odds: {missing_odds_count} / {total_fixtures} ({(missing_odds_count/total_fixtures*100):.1f}%)")
    
    if missing_odds_count > 0:
        print("\nMissing Odds by League:")
        for league, count in missing_by_league.items():
            print(f"  {league}: {count}")
    else:
        print("\n✅ All fixtures have odds!")

    conn.close()

if __name__ == "__main__":
    analyze_odds()
