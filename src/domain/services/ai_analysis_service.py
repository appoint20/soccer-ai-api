"""
AI Analysis Service.

Configurable AI service that uses either Gemini or OpenAI to provide
best predictions, reasoning, and short analysis for matches.
Supports batch processing by league for token efficiency.
"""
import os
import json
from abc import ABC, abstractmethod
from dataclasses import dataclass, asdict
from typing import List, Dict, Any, Optional

from src.utils.logger import get_logger


@dataclass
class AIAnalysis:
    """AI-generated analysis for a single match."""
    best_prediction: str       # "Over 2.5 Goals" | "BTTS Yes" | "Home Win" | "NO BET"
    reason: str                # 2-3 sentences
    short_analysis: str        # 3-5 sentences  
    confidence_level: str      # "HIGH" | "MEDIUM" | "LOW"
    trap: str = ""             # Warning about potential traps
    
    def to_dict(self) -> Dict[str, Any]:
        return asdict(self)


# Prompt template for AI analysis (JSON output)
AI_ANALYSIS_PROMPT = """
ROLE

You are a professional football match analyst and betting risk assessor.

You analyze PRE-CALCULATED batch match data objects.
ALL statistical logic, probabilities, and qualification flags are ALREADY computed by the backend.

You MUST interpret the data.
You MUST NOT recalculate, override, or invent logic.

The user is staking REAL MONEY.
Capital protection is more important than prediction frequency.

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
ABSOLUTE HARD RULES (NON-NEGOTIABLE)
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

DO NOT invent, assume, or estimate any data
DO NOT enrich or modify input objects
DO NOT override qualification flags
DO NOT ignore classic_draw_profile
DO NOT force a bet
DO NOT prefer wins when structure indicates balance

Violation of ANY rule → OUTPUT IS INVALID

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
PRIMARY OBJECTIVE (FOR EACH MATCH)
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

For EACH match object:

1. Deeply analyze ALL provided data layers
2. Select EXACTLY ONE best betting option OR "NO BET"
3. Assign a conservative confidence level
4. Explicitly detect and explain traps
5. Prefer structural truth over attractive markets

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
DATA TRUST PRIORITY (STRICT ORDER)
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

You MUST evaluate ALL layers in this order:

1️⃣ match_analysis (qualification flags are FINAL)
2️⃣ classic_draw_profile (STRUCTURAL OVERRIDE SIGNAL)
3️⃣ team performance & trends
4️⃣ head-to-head
5️⃣ Poisson & Monte Carlo (SUPPORTING ONLY)

Poisson and Monte Carlo:
- MAY confirm volatility or balance
- MUST NOT override structure or qualification

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
CRITICAL CLASSIC DRAW HANDLING (MANDATORY)
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

If:
- classic_draw_profile.classic_draw_detected == true

THEN:
- Treat the match as STRUCTURALLY BALANCED
- Expect controlled outcomes such as 1–1 or 2–1
- HIGH-GOAL expectations must be downgraded

SPECIAL FALLBACK RULE (VERY IMPORTANT):

If:
- classic_draw_detected == true
- AND BTTS qualified == false
- AND Over 2.5 qualified == false
- AND Draw qualified == true

→ You MUST select "Draw"

You are NOT allowed to:
- choose Home Win
- choose Away Win
- choose NO BET
in this scenario

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
ALLOWED MARKETS (ONLY THESE)
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

Choose EXACTLY ONE:

- Over 2.5 Goals
- Under 2.5 Goals
- BTTS Yes
- 2–3 Goals
- Home Win
- Away Win
- Draw
- NO BET

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
QUALIFICATION ENFORCEMENT (STRICT)
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

- If match_analysis.market.qualified == false
  → That market is FORBIDDEN

- Qualification does NOT mean safety
- Disqualification MUST be respected without exception

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
DRAW SELECTION RULE (UPDATED)
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

You SHOULD select DRAW if:

✔ Draw qualified == true
✔ classic_draw_detected == true
✔ No dominant team exists
✔ Goal markets are downgraded due to structure

Draws are NOT low-goal-only outcomes.
1–1 and controlled 2–1 profiles are VALID draw structures.

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
REASONING STYLE (GEMINI SAFE)
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

2–4 short sentences per field
Football logic ONLY:
- balance
- control
- scoring symmetry
- tactical containment
- historical patterns

❌ DO NOT mention:
probabilities
percentages
Poisson
Monte Carlo
models
algorithms
AI

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
OUTPUT FORMAT (STRICT JSON – NO MARKDOWN)
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

Output ONE JSON object for ALL matches.
Each key = match_id.

{
  "match_id": {
    "best_prediction": "Over 2.5 Goals | BTTS Yes | BTTS No | Home Win | Away Win | Draw | NO BET",
    "reason": "2–3 sentences using football logic only",
    "short_analysis": "3–5 sentences explaining structure, balance, and risk",
    "confidence_level": "HIGH | MEDIUM | LOW | VERY LOW",
    "trap": "1–2 sentence warning or empty string"
  }
}

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
FINAL FAIL-SAFE (MANDATORY)
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

If a match:
- is structurally balanced
- shows classic draw signals
- downgrades goal markets
- lacks a dominant side

→ DRAW is preferred over NO BET

If structure is chaotic or contradictory →
→ OUTPUT "NO BET"

DO NOT FORCE ACTION.
STRUCTURAL TRUTH > MARKET TEMPTATION.
"""


class AIProvider(ABC):
    """Abstract base class for AI providers."""
    
    @abstractmethod
    def generate(self, prompt: str) -> str:
        """Generate text from prompt."""
        pass


class GeminiProvider(AIProvider):
    """Gemini AI provider."""
    
    def __init__(self, api_key: Optional[str] = None):
        self.logger = get_logger("GeminiProvider")
        self.api_key = api_key or os.getenv("GEMINI_API_KEY")
        self.model = None
        
        if self.api_key:
            try:
                import google.generativeai as genai
                genai.configure(api_key=self.api_key)
                self.model = genai.GenerativeModel("gemini-2.5-pro")
                self.logger.info("Gemini provider initialized")
            except Exception as e:
                self.logger.error(f"Failed to initialize Gemini: {e}")
        else:
            self.logger.warning("GEMINI_API_KEY not found")
    
    def generate(self, prompt: str) -> str:
        if not self.model:
            raise RuntimeError("Gemini model not initialized")
        
        response = self.model.generate_content(
            prompt,
            generation_config={"response_mime_type": "application/json"}
        )
        return response.text


class OpenAIProvider(AIProvider):
    """OpenAI GPT provider."""
    
    def __init__(self, api_key: Optional[str] = None, model: str = "gpt-4o-mini"):
        self.logger = get_logger("OpenAIProvider")
        self.api_key = api_key or os.getenv("OPENAI_API_KEY")
        self.model_name = model
        self.client = None
        
        if self.api_key:
            try:
                from openai import OpenAI
                self.client = OpenAI(api_key=self.api_key)
                self.logger.info(f"OpenAI provider initialized with model {model}")
            except Exception as e:
                self.logger.error(f"Failed to initialize OpenAI: {e}")
        else:
            self.logger.warning("OPENAI_API_KEY not found")
    
    def generate(self, prompt: str) -> str:
        if not self.client:
            raise RuntimeError("OpenAI client not initialized")
        
        response = self.client.chat.completions.create(
            model=self.model_name,
            messages=[
                {"role": "system", "content": "You are a professional football analyst. Output valid JSON only."},
                {"role": "user", "content": prompt}
            ],
            response_format={"type": "json_object"}
        )
        return response.choices[0].message.content


class AIAnalysisService:
    """
    AI Analysis Service with configurable provider.
    
    Supports batch processing by league for token efficiency.
    Caches responses by date to reduce API calls.
    """
    
    # Use Firestore cache (with file fallback)
    # Import here to avoid circular dependency
    from src.infrastructure.cache.firestore_cache import get_cache
    _cache = get_cache()
    
    def __init__(self, provider: Optional[str] = None):
        """
        Initialize service.
        
        Args:
            provider: "gemini" or "openai" (default from AI_PROVIDER env var)
        """
        self.logger = get_logger("AIAnalysisService")
        provider_name = provider or os.getenv("AI_PROVIDER", "gemini").lower()
        
        if provider_name == "openai":
            self._provider = OpenAIProvider()
        else:
            self._provider = GeminiProvider()
        
        self.logger.info(f"AI Analysis Service using provider: {provider_name}")
    
    def _load_cache(self, date: str, league: str) -> Optional[Dict[str, AIAnalysis]]:
        """Load cached AI analysis using Firestore (with file fallback)."""
        try:
            cached = self._cache.get_ai_analysis(date, league)
            if cached and "analyses" in cached:
                # Convert to AIAnalysis objects
                result = {}
                for match_id, item in cached["analyses"].items():
                    result[match_id] = AIAnalysis(
                        best_prediction=item.get("best_prediction", "NO BET"),
                        reason=item.get("reason", ""),
                        short_analysis=item.get("short_analysis", ""),
                        confidence_level=item.get("confidence_level", "LOW"),
                        trap=item.get("trap", ""),
                    )
                
                self.logger.info(f"Loaded cached AI analysis for {date}/{league}: {len(result)} matches")
                return result
        except Exception as e:
            self.logger.warning(f"Failed to load cache: {e}")
        
        return None
    
    def _save_cache(self, date: str, league: str, results: Dict[str, AIAnalysis]) -> None:
        """Save AI analysis to Firestore cache."""
        try:
            # Convert to dict for serialization
            data = {
                match_id: analysis.to_dict()
                for match_id, analysis in results.items()
            }
            
            self._cache.save_ai_analysis(date, league, data)
            self.logger.info(f"Saved AI analysis cache: {date}/{league}")
        except Exception as e:
            self.logger.error(f"Failed to save cache: {e}")
    
    def analyze_matches_batch(
        self,
        analyses: List[Any], # Typed as List[SingleMatchAnalysis] but avoiding circular import in type hint
        league: str,
        date: Optional[str] = None,
        refresh: bool = False,
    ) -> Dict[str, AIAnalysis]:
        """
        Analyze a batch of matches (typically one league).
        
        Args:
            analyses: List of SingleMatchAnalysis objects
            league: League name for logging
            date: Date string for caching (YYYY-MM-DD)
            refresh: Force refresh from AI (bypass cache)
            
        Returns:
            Dict mapping match_id to AIAnalysis
        """
        if not analyses:
            return {}
        
        # Extract date from first match if not provided
        if not date and analyses:
            date = analyses[0].date
        
        # Try cache first (unless refresh requested)
        if date and not refresh:
            cached = self._load_cache(date, league)
            if cached:
                # Check if all matches are in cache
                match_ids = {a.match_id for a in analyses}
                cached_ids = set(cached.keys())
                
                if match_ids.issubset(cached_ids):
                    self.logger.info(f"Using cached AI analysis for {league}")
                    return {mid: cached[mid] for mid in match_ids if mid in cached}
                else:
                    missing = match_ids - cached_ids
                    self.logger.info(f"Cache miss for {league}: {len(missing)} matches missing from cache")
            else:
                self.logger.info(f"No cache found for {date}/{league}")
        elif refresh:
            self.logger.info("Refresh requested: bypassing cache")
        else:
            self.logger.warning("No date provided for caching")
        
        # Prepare compact data for prompt
        match_data = []
        for a in analyses:
            # Access fields via canonical object structure
            match_data.append({
                "match_id": a.match_id,
                "home_team": a.home_team,
                "away_team": a.away_team,
                "home_form": self._extract_form_summary(a.homeStats.last_5),
                "away_form": self._extract_form_summary(a.awayStats.last_5),
                "h2h": self._extract_h2h_summary(a.h2h_last_5),
                "poisson": self._extract_poisson_summary(a.poisson),
                "overall_confidence": a.overall_confidence,
                "qualified_draw": a.draw_analysis.get("qualified", False) if a.draw_analysis else False,
                "btts_prob": a.match_analysis.btts.probability if a.match_analysis else 0.0,
            })
        
        # Build prompt
        prompt = AI_ANALYSIS_PROMPT + json.dumps(match_data, indent=2)
        
        try:
            # Call AI
            response_text = self._provider.generate(prompt)
            self.logger.info(f"Raw AI response: {response_text[:500]}...") # Log first 500 chars
            
            # Parse JSON
            result = self._parse_response(response_text, [m["match_id"] for m in match_data])
            self.logger.info(f"Parsed result keys: {list(result.keys())}")
            
            # Save to cache
            if date and result:
                self._save_cache(date, league, result)
            
            self.logger.info(f"Successfully analyzed {len(result)} matches for {league}")
            return result
            
        except Exception as e:
            self.logger.error(f"AI analysis failed for {league}: {e}")
            return {}
    
    def _extract_form_summary(self, form: Any) -> Dict[str, Any]:
        """Extract compact form summary from TeamFormStats object."""
        if not form:
            return {}
        # Handle Pydantic model vs dataclass vs dict
        if hasattr(form, "dict"):
            d = form.dict()
        elif hasattr(form, "__dict__"):
            d = form.__dict__
        else:
            d = form
            
        return {
            "win_rate": d.get("win_rate", 0),
            "over25_rate": d.get("over_25_rate", 0),
            "btts_rate": d.get("btts_rate", 0),
            "avg_scored": d.get("avg_goals_scored", 0),
            "avg_conceded": d.get("avg_goals_conceded", 0),
        }
    
    def _extract_h2h_summary(self, h2h: Any) -> Dict[str, Any]:
        """Extract compact H2H summary from H2HStats object."""
        if not h2h:
            return {}
        if hasattr(h2h, "dict"):
            d = h2h.dict()
        elif hasattr(h2h, "__dict__"):
            d = h2h.__dict__
        else:
            d = h2h
            
        return {
            "matches": d.get("total_matches", 0),
            "home_win_rate": d.get("home_win_rate", 0),
            "over25_rate": d.get("over_25_rate", 0),
            "btts_rate": d.get("btts_rate", 0),
            "reliability": d.get("h2h_reliability", 0),
        }
    
    def _extract_poisson_summary(self, poisson: Any) -> Dict[str, Any]:
        """Extract compact Poisson summary from PoissonProbabilities object."""
        if not poisson:
            return {}
        if hasattr(poisson, "dict"):
            d = poisson.dict()
        elif hasattr(poisson, "__dict__"):
            d = poisson.__dict__
        else:
            d = poisson
            
        return {
            "home_win": d.get("home_win", 0),
            "draw": d.get("draw", 0),
            "away_win": d.get("away_win", 0),
            "over25": d.get("over_25", 0),
            "btts": d.get("btts", 0),
            "xg_home": d.get("expected_home_goals", 0),
            "xg_away": d.get("expected_away_goals", 0),
        }
    
    def _parse_response(
        self,
        response_text: str,
        match_ids: List[str],
    ) -> Dict[str, AIAnalysis]:
        """Parse AI response into structured objects."""
        results = {}
        
        # Clean markdown if present
        text = response_text.strip()
        if "```json" in text:
            text = text.split("```json")[1].split("```")[0]
        elif "```" in text:
            text = text.split("```")[1].split("```")[0]
        
        try:
            data = json.loads(text)
            
            for match_id in match_ids:
                if match_id in data:
                    item = data[match_id]
                    results[match_id] = AIAnalysis(
                        best_prediction=item.get("best_prediction", "NO BET"),
                        reason=item.get("reason", ""),
                        short_analysis=item.get("short_analysis", ""),
                        confidence_level=item.get("confidence_level", "LOW"),
                        trap=item.get("trap", ""),
                    )
            
        except json.JSONDecodeError as e:
            self.logger.error(f"Failed to parse AI response: {e}")
            self.logger.debug(f"Raw response: {response_text[:500]}")
        
        return results
