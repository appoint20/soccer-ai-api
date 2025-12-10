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
                    f"{API_BASE}/analyze/matches/analyze",
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
                            'ml_prediction': match.get('ml_predictions', {}).get('hdw', {}).get('prediction', ''),
                            'consensus': match.get('pattern_analysis', {}).get('pattern', ''),
                            'gemini_prediction': match.get('gemini_analysis', {}).get('prediction', '')
                        })
                else:
                    print(f"    ❌ Error: {response.status_code}")
            except Exception as e:
                print(f"    ❌ Exception: {e}")
            
            if len(all_predictions) >= num_matches:
                break
    
    print(f"\n✅ Got {len(all_predictions)} predictions from API")
    
    # Match predictions to actual results
    correct_ml = 0
    correct_gemini = 0
    total = 0
    
    for pred in all_predictions:
        # Find actual result
        match = historical[
            (historical['HomeTeam'] == pred['home_team']) &
            (historical['AwayTeam'] == pred['away_team'])
        ]
        
        if match.empty:
            continue
        
        actual = match.iloc[0]['FTR']
        total += 1
        
        if pred['ml_prediction'] == actual:
            correct_ml += 1
        if pred['gemini_prediction'] == actual:
            correct_gemini += 1
    
    # Results
    print("\n" + "=" * 60)
    print("📊 ANALYSIS ENDPOINT BACKTEST RESULTS")
    print("=" * 60)
    print(f"\nMatches tested: {total}")
    print(f"\nML Model Accuracy:    {correct_ml}/{total} = {correct_ml/max(total,1)*100:.1f}%")
    print(f"Gemini AI Accuracy:   {correct_gemini}/{total} = {correct_gemini/max(total,1)*100:.1f}%")
    print("=" * 60)


if __name__ == "__main__":
    asyncio.run(backtest_analyze_endpoint(150))
