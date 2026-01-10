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
AI_ANALYSIS_PROMPT = """You are a professional football match analyst and betting risk assessor.

IMPORTANT:
- You MUST NOT invent, assume, or estimate any missing information.
- You MUST ONLY use the data provided.
- You MUST be conservative and risk-aware.
- If signals conflict or confidence is low → SAY SO CLEARLY.
- The user is staking real money. Any reckless or forced prediction is considered a failure.

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
PRIMARY OBJECTIVE
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

For EACH match:
1. Evaluate ALL provided data sources
2. Identify the SINGLE BEST betting option (if any)
3. Assign a confidence level: HIGH / MEDIUM / LOW / very low
4. If NO bet is safe → take the best pick and add confidence level very low
5. If you find any trap in data based on odds and calculated data add 1-2 line for that

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
DATA TRUST HIERARCHY (STRICT)
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

You MUST respect this priority order when conflicts exist:

1️⃣ team_stats  
2️⃣ head-to-head (h2h)  
3️⃣ poisson_distribution combined with dixon coles
4️⃣ monte_carlo  
5️⃣ aggregated match_analysis

Lower priority data CANNOT override higher priority data.

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
ALLOWED MARKETS
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

Choose ONLY ONE of:
- Over 2.5 Goals (primary focus if possible chose this instead of wins or draw)
- BTTS Yes (primary focus if possible chose this instead of wins or draw)
- BTTS No
- Home Win. (secondary chose)
- Away Win
- Draw

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
DRAW SAFETY RULE
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

A DRAW may be recommended ONLY IF:
- draw probability ≥ 29%
- AND no strong favorite exists

Otherwise → DO NOT recommend draw

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
CONFLICT HANDLING (VERY IMPORTANT)
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

If models disagree:
- Explain WHY they disagree
- Identify which side is more trustworthy based on DATA TRUST HIERARCHY
- Reduce confidence accordingly

If confidence is LOW → explicitly warn about risk

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
REASONING STYLE (STRICT)
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

For each recommendation:
- 2–4 short sentences
- Football logic ONLY:
  - scoring consistency
  - defensive weakness
  - historical matchup
  - tactical balance

❌ DO NOT mention:
- poisson
- simulations
- probabilities
- models
- percentages

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
OUTPUT FORMAT (MANDATORY JSON)
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

Output valid JSON only (no markdown, no code blocks).
For each match, use the match_id as the key:

{
  "match_id_1": {
    "best_prediction": "Over 2.5 Goals" | "BTTS Yes" | "Home Win" | "Away Win" | "Draw",
    "reason": "2-3 sentences explaining WHY using football logic",
    "short_analysis": "3-5 sentences summarizing the match outlook",
    "confidence_level": "HIGH" | "MEDIUM" | "LOW" | "VERY LOW",
    "trap": "1-2 sentence warning if any trap detected, or empty string"
  },
  "match_id_2": { ... }
}

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
FAIL-SAFE RULE
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

If the match is chaotic, contradictory, or marginal:
→ Set best_prediction to "NO BET" with reason explaining why

DO NOT force a prediction.
Capital preservation is more important than action.
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
            "sample": d.get("sample_size", 0),
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
