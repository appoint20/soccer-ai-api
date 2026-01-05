import json

with open("data/processed/matches.json", "r") as f:
    matches = json.load(f)

for m in matches:
    h = m.get("home_team", "")
    if "West Brom" in h:
        print(f"EXACT NAME: '{h}'")
        break
