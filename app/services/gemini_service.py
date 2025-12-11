"""
Gemini AI Service for Match Analysis and Ticket Generation
With 24-hour caching for cost optimization.
"""
import os
import json
import asyncio
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
        self.api_key = os.getenv("GEMINI_API_KEY")
        # Gemini 3 Pro Preview (verified from API list)
        self.model_name = "gemini-3-pro-preview"
        
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
            # IMPORTANT: Cached data should already have gemini_analysis populated
            # Verify and return
            if cached and isinstance(cached, list) and len(cached) > 0:
                # Check if first match has gemini_analysis (it should if cache is valid)
                if 'gemini_analysis' in cached[0]:
                    return cached
                # Cache is invalid/old format, regenerate
                print(f"   ⚠️ Cache format outdated, regenerating...")
        
        if not self.api_key:
            return self._fallback_analysis(matches)
        
        # Build prompt with match data
        prompt = self._build_analysis_prompt(matches, league_name)
        
        try:
            response = await self._call_gemini(prompt)
            result = self._parse_analysis_response(response, matches)
            
            # Cache the ANALYZED result (with gemini_analysis populated)
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
        """Build enhanced analysis prompt for Gemini 3 Pro with logical reasoning"""
        
        # Format match data
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

You are an elite sports analytics AI with advanced logical reasoning. Your task is to analyze football matches and make superior predictions by identifying patterns that ML models miss.

## CRITICAL CONTEXT: ML MODEL FAILURES (30% error rate)

The ML model makes these systematic errors. YOU must identify and correct them:

### 1. DRAW PATTERN (40% of failures)
**Symptoms**: ML predicts H or A, but result is Draw
**Root Causes**:
- Odds balanced (all 2.5-3.5)
- Teams have similar stats (win rate within 10%)
- Both teams defensive (clean sheet rate > 30%)
**YOUR TASK**: Check if match fits this pattern → increase Draw probability

### 2. UPSET PATTERN (25% of failures)  
**Symptoms**: ML predicts heavy favorite (>70% confidence), but they lose
**Root Causes**:
- Favorite has declining form (last 3 games worse than average)
- Favorite has fixture congestion (congestion_index >= 3)
- Underdog has momentum (improving form)
**YOUR TASK**: Check favorite's recent form + congestion → reduce if red flags

### 3. CLOSE MATCH PATTERN (20% of failures)
**Symptoms**: Odds very tight (all outcomes 2.0-3.0)
**Root Causes**: Inherently unpredictable 50/50 games
**YOUR TASK**: Detect tight odds → cap confidence at 60% maximum

### 4. OVERCONFIDENCE PATTERN (10% of failures)
**Symptoms**: ML confidence >75% but wrong
**Root Causes**: 
- Models disagree (Poisson says H, MC says A)
- Limited H2H data
- Trap warning present
**YOUR TASK**: If models diverge OR trap warning → reduce confidence by 15-20%

### 5. AWAY STRENGTH PATTERN (5% of failures)
**Symptoms**: Strong away team underestimated
**Root Causes**:
- Away team better form than home
- Home team congested (congestion >= 3)
- Away team superior stats
**YOUR TASK**: Check form differential + home congestion → boost away if evident

---

## YOUR ANALYTICAL PROCESS (Step-by-Step Reasoning)

For EACH match, follow this logical chain:

### STEP 1: DATA SYNTHESIS
- Review all 3 models (ML, Poisson, Monte Carlo)
- Check team statistics and form
- Examine odds and implied probabilities
- Note fixture congestion levels
- Read trap detector warnings

### STEP 2: PATTERN DETECTION
Ask yourself:
1. **Is this a DRAW scenario?** (balanced odds + similar stats)
2. **Is this an UPSET risk?** (favorite fatigued/declining)
3. **Is this a CLOSE MATCH?** (tight odds 2.0-3.0)
4. **Are models OVERCONFIDENT?** (divergence or warnings)
5. **Is AWAY team underrated?** (better form + home congested)

### STEP 3: MODEL AGREEMENT ANALYSIS
- Do all 3 models agree? → Higher confidence
- Do 2/3 agree? → Moderate confidence  
- Do all disagree? → Low confidence, check for patterns

### STEP 4: CORRECTION LOGIC
IF pattern detected:
- Apply probability adjustments
- Reduce/increase confidence accordingly
- Override ML if strong pattern evidence

### STEP 5: FINAL DECISION
- Weighted ensemble of corrected probabilities
- Confidence based on agreement + pattern clarity
- Flag uncertainties and traps

---

## DATA PROVIDED

{json.dumps(matches_data, indent=2)}

---

## OUTPUT FORMAT (JSON)

Return valid JSON with NO multi-line strings. Keep all text on single lines:

{{
  "predictions": [
    {{
      "home_team": "Team A",
      "away_team": "Team B",
      "reasoning_summary": "Single line: ML H 75%, Poisson H 60%, MC H 55%. Home congested (index=4). Applied UPSET correction, reduced home 12%.",
      "prediction": "H",
      "confidence": 55,
      "probabilities": {{
        "home_win": 48,
        "draw": 33,
        "away_win": 19
      }},
      "consensus": "PARTIAL_CONSENSUS",
      "pattern_detected": "UPSET_RISK",
      "trap_warning": "Fixture congestion",
      "over_25": true,
      "over_25_confidence": 62,
      "btts": false,
      "btts_confidence": 45,
      "value_bet": false
    }}
  ]
}}

## CRITICAL RULES

1. **Single-line strings** - NO line breaks in JSON strings
2. **Think logically** - Use ALL data provided
3. **Detect patterns** - Apply corrections
4. **Be brave** - Override ML when pattern is clear
5. **Be humble** - Reduce confidence when uncertain
6. **Return ONLY valid JSON** - No markdown

Begin analysis.
"""
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

    async def _call_gemini(self, prompt: str, max_retries: int = 2) -> str:
        """Call Gemini API with timeout and retry logic"""
        
        payload = {
            "contents": [{"parts": [{"text": prompt}]}],
            "generationConfig": {
                "temperature": 0.3,
                "maxOutputTokens": 4096,
                "responseMimeType": "application/json"
            }
        }
        
        for attempt in range(max_retries):
            try:
                timeout = httpx.Timeout(120.0, connect=10.0)
                async with httpx.AsyncClient(timeout=timeout) as client:
                    url = f"https://generativelanguage.googleapis.com/v1beta/models/{self.model_name}:generateContent?key={self.api_key}"
                    
                    response = await client.post(url, json=payload)
                    response.raise_for_status()
                    data = response.json()
                    
                    # Extract text from response
                    text = data.get("candidates", [{}])[0].get("content", {}).get("parts", [{}])[0].get("text", "{}")
                    return text
            except asyncio.TimeoutError:
                if attempt < max_retries - 1:
                    print(f"⏱️  Timeout, retrying... ({attempt + 1}/{max_retries})")
                    await asyncio.sleep(1)  # Brief delay before retry
                else:
                    raise Exception("Gemini API timeout after retries")
            except httpx.HTTPStatusError as e:
                if attempt < max_retries - 1 and e.response.status_code in [429, 503]:
                    print(f"🔄 Rate limit/unavailable, retrying... ({attempt + 1}/{max_retries})")
                    await asyncio.sleep(2 ** attempt)  # Exponential backoff
                else:
                    raise
    
    def _parse_analysis_response(self, response: str, matches: List[Dict]) -> List[Dict]:
        """Parse Gemini analysis response - handles both old and new chain-of-thought format"""
        try:
            # Clean response - remove markdown code blocks if present
            cleaned_response = response.strip()
            if cleaned_response.startswith('```'):
                # Remove markdown code fences
                lines = cleaned_response.split('\n')
                cleaned_response = '\n'.join(lines[1:-1] if len(lines) > 2 else lines)
            
            # Try to parse JSON
            data = json.loads(cleaned_response)
            predictions = data.get("predictions", [])
            
            for i, pred in enumerate(predictions):
                if i >= len(matches):
                    break
                
                try:
                    # Extract prediction (works for both old and new format)
                    prediction = pred.get("prediction", "H")
                    confidence = pred.get("confidence", 50)
                    
                    # Normalize confidence to 0-1 range
                    if isinstance(confidence, (int, float)):
                        confidence = confidence / 100 if confidence > 1 else confidence
                    else:
                        confidence = 0.5
                    
                    # Handle new format with reasoning_chain and probabilities
                    probabilities = pred.get("probabilities", {})
                    if probabilities:
                        home_win = probabilities.get("home_win", 33) / 100 if probabilities.get("home_win", 33) > 1 else probabilities.get("home_win", 0.33)
                        draw = probabilities.get("draw", 33) / 100 if probabilities.get("draw", 33) > 1 else probabilities.get("draw", 0.33)
                        away_win = probabilities.get("away_win", 33) / 100 if probabilities.get("away_win", 33) > 1 else probabilities.get("away_win", 0.33)
                    else:
                        # Old format or defaults
                        home_win = 0.33
                        draw = 0.33
                        away_win = 0.33
                    
                    matches[i]["gemini_analysis"] = {
                        "prediction": prediction,
                        "confidence": confidence,
                        "home_win": home_win,
                        "draw": draw,
                        "away_win": away_win,
                        "consensus": pred.get("consensus", "UNKNOWN"),
                        "pattern_detected": pred.get("pattern_detected", ""),
                        "reasoning_summary": pred.get("reasoning_summary", ""),
                        "over_25": pred.get("over_25", False),
                        "btts": pred.get("btts", False),
                        "trap_warning": pred.get("trap_warning", "")
                    }
                except Exception as match_error:
                    # If individual match fails, use ML fallback for that match
                    ml_pred = matches[i].get('ml_analysis', {})
                    matches[i]["gemini_analysis"] = {
                        "prediction": ml_pred.get('prediction', 'H'),
                        "confidence": ml_pred.get('confidence', 0.5),
                        "home_win": ml_pred.get('home_win', 0.33),
                        "draw": ml_pred.get('draw', 0.33),
                        "away_win": ml_pred.get('away_win', 0.33),
                        "consensus": "UNKNOWN",
                        "pattern_detected": "",
                        "reasoning_summary": f"Parse error for match {i}",
                        "over_25": False,
                        "btts": False,
                        "trap_warning": ""
                    }
            
            return matches
            
        except json.JSONDecodeError as e:
            print(f"JSON Parse error: {e}")
            # Try to extract partial JSON
            try:
                # Attempt to fix common JSON issues
                fixed_response = response.replace('\n', ' ').replace('\r', '')
                # Remove any trailing commas
                fixed_response = fixed_response.replace(',]', ']').replace(',}', '}')
                data = json.loads(fixed_response)
                predictions = data.get("predictions", [])
                
                # Process what we can
                for i, pred in enumerate(predictions[:len(matches)]):
                    if "prediction" in pred:
                        matches[i]["gemini_analysis"] = {
                            "prediction": pred.get("prediction", "H"),
                            "confidence": pred.get("confidence", 50) / 100 if pred.get("confidence", 50) > 1 else pred.get("confidence", 0.5),
                            "over_25": pred.get("over_25", False),
                            "btts": pred.get("btts", False),
                        }
                return matches
            except:
                # Full fallback
                return self._fallback_analysis(matches)
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
