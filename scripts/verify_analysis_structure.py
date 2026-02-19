import urllib.request
import json
import sys

# Configuration
API_URL = "http://localhost:5078/api/Analysis"
DATE = "2026-02-08"

def fetch_analysis():
    print(f"\n{'='*60}")
    print(f"FETCHING ANALYSIS FOR: {DATE}")
    print(f"{'='*60}")
    
    try:
        url = f"{API_URL}/{DATE}?language=en"
        print(f"Requesting: {url}")
        
        with urllib.request.urlopen(url) as response:
            if response.status != 200:
                print(f"Error {response.status}")
                return
            
            data = json.loads(response.read().decode('utf-8'))
            matches = data.get('matches', [])
            
            if not matches:
                print("No matches found for this date.")
                return

            print(f"Found {len(matches)} matches. Inspecting first match...")
            
            first_match = matches[0]
            
            # recursive print keys
            print("\n[Match Structure]")
            print(json.dumps(first_match, indent=2, default=str))
            
            # Validations
            context = first_match.get('match_context', {})
            result = context.get('result')
            stats = first_match.get('team_stats', {})
            # Check Prediction Object (New Structure)
            if 'prediction' in first_match:
                print(f"✅ Prediction object present")
                pred = first_match['prediction']
                
                # Check Over25 Sub-Object
                if 'over25' in pred and isinstance(pred['over25'], dict):
                    o25 = pred['over25']
                    required_sub = ['prediction', 'is_qualified', 'reason', 'probability']
                    missing_sub = [k for k in required_sub if k not in o25]
                    if not missing_sub:
                        print(f"✅ Over25 prediction merged correctly: {o25}")
                        if isinstance(pred.get('two_to_three_goals'), dict): 
                            print(f"✅ TwoToThreeGoals is object")
                        else: print(f"❌ TwoToThreeGoals is NOT object")
                        
                        # Check Low Scoring
                        if 'low_scoring' in pred and isinstance(pred['low_scoring'], dict):
                            ls = pred['low_scoring']
                            print(f"✅ Low Scoring prediction present: {ls}")
                            if isinstance(ls.get('prediction'), bool): print(f"   - Prediction is bool: {ls['prediction']}")
                            else: print(f"   ❌ Prediction is NOT bool!")
                        else:
                            print(f"❌ Low Scoring prediction MISSING!")

                        if isinstance(pred.get('match_winner'), dict): print(f"✅ Match Winner is object")
                        else: print(f"❌ Match Winner is NOT object")
                    else:
                         print(f"❌ Over25 prediction MISSING structure: {missing_sub}")
                else:
                    print(f"❌ 'over25' key missing or not object in prediction")

                # Check Match Winner
                if 'match_winner' in pred and isinstance(pred['match_winner'], dict):
                    mw = pred['match_winner']
                    required_sub = ['prediction', 'is_qualified', 'reason', 'confidence']
                    missing_sub = [k for k in required_sub if k not in mw]
                    if not missing_sub:
                         print(f"✅ Match Winner merged correctly: {mw}")
                    else:
                         print(f"❌ Match Winner MISSING structure: {missing_sub}")
            else:
                print(f"❌ Prediction object MISSING!")

            # Check Removal of Deprecated fields (models, qualification)
            deprecated = ['models', 'qualification', 'ml_predictions', 'final_predictions', 'decisions']
            # decisions also removed (merged)
            
            found_deprecated = [k for k in deprecated if k in first_match]
            if not found_deprecated:
                print(f"✅ Deprecated fields (models, qualification, decisions) correctly REMOVED")
            else:
                print(f"❌ Deprecated fields STILL PRESENT: {found_deprecated}")

            # Check Trap
            if 'trap' in first_match:
                print(f"✅ 'trap' present at root")
                print(f"   Trap: {first_match['trap']}")
            else:
                 print(f"❌ 'trap' MISSING at root!")

    except Exception as e:
        print(f"Error: {e}")

if __name__ == "__main__":
    fetch_analysis()
