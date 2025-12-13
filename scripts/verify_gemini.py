
import os
import asyncio
import json
import httpx
from dotenv import load_dotenv

# Load environment variables
load_dotenv()

api_key = os.getenv("GEMINI_API_KEY")
print(f"API Key found: {'Yes' if api_key else 'No'} ({str(api_key)[:5]}...)")

# Mock match data based on what the API actually produces
matches = [
    {
        "home_team": "Manchester City",
        "away_team": "Liverpool",
        "league": "Premier League",
        "date": "2025-12-14",
        "odds": {"home": 2.1, "draw": 3.4, "away": 3.0},
        "ml_predictions": {
            "hdw": {
                "prediction": "H", 
                "confidence": 0.75,
                "probabilities": {"home": 0.60, "draw": 0.25, "away": 0.15}
            },
            "btts": {"prediction": "Yes", "confidence": 0.82},
            "over_25": {"prediction": "Over", "confidence": 0.78}
        },
        "poisson_analysis": {"hdw": "H", "home_goals": 2.1, "away_goals": 1.2},
        "monte_carlo_analysis": {"hdw": "H", "probabilities": {"home": 55, "draw": 25, "away": 20}},
        "pattern_analysis": {"pattern": "STRONG_CONSENSUS"},
        "trap_detector": {"is_trap": False},
        "h2h_analysis": {"total_matches": 5, "avg_goals": 3.2},
        "team_stats": {
            "home": {"form": ["W", "W", "D", "W", "L"], "goals_for": 2.4, "goals_against": 0.8},
            "away": {"form": ["W", "L", "W", "W", "D"], "goals_for": 2.1, "goals_against": 1.1}
        }
    },
    {
        "home_team": "Arsenal",
        "away_team": "Chelsea",
        "league": "Premier League",
        "date": "2025-12-14",
        "odds": {"home": 1.9, "draw": 3.5, "away": 3.8},
        "ml_predictions": {
            "hdw": {
                "prediction": "H", 
                "confidence": 0.65
            },
            "btts": {"prediction": "Yes", "confidence": 0.70},
            "over_25": {"prediction": "Over", "confidence": 0.72}
        },
        "poisson_analysis": {"hdw": "H", "home_goals": 1.8, "away_goals": 1.1},
        "monte_carlo_analysis": {"hdw": "H"},
        "pattern_analysis": {"pattern": "PARTIAL_CONSENSUS"},
        "trap_detector": {"is_trap": False},
        "h2h_analysis": {"total_matches": 5, "avg_goals": 2.8},
        "team_stats": {
            "home": {"form": ["W", "D", "W"], "goals_for": 2.0},
            "away": {"form": ["L", "W", "D"], "goals_for": 1.5}
        }
    },
    {
        "home_team": "Real Madrid",
        "away_team": "Barcelona",
        "league": "La Liga",
        "date": "2025-12-14",
        "odds": {"home": 2.4, "draw": 3.4, "away": 2.8},
        "ml_predictions": {
            "hdw": {
                "prediction": "A", 
                "confidence": 0.68
            },
            "btts": {"prediction": "Yes", "confidence": 0.85},
            "over_25": {"prediction": "Over", "confidence": 0.88}
        },
        "poisson_analysis": {"hdw": "A", "home_goals": 1.5, "away_goals": 1.8},
        "monte_carlo_analysis": {"hdw": "A"},
        "pattern_analysis": {"pattern": "STRONG_CONSENSUS"},
        "trap_detector": {"is_trap": False},
        "h2h_analysis": {"total_matches": 5, "avg_goals": 3.5},
        "team_stats": {
            "home": {"form": ["W", "W", "W"], "goals_for": 2.2},
            "away": {"form": ["W", "W", "W"], "goals_for": 2.5}
        }
    }
]

# --- GEMINI SERVICE CODE (Mini version) ---
class GeminiTester:
    def __init__(self):
        self.api_key = api_key
        self.model_name = "gemini-2.0-flash-exp" # Trying flash model which is usually more reliable
    
    def _build_ticket_prompt(self, matches, min_confidence):
        match_summaries = []
        for i, m in enumerate(matches):
            ml_preds = m.get("ml_predictions", {})
            # Handle nested structure explicitly
            if "hdw" in ml_preds:
                ml_summary = {
                    "prediction": ml_preds["hdw"].get("prediction"),
                    "confidence": ml_preds["hdw"].get("confidence"),
                    "btts": ml_preds.get("btts", {}),
                    "over_25": ml_preds.get("over_25", {})
                }
            else:
                ml_summary = ml_preds # Fallback

            summary = {
                "id": i,
                "match": f"{m.get('home_team')} vs {m.get('away_team')}",
                "league": m.get("league"),
                "odds": m.get("odds", {}),
                "ml_analysis": ml_summary,
                "pattern_analysis": m.get("pattern_analysis", {})
            }
            match_summaries.append(summary)
            
        return f"""You are a professional football betting analyst.
AVAILABLE MATCHES:
{json.dumps(match_summaries, indent=2)}

Generate ONE ticket with exactly 3 matches.
RETURN ONLY JSON:
{{
  "tickets": [
    {{
      "ticket_id": 1,
      "games": [
        {{ "match_id": 0, "bet": "Home Win", "odds": 1.5 }}
      ],
      "combined_odds": 1.5,
      "reasoning": "Test ticket"
    }}
  ]
}}
"""

    async def _call_gemini(self, prompt):
        print(f"\n🔄 Connecting to Gemini ({self.model_name})...")
        payload = {
            "contents": [{"parts": [{"text": prompt}]}],
            "generationConfig": {"temperature": 0.3, "responseMimeType": "application/json"}
        }
        
        url = f"https://generativelanguage.googleapis.com/v1beta/models/{self.model_name}:generateContent?key={self.api_key}"
        
        async with httpx.AsyncClient(timeout=30.0) as client:
            response = await client.post(url, json=payload)
            if response.status_code != 200:
                print(f"\n❌ API Error {response.status_code}: {response.text}")
                return "{}"
            
            data = response.json()
            return data.get("candidates", [{}])[0].get("content", {}).get("parts", [{}])[0].get("text", "{}")

    async def generate_ticket(self):
        prompt = self._build_ticket_prompt(matches, 0.6)
        print(f"\n📝 Prompt generated ({len(prompt)} chars)")
        try:
            response = await self._call_gemini(prompt)
            print(f"\n✅ RESPONSE RECEIVED:\n{response}")
            return response
        except Exception as e:
            print(f"\n❌ EXCEPTION: {e}")

# --- RUNNER ---
async def main():
    tester = GeminiTester()
    await tester.generate_ticket()

if __name__ == "__main__":
    asyncio.run(main())
