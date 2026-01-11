"""
Backtest the /api/v1/analyze endpoint against historical results.
Uses 150+ matches to verify the full analysis pipeline (ML + Poisson + MC + Gemini).
"""
import asyncio
import httpx
import pandas as pd
from pathlib import Path
from datetime import datetime, timedelta

API_BASE = "http://localhost:8000/api/v1"
DATA_DIR = Path(__file__).parent.parent / "data"

LEAGUE_IDS = ['E0', 'E1', 'D1', 'I1', 'SP1', 'F1']


async def backtest_analyze_endpoint(num_matches: int = 150):
    """Call /analyze endpoint for historical dates and check accuracy."""
    
    # Load historical results
    print("📂 Loading historical match results...")
    all_matches = []
    for sheet in LEAGUE_IDS:
        try:
            df = pd.read_excel(DATA_DIR / 'historical/all-euro-data-2025-2026.xlsx', sheet_name=sheet)
            df['Date'] = pd.to_datetime(df['Date'], errors='coerce')
            df['League'] = sheet
            df = df[df['FTR'].notna()]
            all_matches.append(df)
        except Exception as e:
            print(f"  ⚠️ Could not load {sheet}: {e}")
    
    historical = pd.concat(all_matches, ignore_index=True)
    historical = historical.sort_values('Date', ascending=False)
    print(f"✅ Loaded {len(historical)} historical matches")
    
    # Get unique dates from last 9 weeks
    cutoff = datetime.now() - timedelta(weeks=9)
    recent = historical[historical['Date'] >= cutoff]
    dates = recent['Date'].dt.strftime('%Y-%m-%d').unique()[:20]  # Max 20 dates
    print(f"📅 Testing {len(dates)} dates from recent 9 weeks")
    
    # Call API for each date
    async with httpx.AsyncClient(timeout=60.0) as client:
        all_predictions = []
        
        for date_str in dates:
            print(f"\n  → Calling /analyze for {date_str}...")
            try:
                response = await client.post(
                    f"{API_BASE}/matches/analyze",
                    json={"date": date_str},
                    params={"limit": 50}
                )
                
                if response.status_code == 200:
                    data = response.json()
                    items = data.get('items', [])
                    print(f"    Got {len(items)} matches")
                    
                    for match in items:
                        all_predictions.append({
                            'date': match.get('date'),
                            'home_team': match.get('home_team'),
                            'away_team': match.get('away_team'),
                            'league_id': match.get('league_id'),
                            'consensus_hdw': match.get('pattern_analysis', {}).get('hdw_consensus', ''),
                            'consensus_btts': match.get('pattern_analysis', {}).get('btts_consensus', ''),
                            'consensus_o25': match.get('pattern_analysis', {}).get('over25_consensus', ''),
                            'is_trap': match.get('trap_detector', {}).get('is_trap', False),
                            'trap_flags': match.get('trap_detector', {}).get('flags', []),
                            'fthg': match.get('fthg', 0), # Ensure analyze endpoint returns this for testing context if possible, or we look it up later
                            'ftag': match.get('ftag', 0)
                        })
                else:
                    print(f"    ❌ Error: {response.status_code}")
            except Exception as e:
                print(f"    ❌ Exception: {e}")
            
            if len(all_predictions) >= num_matches:
                break
    
    print(f"\n✅ Got {len(all_predictions)} predictions from API")
    
    # Match predictions to actual results
    stats = {
        'total': 0,
        'hdw': {'correct': 0, 'total': 0},
        'btts': {'correct': 0, 'total': 0},
        'over25': {'correct': 0, 'total': 0},
        'traps': {'detected': 0, 'avoided_loss': 0, 'false_positive': 0}
    }
    
    for pred in all_predictions:
        # Find actual result from loaded history
        match = historical[
            (historical['HomeTeam'] == pred['home_team']) &
            (historical['AwayTeam'] == pred['away_team'])
        ]
        
        if match.empty:
            continue
        
        row = match.iloc[0]
        actual_ftr = row['FTR']
        actual_fthg = row['FTHG']
        actual_ftag = row['FTAG']
        
        stats['total'] += 1
        
        # 1. HDW Accuracy
        if pred['consensus_hdw']:
            stats['hdw']['total'] += 1
            if pred['consensus_hdw'] == actual_ftr:
                stats['hdw']['correct'] += 1
                
        # 2. BTTS Accuracy
        if pred['consensus_btts'] in ['Yes', 'No']:
            stats['btts']['total'] += 1
            actual_btts = 'Yes' if (actual_fthg > 0 and actual_ftag > 0) else 'No'
            if pred['consensus_btts'] == actual_btts:
                stats['btts']['correct'] += 1
                
        # 3. Over 2.5 Accuracy
        if pred['consensus_o25'] in ['Over', 'Under']:
            stats['over25']['total'] += 1
            actual_o25 = 'Over' if (actual_fthg + actual_ftag) > 2.5 else 'Under'
            if pred['consensus_o25'] == actual_o25:
                stats['over25']['correct'] += 1
        
        # 4. Trap Detector
        if pred['is_trap']:
            stats['traps']['detected'] += 1
            # If trap flagged and our prediction (HDW) was WRONG, it was a "good" trap detection
            # If trap flagged but prediction was RIGHT, it was a "false positive"
            if pred['consensus_hdw'] and pred['consensus_hdw'] != actual_ftr:
                stats['traps']['avoided_loss'] += 1
            elif pred['consensus_hdw'] == actual_ftr:
                stats['traps']['false_positive'] += 1
    
    # Results
    print("\n" + "=" * 60)
    print("📊 ANALYSIS ENDPOINT BACKTEST RESULTS")
    print("=" * 60)
    print(f"\nMatches tested: {stats['total']}")
    
    def print_acc(name, d):
        acc = (d['correct'] / d['total'] * 100) if d['total'] > 0 else 0
        print(f"{name:<15} {d['correct']}/{d['total']:<5} = {acc:.1f}%")

    print("\n✅ Prediction Markets:")
    print_acc("Winner (HDW):", stats['hdw'])
    print_acc("BTTS:", stats['btts'])
    print_acc("Over 2.5:", stats['over25'])
    
    print("\n🕵️ Trap Detector:")
    print(f"Traps Detected: {stats['traps']['detected']}")
    print(f"Losses Avoided: {stats['traps']['avoided_loss']}")
    print(f"False Positives: {stats['traps']['false_positive']}")
    
    print("=" * 60)


if __name__ == "__main__":
    asyncio.run(backtest_analyze_endpoint(150))
