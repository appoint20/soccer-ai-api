"""
Gemini AI Service for Match Analysis and Ticket Generation
With 24-hour caching for cost optimization.
"""
import os
import json
import httpx
import hashlib
from typing import List, Dict, Optional
from datetime import datetime, timedelta
from pathlib import Path


# Cache directory
CACHE_DIR = Path(__file__).parent.parent.parent / "data" / "cache"
CACHE_TTL_HOURS = 24


class GeminiService:
    """Gemini AI integration for football analysis with caching."""
    
    def __init__(self):
        self.api_key = os.getenv("GEMINI_API_KEY", "")
        self.base_url = "https://generativelanguage.googleapis.com/v1beta/models"
        self.model = "gemini-2.0-flash"
        
        # Ensure cache directory exists
        CACHE_DIR.mkdir(parents=True, exist_ok=True)
    
    def _get_cache_key(self, league_name: str, matches: List[Dict]) -> str:
        """Generate cache key from league and match data."""
        match_ids = [f"{m.get('home_team', '')}_{m.get('away_team', '')}" for m in matches]
        content = f"{league_name}_{'_'.join(sorted(match_ids))}"
        return hashlib.md5(content.encode()).hexdigest()
    
    def _get_cached_response(self, cache_key: str) -> Optional[List[Dict]]:
        """Get cached Gemini response if valid."""
        cache_file = CACHE_DIR / f"{cache_key}.json"
        
        if not cache_file.exists():
            return None
        
        try:
            with open(cache_file, 'r') as f:
                cached = json.load(f)
            
            # Check TTL
            cached_time = datetime.fromisoformat(cached.get("timestamp", "2000-01-01"))
            if datetime.now() - cached_time > timedelta(hours=CACHE_TTL_HOURS):
                cache_file.unlink()  # Delete expired cache
                return None
            
            return cached.get("data")
        except Exception:
            return None
    
    def _set_cache(self, cache_key: str, data: List[Dict]):
        """Save Gemini response to cache."""
        cache_file = CACHE_DIR / f"{cache_key}.json"
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
            print(f"📦 Cache hit for {league_name}")
            return cached
        
        if not self.api_key:
            return self._fallback_analysis(matches)
        
        # Build prompt with match data
        prompt = self._build_analysis_prompt(matches, league_name)
        
        try:
            response = await self._call_gemini(prompt)
            result = self._parse_analysis_response(response, matches)
            
            # Cache the result
            self._set_cache(cache_key, result)
            
            return result
        except Exception as e:
            print(f"Gemini error: {e}")
            return self._fallback_analysis(matches)
    
    async def generate_tickets(self, matches: List[Dict], min_confidence: float = 0.65) -> List[Dict]:
        """
        Use Gemini to generate optimal betting tickets.
        3 matches per ticket, based on confidence and value.
        """
        if not self.api_key:
            return self._fallback_tickets(matches, min_confidence)
        
        prompt = self._build_ticket_prompt(matches, min_confidence)
        
        try:
            response = await self._call_gemini(prompt)
            return self._parse_ticket_response(response, matches)
        except Exception as e:
            print(f"Gemini ticket error: {e}")
            return self._fallback_tickets(matches, min_confidence)
    
    def _build_analysis_prompt(self, matches: List[Dict], league_name: str) -> str:
        """Build prompt for match analysis with multi-opinion weighting."""
        matches_data = []
        for m in matches:
            # Include statistical predictions for Gemini to weigh
            matches_data.append({
                "home_team": m.get("home_team"),
                "away_team": m.get("away_team"),
                "date": m.get("date"),
                "home_stats": m.get("team_stats", {}).get("home", {}),
                "away_stats": m.get("team_stats", {}).get("away", {}),
                "odds": m.get("odds", {}),
                # Statistical model predictions for Gemini to weigh
                "poisson_prediction": m.get("poisson_analysis", {}),
                "monte_carlo_prediction": m.get("monte_carlo_analysis", {}),
                "pattern_analysis": m.get("pattern_analysis", {})
            })
        
        return f"""You are a professional football betting analyst. You are the FINAL JUDGE who weighs multiple independent predictions.

## MATCHES - {league_name}
{json.dumps(matches_data, indent=2)}

## YOUR TASK
You have 3 independent statistical opinions for each match:
1. POISSON MODEL - Expected goals and probabilities
2. MONTE CARLO - Simulation-based predictions
3. PATTERN ANALYSIS - Agreement detection

Your job:
- Analyze team stats (form, goals, clean sheets)
- Weigh the statistical predictions
- Identify consensus patterns
- Make your final prediction

## CONSENSUS RULES
- ALL 3 AGREE = STRONG_CONSENSUS (69%+ accuracy historically)
- 2/3 AGREE = PARTIAL_CONSENSUS (55% accuracy)
- ALL DIFFER = DIVERGENT (caution advised)

## OUTPUT FORMAT (JSON only)
{{
  "predictions": [
    {{
      "home_team": "Team A",
      "away_team": "Team B",
      "prediction": "H" or "D" or "A",
      "analysis": "4-5 sentence expert analysis weighing all factors",
      "confidence": 10%-100%,
      "consensus": "STRONG_CONSENSUS" or "PARTIAL_CONSENSUS" or "DIVERGENT",
      "over_25": true or false,
      "btts": true or false,
      "value_bet": true or false,
      "trap_warning": "" or "Warning message if trap detected",
      "reasoning": "1-2 sentence final verdict"
    }}
  ]
}}

## RULES
- Be unbiased - don't automatically favor favorites
- Higher confidence when Poisson + Monte Carlo + Form all agree
- Flag traps: strong favorites in poor form, injury prone teams
- Identify value: probability > implied odds probability

Return ONLY valid JSON, no markdown."""

    def _build_ticket_prompt(self, matches: List[Dict], min_confidence: float) -> str:
        """Build prompt for ticket generation"""
        # Prepare match summaries
        match_summaries = []
        for i, m in enumerate(matches):
            summary = {
                "id": i,
                "match": f"{m.get('home_team')} vs {m.get('away_team')}",
                "league": m.get("league"),
                "odds": m.get("odds", {}),
                "prediction": m.get("ml_predictions", {}).get("hdw", {}).get("prediction"),
                "confidence": m.get("ml_predictions", {}).get("hdw", {}).get("confidence"),
                "over25_conf": m.get("ml_predictions", {}).get("over_25", {}).get("confidence"),
                "pattern": m.get("pattern_analysis", {}).get("pattern")
            }
            match_summaries.append(summary)
        
        return f"""You are a professional football betting analyst. Generate betting tickets from these matches.

AVAILABLE MATCHES:
{json.dumps(match_summaries, indent=2)}

RULES:
- Each ticket must have exactly 3 games
- Minimum confidence: {min_confidence}
- Maximum 10 tickets total
- Prioritize STRONG_CONSENSUS patterns
- Mix different bet types (HDW, Over 2.5, BTTS) for diversification
- Calculate combined odds realistically - use bet365 odds from input
- Do NOT use the same match more than once across all tickets
- Be consistent: if match X is predicted Home Win, don't use Over 2.5 for same match in another ticket

Return in this exact JSON format:
{{
  "tickets": [
    {{
      "ticket_id": 1,
      "games": [
        {{"match_id": 0, "bet": "Home Win", "odds": 1.65}},
        {{"match_id": 2, "bet": "Over 2.5", "odds": 1.80}},
        {{"match_id": 5, "bet": "BTTS Yes", "odds": 1.75}}
      ],
      "combined_odds": 5.20,
      "reasoning": "Why these 3 picks work well together",
      "stake": 100,
      "profit": 420,
      "potential_return": 520
    }}
  ]
}}

Return ONLY valid JSON, no markdown."""

    async def _call_gemini(self, prompt: str) -> str:
        """Call Gemini API"""
        url = f"{self.base_url}/{self.model}:generateContent?key={self.api_key}"
        
        payload = {
            "contents": [{"parts": [{"text": prompt}]}],
            "generationConfig": {
                "temperature": 0.3,
                "maxOutputTokens": 4096,
                "responseMimeType": "application/json"
            }
        }
        
        async with httpx.AsyncClient(timeout=60.0) as client:
            response = await client.post(url, json=payload)
            response.raise_for_status()
            data = response.json()
            
            # Extract text from response
            text = data.get("candidates", [{}])[0].get("content", {}).get("parts", [{}])[0].get("text", "{}")
            return text
    
    def _parse_analysis_response(self, response: str, matches: List[Dict]) -> List[Dict]:
        """Parse Gemini analysis response with multi-opinion weighting."""
        try:
            data = json.loads(response)
            predictions = data.get("predictions", [])
            
            # Merge Gemini analysis with original match data
            result = []
            for i, match in enumerate(matches):
                gemini_pred = predictions[i] if i < len(predictions) else {}
                match["gemini_analysis"] = {
                    "prediction": gemini_pred.get("prediction", match.get("ml_predictions", {}).get("hdw", {}).get("prediction")),
                    "analysis": gemini_pred.get("analysis", ""),
                    "confidence": gemini_pred.get("confidence", 0.5),
                    "consensus": gemini_pred.get("consensus", "UNKNOWN"),
                    "over_25": gemini_pred.get("over_25", False),
                    "btts": gemini_pred.get("btts", False),
                    "value_bet": gemini_pred.get("value_bet", False),
                    "trap_warning": gemini_pred.get("trap_warning", ""),
                    "reasoning": gemini_pred.get("reasoning", "No analysis available")
                }
                result.append(match)
            return result
        except Exception as e:
            print(f"Parse error: {e}")
            return self._fallback_analysis(matches)
    
    def _parse_ticket_response(self, response: str, matches: List[Dict]) -> List[Dict]:
        """Parse Gemini ticket response"""
        try:
            data = json.loads(response)
            tickets = data.get("tickets", [])
            
            result = []
            for ticket in tickets:
                games = []
                for game in ticket.get("games", []):
                    match_id = game.get("match_id", 0)
                    if match_id < len(matches):
                        m = matches[match_id]
                        games.append({
                            "match": f"{m.get('home_team')} vs {m.get('away_team')}",
                            "bet": game.get("bet"),
                            "odds": game.get("odds", 1.5),
                            "confidence": m.get("ml_predictions", {}).get("hdw", {}).get("confidence", 0.5),
                            "pattern": m.get("pattern_analysis", {}).get("pattern", "UNKNOWN")
                        })
                
                if len(games) >= 3:
                    combined_odds = 1.0
                    for g in games:
                        combined_odds *= g["odds"]
                    
                    result.append({
                        "ticket_id": ticket.get("ticket_id", len(result) + 1),
                        "stake": 100,
                        "games": games[:3],
                        "combined_odds": round(combined_odds, 2),
                        "potential_return": round(100 * combined_odds, 2),
                        "avg_confidence": round(sum(g["confidence"] for g in games[:3]) / 3, 2),
                        "gemini_reasoning": ticket.get("reasoning", "AI-generated ticket")
                    })
            
            return result
        except Exception as e:
            print(f"Ticket parse error: {e}")
            return self._fallback_tickets(matches, 0.65)
    
    def _fallback_analysis(self, matches: List[Dict]) -> List[Dict]:
        """Fallback when Gemini unavailable"""
        for match in matches:
            match["gemini_analysis"] = {
                "prediction": match.get("ml_predictions", {}).get("hdw", {}).get("prediction", "H"),
                "confidence": match.get("ml_predictions", {}).get("hdw", {}).get("confidence", 0.5),
                "over_25": match.get("ml_predictions", {}).get("over_25", {}).get("prediction") == "Over",
                "btts": match.get("ml_predictions", {}).get("btts", {}).get("prediction") == "Yes",
                "reasoning": "Statistical analysis based on team form and historical performance."
            }
        return matches
    
    def _fallback_tickets(self, matches: List[Dict], min_confidence: float) -> List[Dict]:
        """Fallback ticket generation when Gemini unavailable"""
        # Filter high confidence matches
        confident_matches = [
            m for m in matches 
            if m.get("ml_predictions", {}).get("hdw", {}).get("confidence", 0) >= min_confidence
        ]
        
        # Sort by confidence
        confident_matches.sort(
            key=lambda x: x.get("ml_predictions", {}).get("hdw", {}).get("confidence", 0),
            reverse=True
        )
        
        tickets = []
        for i in range(min(4, len(confident_matches) // 3)):
            ticket_matches = confident_matches[i*3:(i+1)*3]
            if len(ticket_matches) < 3:
                break
            
            games = []
            combined_odds = 1.0
            for m in ticket_matches:
                pred = m.get("ml_predictions", {}).get("hdw", {}).get("prediction", "H")
                odds = m.get("odds", {}).get("home" if pred == "H" else "away" if pred == "A" else "draw", 2.0)
                games.append({
                    "match": f"{m.get('home_team')} vs {m.get('away_team')}",
                    "bet": "Home Win" if pred == "H" else "Away Win" if pred == "A" else "Draw",
                    "odds": round(odds, 2),
                    "confidence": m.get("ml_predictions", {}).get("hdw", {}).get("confidence", 0.5),
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
        
        return tickets


# Singleton
_gemini_service = None

def get_gemini_service() -> GeminiService:
    global _gemini_service
    if _gemini_service is None:
        _gemini_service = GeminiService()
    return _gemini_service
