
import urllib.request
import json
import ssl

API_URL = "http://localhost:5078/api/Analysis/2026-02-07?language=en"

def analyze_failed_bets():
    try:
        ctx = ssl.create_default_context()
        ctx.check_hostname = False
        ctx.verify_mode = ssl.CERT_NONE
        
        print(f"Fetching from {API_URL}...")
        with urllib.request.urlopen(API_URL, context=ctx) as response:
            if response.status != 200:
                print(f"Error {response.status}")
                return
            
            data = json.loads(response.read().decode('utf-8'))
            matches = data.get('matches', [])
            print(f"Found {len(matches)} matches")
            

            
            for m in matches:
                context = m.get('match_context', {})
                home = context.get('home_team', 'Unknown')
                away = context.get('away_team', 'Unknown')
                
                # print(f"Checking: {home} vs {away}")
                
                if "Coventry" in home or "Southampton" in home:
                    print(f"\n{'='*60}")
                    print(f"MATCH: {home} vs {away}")
                    print(f"{'='*60}")
                    
                    print(f"Date: {context.get('date')} | Score: {context.get('home_score')}-{context.get('away_score')}")
                    
                    snapshots = m.get('team_snapshots', {})
                    home_stats = snapshots.get('home_last7', {})
                    away_stats = snapshots.get('away_last7', {})
                    
                    print("\n--- FORM (Last 5 Matches) ---")
                    print(f"{home}: {home_stats.get('form')} (Last 5 Points: {home_stats.get('points')})")
                    print(f"{away}: {away_stats.get('form')} (Last 5 Points: {away_stats.get('points')})")
                    
                    print("\n--- GOALS (Avg Scored/Conceded) ---")
                    print(f"{home}: Scored {home_stats.get('goals_scored_avg'):.2f} | Conceded {home_stats.get('goals_conceded_avg'):.2f}")
                    print(f"{away}: Scored {away_stats.get('goals_scored_avg'):.2f} | Conceded {away_stats.get('goals_conceded_avg'):.2f}")
                    
                    print("\n--- BTTS % (Last 5) ---")
                    h_btts = home_stats.get('btts_rate', 0) * 100
                    a_btts = away_stats.get('btts_rate', 0) * 100
                    print(f"{home}: {h_btts:.1f}%")
                    print(f"{away}: {a_btts:.1f}%")
                    
                    print("\n--- H2H Summary ---")
                    h2h = m.get('head_to_head', {})
                    print(f"BTTS Rate in H2H: {h2h.get('btts_rate', 0)*100:.1f}%")
                    print(f"Avg Goals in H2H: {h2h.get('avg_total_goals', 0):.2f}")

                    print("\n--- MODEL CONFIDENCE ---")
                    final = m.get('final_predictions', {})
                    btts_pred = final.get('btts', {})
                    print(f"Final BTTS Confidence: {btts_pred.get('confidence', 0)*100:.1f}%")
                    
                    models = m.get('models', {})
                    poisson = models.get('poisson', {})
                    print(f"Poisson BTTS Prob: {poisson.get('btts', 0)*100:.1f}%")
                    
                    decisions = m.get('decisions', {})
                    btts_dec = decisions.get('btts', {})
                    print(f"Decision Warning: {btts_dec.get('warning_reason')}")
                    
    except Exception as e:
        print(f"Error: {e}")

if __name__ == "__main__":
    analyze_failed_bets()
