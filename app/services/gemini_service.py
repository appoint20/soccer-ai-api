"""
Gemini AI Service for football match analysis
"""
import os
import json
import asyncio
import httpx
import hashlib
import traceback
from datetime import datetime, timedelta
from pathlib import Path
from typing import List, Dict, Optional
from openai import OpenAI


class GeminiService:
    """Gemini AI integration for football analysis with caching."""
    
    def __init__(self):
        self.api_key = os.getenv("GEMINI_API_KEY")
        # Use verified flash-exp model
        self.model_name = "gemini-2.0-flash-exp"
        
        if self.api_key:
            # We don't actually use the OpenAI client for the direct API calls in this version,
            # but getting it ready for future use if needed.
            try:
                self.client = OpenAI(
                    api_key=self.api_key,
                    base_url="https://generativelanguage.googleapis.com/v1beta/openai/"
                )
            except:
                self.client = None
        else:
            self.client = None
        
        # Cache directory for Gemini responses (24-hour cache)
        self.cache_dir = Path(__file__).parent.parent.parent / "data" / "cache" / "gemini"
        self.cache_dir.mkdir(parents=True, exist_ok=True)
        self.cache_ttl_hours = 24
    
    def _get_cache_key(self, league_name: str, matches: List[Dict]) -> str:
        """Generate cache key from league and match data."""
        # Sort matches to ensure consistent key regardless of order
        match_ids = [f"{m.get('home_team', '')}_{m.get('away_team', '')}" for m in matches]
        content = f"{league_name}_{'_'.join(sorted(match_ids))}"
        return hashlib.md5(content.encode()).hexdigest()
    
    def _get_cached_response(self, cache_key: str) -> Optional[List[Dict]]:
        """Get cached Gemini response if valid."""
        cache_file = self.cache_dir / f"{cache_key}.json"
        
        if not cache_file.exists():
            return None
        
        try:
            with open(cache_file, 'r') as f:
                cached = json.load(f)
            
            # Check TTL
            cached_time = datetime.fromisoformat(cached.get("timestamp", "2000-01-01"))
            if datetime.now() - cached_time > timedelta(hours=self.cache_ttl_hours):
                cache_file.unlink()  # Delete expired cache
                return None
            
            return cached.get("data")
        except Exception:
            return None
    
    def _set_cache(self, cache_key: str, data: List[Dict]):
        """Save Gemini response to cache."""
        cache_file = self.cache_dir / f"{cache_key}.json"
        try:
            with open(cache_file, 'w') as f:
                json.dump({
                    "timestamp": datetime.now().isoformat(),
                    "data": data
                }, f)
        except Exception:
            pass
    
    async def analyze_matches_batch(self, matches: List[Dict], league_name: str) -> List[Dict]:
        """
        Send batch of matches to Gemini for expert analysis.
        Uses 24-hour cache to reduce API costs.
        """
        if not matches:
            return []
        
        # Check cache first
        cache_key = self._get_cache_key(league_name, matches)
        cached = self._get_cached_response(cache_key)
        if cached:
            # Validate cache format
            if cached and isinstance(cached, list) and len(cached) > 0 and 'gemini_analysis' in cached[0]:
                print(f"📦 Cache hit for {league_name}")
                return cached
        
        if not self.api_key:
            return self._fallback_analysis(matches)
        
        # Build prompt with match data
        prompt = self._build_analysis_prompt(matches, league_name)
        
        try:
            response = await self._call_gemini(prompt)
            result = self._parse_analysis_response(response, matches)
            
            # Cache the ANALYZED result
            self._set_cache(cache_key, result)
            
            return result
        except Exception as e:
            print(f"Gemini analysis error: {e}")
            return self._fallback_analysis(matches)
    
    async def generate_tickets(self, matches: List[Dict], min_confidence: float = 0.65) -> List[Dict]:
        """
        Use Gemini to generate optimal betting tickets.
        3 matches per ticket, based on confidence and value.
        """
        print(f"\n{'='*60}")
        print(f"🎫 TICKET GENERATION START")
        print(f"{'='*60}")
        print(f"   Matches: {len(matches)}")
        print(f"   Min confidence: {min_confidence}")
        
        if not self.api_key:
            print("❌ SKIPPING GEMINI: No API key found in environment")
            return self._fallback_tickets(matches, min_confidence)
        
        print("✅ API key found - proceeding with Gemini")
        
        # OPTIMIZATION: Filter matches BEFORE building prompt to reduce size
        candidates = []
        for m in matches:
            # Handle nested vs flat ml_predictions structure safely
            ml = m.get("ml_predictions", {})
            conf = 0
            if "hdw" in ml:
                conf = ml["hdw"].get("confidence", 0)
            else:
                conf = ml.get("confidence", 0)
                
            if conf >= min_confidence:
                candidates.append(m)
        
        print(f"   Filtered candidates (conf >= {min_confidence}): {len(candidates)} / {len(matches)}")
        
        # Sort by confidence descending
        candidates.sort(key=lambda x: x.get("ml_predictions", {}).get("hdw", {}).get("confidence", 0) if "hdw" in x.get("ml_predictions", {}) else x.get("ml_predictions", {}).get("confidence", 0), reverse=True)
        
        # Cap to top 30 matches for prompt efficiency
        MAX_MATCHES = 30
        if len(candidates) > MAX_MATCHES:
            print(f"   ⚠️ Capping to top {MAX_MATCHES} matches to prevent prompt overflow")
            top_candidates = candidates[:MAX_MATCHES]
        else:
            top_candidates = candidates
            
        if not top_candidates:
            print("⚠️  No matches meet confidence threshold (even for fallback)")
            return self._fallback_tickets(matches, min_confidence) # Fallback might find some marginally close ones

        try:
            print("📝 Building prompt...")
            prompt = self._build_ticket_prompt(top_candidates, min_confidence)
            print(f"   Prompt length: {len(prompt)} chars")
            
            print("🔄 Calling Gemini API...")
            response = await self._call_gemini(prompt)
            print(f"✅ Gemini response received: {len(response)} chars")
            
            tickets = self._parse_ticket_response(response, matches) # Pass ORIGINAL matches list for lookup
            print(f"🎟️  Parsed {len(tickets)} tickets from Gemini")
            
            if len(tickets) == 0:
                print("⚠️  Gemini returned 0 valid tickets, using fallback")
                return self._fallback_tickets(matches, min_confidence)
            
            return tickets
        except Exception as e:
            print(f"❌ Gemini ticket error: {type(e).__name__}: {str(e)}")
            print(f"   Falling back to statistical tickets")
            return self._fallback_tickets(matches, min_confidence)
    
    def _build_analysis_prompt(self, matches: List[Dict], league_name: str) -> str:
        """Build enhanced analysis prompt"""
        matches_data = []
        for m in matches:
            matches_data.append({
                'home_team': m['home_team'],
                'away_team': m['away_team'],
                'date': m.get('date', ''),
                'odds': m['odds'],
                'home_stats': m['team_stats']['home'],
                'away_stats': m['team_stats']['away'],
                'ml_model': m['ml_analysis'],
                'poisson_model': m['poisson_analysis'],
                'monte_carlo_model': m['monte_carlo_analysis'],
                'pattern': m['pattern_analysis'],
                'trap_detector': m['trap_detector'],
            })
        
        return f"""# SOCCER MATCH ANALYSIS - {league_name}
You are an expert sports analyst. Analyze these football matches.
Identify patterns where ML models might fail (draws, upsets).
Return clear JSON predictions.

DATA:
{json.dumps(matches_data, indent=2)}

OUTPUT FORMAT (JSON):
{{
  "predictions": [
    {{
      "home_team": "Team A",
      "away_team": "Team B",
      "prediction": "H",
      "confidence": 0.75,
      "probabilities": {{"home_win": 0.60, "draw": 0.25, "away_win": 0.15}},
      "consensus": "STRONG_CONSENSUS",
      "reasoning_summary": "ML and Monte Carlo agree on Home Win. Strong home form.",
      "over_25": true,
      "btts": false
    }}
  ]
}}
Return ONLY valid JSON.
"""

    def _build_ticket_prompt(self, matches: List[Dict], min_confidence: float) -> str:
        """Build comprehensive prompt for ticket generation"""
        match_summaries = []
        for i, m in enumerate(matches):
            ml_preds = m.get("ml_predictions", {})
            if "hdw" in ml_preds:
                ml_summary = {
                    "prediction": ml_preds["hdw"].get("prediction"),
                    "confidence": ml_preds["hdw"].get("confidence"),
                    "btts": ml_preds.get("btts", {}),
                    "over_25": ml_preds.get("over_25", {})
                }
            else:
                ml_summary = {
                    "prediction": ml_preds.get("prediction"),
                    "confidence": ml_preds.get("confidence"),
                    "btts": ml_preds.get("btts_prediction"),
                    "over_25": ml_preds.get("over25_prediction")
                }
            
            # Use original index/ID to map back correctly
            # We add a temporary 'temp_id' to the prompt to help the LLM refer to it
            summary = {
                "match_id": i, # Key for mapping back
                "match": f"{m.get('home_team')} vs {m.get('away_team')}",
                "league": m.get("league"),
                "odds": m.get("odds", {}),
                "ml_analysis": ml_summary,
                "pattern_analysis": m.get("pattern_analysis", {}),
                "reasoning": "Consensus analysis"
            }
            match_summaries.append(summary)
        
        return f"""You are an AI betting expert.
Create betting tickets from these matches using strict criteria.

MATCHES:
{json.dumps(match_summaries, indent=2)}

RULES:
1. Create up to 10 tickets.
2. Each ticket MUST have EXACTLY 3 matches.
3. Use 'match_id' from the input to identify games.
4. Select Best Bets: High confidence, Strong Consenus, No Traps.
5. Min confidence: {min_confidence}.

OUTPUT FORMAT (JSON ONLY):
{{
  "tickets": [
    {{
      "ticket_id": 1,
      "games": [
        {{ "match_id": 0, "bet": "Home Win", "odds": 1.5 }},
        {{ "match_id": 5, "bet": "Over 2.5", "odds": 1.8 }},
        {{ "match_id": 8, "bet": "BTTS Yes", "odds": 1.7 }}
      ],
      "combined_odds": 4.59,
      "reasoning": "Strong home favorite combined with high-scoring patterns.",
      "stake": 100,
      "potential_return": 459
    }}
  ]
}}
"""

    async def _call_gemini(self, prompt: str, max_retries: int = 2) -> str:
        """Call Gemini API with httpx"""
        payload = {
            "contents": [{"parts": [{"text": prompt}]}],
            "generationConfig": {
                "temperature": 0.3,
                "responseMimeType": "application/json"
            }
        }
        
        url = f"https://generativelanguage.googleapis.com/v1beta/models/{self.model_name}:generateContent?key={self.api_key}"
        
        for attempt in range(max_retries + 1):
            try:
                async with httpx.AsyncClient(timeout=45.0) as client:
                    response = await client.post(url, json=payload)
                    
                    if response.status_code == 200:
                        data = response.json()
                        candidates = data.get("candidates", [])
                        if candidates:
                            return candidates[0]["content"]["parts"][0]["text"]
                        return "{}" # Empty JSON if no text
                    
                    elif response.status_code in [429, 503]:
                        if attempt < max_retries:
                            await asyncio.sleep(2 ** attempt)
                            continue
                        else:
                            print(f"❌ Gemini Rate Limit/Unavailable: {response.text}")
                            raise Exception("Gemini API Unavailable")
                    else:
                        print(f"❌ Gemini API Error {response.status_code}: {response.text}")
                        raise Exception(f"Gemini API Error {response.status_code}")
                        
            except httpx.TimeoutException:
                if attempt < max_retries:
                    print("⏱️  Gemini Timeout - retrying...")
                    continue
                raise Exception("Gemini Timeout")
            except Exception as e:
                if attempt < max_retries:
                    continue
                raise e
        return "{}"

    def _parse_analysis_response(self, response: str, matches: List[Dict]) -> List[Dict]:
        """Parse Gemini analysis response"""
        try:
            # Clean possible markdown
            cleaned = response.replace("```json", "").replace("```", "").strip()
            data = json.loads(cleaned)
            predictions = data.get("predictions", [])
            
            for i, match in enumerate(matches):
                # Default empty
                match["gemini_analysis"] = {
                    "prediction": "N/A", "confidence": 0, 
                    "reasoning_summary": "Analysis failed"
                }
                
                # Try to map by index if array lengths match, or logical fallback
                if i < len(predictions):
                    pred = predictions[i]
                    # In a real scenario, we might want to match home/away team names
                    # but simple index matching is usually fine for batch processing if order is preserved
                    match["gemini_analysis"] = {
                        "prediction": pred.get("prediction", "H"),
                        "confidence": pred.get("confidence", 0.5),
                        "reasoning_summary": pred.get("reasoning_summary", "AI Analysis"),
                        "over_25": pred.get("over_25", False),
                        "btts": pred.get("btts", False)
                    }
            return matches
        except Exception as e:
            print(f"Error parsing analysis: {e}")
            return self._fallback_analysis(matches)

    def _parse_ticket_response(self, response: str, matches: List[Dict]) -> List[Dict]:
        """Parse Gemini ticket response"""
        try:
            cleaned = response.replace("```json", "").replace("```", "").strip()
            data = json.loads(cleaned)
            tickets = data.get("tickets", [])
            
            parsed_tickets = []
            for ticket in tickets:
                games = []
                for game in ticket.get("games", []):
                    match_id = game.get("match_id")
                    
                    # Validate match_id exists in our list
                    if isinstance(match_id, int) and 0 <= match_id < len(matches):
                        original_match = matches[match_id]
                        
                        # Use ML confidence as base if Gemini doesn't provide specific confidence
                        # Or extract from original match data
                        ml_preds = original_match.get("ml_predictions", {})
                        if "hdw" in ml_preds:
                            conf = ml_preds["hdw"].get("confidence", 0.5)
                        else:
                            conf = ml_preds.get("confidence", 0.5)
                            
                        pattern = original_match.get("pattern_analysis", {}).get("pattern", "UNKNOWN")
                        
                        games.append({
                            "match": f"{original_match.get('home_team')} vs {original_match.get('away_team')}",
                            "bet": game.get("bet", "Home Win"),
                            "odds": game.get("odds", 1.5),
                            "confidence": conf,
                            "pattern": pattern
                        })
                
                # Only add tickets with games
                if games:
                    parsed_tickets.append({
                        "ticket_id": ticket.get("ticket_id"),
                        "stake": ticket.get("stake", 100),
                        "games": games,
                        "combined_odds": ticket.get("combined_odds", 0),
                        "potential_return": ticket.get("potential_return", 0),
                        "gemini_reasoning": ticket.get("reasoning", "AI Generated")
                    })
                    
            return parsed_tickets
        except Exception as e:
            print(f"Error parsing tickets: {e}")
            return [] # Return empty to trigger fallback logic in caller

    def _fallback_analysis(self, matches: List[Dict]) -> List[Dict]:
        """Fallback when Gemini unavailable"""
        for match in matches:
            match["gemini_analysis"] = {
                "prediction": "N/A",
                "confidence": 0,
                "reasoning": "Fallback - AI Unavailable"
            }
        return matches
    
    def _fallback_tickets(self, matches: List[Dict], min_confidence: float) -> List[Dict]:
        """Fallback ticket generation when Gemini unavailable"""
        print(f"🔧 Fallback tickets: {len(matches)} matches, min_conf={min_confidence}")
        
        # Filter high confidence matches
        confident_matches = []
        for m in matches:
            # Safe access to nested predictions
            ml = m.get("ml_predictions", {})
            conf = 0
            if "hdw" in ml:
                conf = ml["hdw"].get("confidence", 0)
            else:
                conf = ml.get("confidence", 0)
                
            if conf >= min_confidence:
                confident_matches.append(m)
        
        print(f"   Filtered to {len(confident_matches)} matches with confidence >= {min_confidence}")
        
        # Sort by confidence
        confident_matches.sort(
            key=lambda x: x.get("ml_predictions", {}).get("hdw", {}).get("confidence", 0) if "hdw" in x.get("ml_predictions", {}) else x.get("ml_predictions", {}).get("confidence", 0),
            reverse=True
        )
        
        tickets = []
        # Create up to 10 tickets
        for i in range(min(10, len(confident_matches) // 3)):
            ticket_matches = confident_matches[i*3:(i+1)*3]
            if len(ticket_matches) < 3:
                break
            
            games = []
            combined_odds = 1.0
            for m in ticket_matches:
                # Determine best bet (simple logic for fallback)
                ml = m.get("ml_predictions", {})
                pred = "H"
                if "hdw" in ml:
                    pred = ml["hdw"].get("prediction", "H")
                    conf = ml["hdw"].get("confidence", 0.5)
                else:
                    pred = ml.get("prediction", "H")
                    conf = ml.get("confidence", 0.5)
                
                odds_dict = m.get("odds", {})
                odds = 1.5 # Default
                bet_name = "Home Win"
                
                if pred == "H":
                    odds = odds_dict.get("home", 1.5)
                    bet_name = "Home Win"
                elif pred == "A":
                    odds = odds_dict.get("away", 1.5)
                    bet_name = "Away Win"
                else:
                    odds = odds_dict.get("draw", 3.0)
                    bet_name = "Draw"
                    
                games.append({
                    "match": f"{m.get('home_team')} vs {m.get('away_team')}",
                    "bet": bet_name,
                    "odds": odds,
                    "confidence": conf,
                    "pattern": m.get("pattern_analysis", {}).get("pattern", "UNKNOWN")
                })
                combined_odds *= odds
            
            tickets.append({
                "ticket_id": i + 1,
                "stake": 100,
                "games": games,
                "combined_odds": round(combined_odds, 2),
                "potential_return": round(100 * combined_odds, 2),
                "avg_confidence": round(sum(g["confidence"] for g in games) / 3, 2),
                "gemini_reasoning": "Fallback: No AI analysis - based on statistical model"
            })
        
        print(f"   Generated {len(tickets)} tickets (fallback)")
        return tickets


# Singleton
_gemini_service = None

def get_gemini_service() -> GeminiService:
    global _gemini_service
    if _gemini_service is None:
        _gemini_service = GeminiService()
    return _gemini_service
