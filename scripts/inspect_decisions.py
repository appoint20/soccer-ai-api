import json

with open('analysis_decisions.json', 'r') as f:
    data = json.load(f)

for m in data['matches'][:5]:
    ctx = m['match_context']
    dec = m.get('decisions', {})
    h2h = m.get('head_to_head', {})
    
    print(f"\nMatch: {ctx['home_team']} vs {ctx['away_team']}")
    print(f"Odds: Over2.5={ctx.get('odds_over25')}, BTTS={ctx.get('odds_btts_yes')}, Home={ctx.get('odds_home_win')}")
    print(f"H2H Matches: {h2h.get('matches_analyzed')}, Over25Rate={h2h.get('over25_rate')}, BTTSRate={h2h.get('btts_rate')}")
    print("Decisions:")
    print(json.dumps(dec, indent=2))
