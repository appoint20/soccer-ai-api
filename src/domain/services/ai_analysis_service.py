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
    
    def to_dict(self) -> Dict[str, Any]:
        return asdict(self)


# Prompt template for AI analysis (JSON output)
AI_ANALYSIS_PROMPT = """You are a professional football match analyst.

TASK: Analyze each match and provide betting recommendations.

RULES:
- Use ONLY the data provided
- Be conservative - if signals conflict, say "NO BET"
- Do NOT mention ML, Poisson, algorithms, or probabilities
- Use football logic: form, quality, tactics, history

For EACH match, output a JSON object with:
- "best_prediction": One of: "Over 2.5 Goals", "BTTS Yes", "Home Win", "Away Win", "Draw", "NO BET"
- "reason": 2-3 sentences explaining WHY (football logic only)
- "short_analysis": 3-5 sentences summarizing the match outlook
- "confidence_level": "HIGH", "MEDIUM", or "LOW"

CONFIDENCE RULES:
- HIGH: 3+ data sources agree, clear form advantage
- MEDIUM: 2 sources agree, some uncertainty
- LOW: Mixed signals but slight edge visible

OUTPUT FORMAT (JSON only, no markdown):
{
  "match_id_1": {
    "best_prediction": "...",
    "reason": "...",
    "short_analysis": "...",
    "confidence_level": "..."
  },
  "match_id_2": { ... }
}

MATCH DATA:
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
                self.model = genai.GenerativeModel("gemini-2.0-flash")
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
    """
    
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
    
    def analyze_matches_batch(
        self,
        matches: List[Dict[str, Any]],
        league: str,
    ) -> Dict[str, AIAnalysis]:
        """
        Analyze a batch of matches (typically one league).
        
        Args:
            matches: List of match analysis dicts with stats
            league: League name for logging
            
        Returns:
            Dict mapping match_id to AIAnalysis
        """
        if not matches:
            return {}
        
        self.logger.info(f"Analyzing {len(matches)} matches for {league}")
        
        # Prepare compact data for prompt
        match_data = []
        for m in matches:
            match_data.append({
                "match_id": m.get("match_id"),
                "home_team": m.get("home_team"),
                "away_team": m.get("away_team"),
                "home_form": self._extract_form_summary(m.get("home_last_5", {})),
                "away_form": self._extract_form_summary(m.get("away_last_5", {})),
                "h2h": self._extract_h2h_summary(m.get("h2h_last_5", {})),
                "poisson": self._extract_poisson_summary(m.get("poisson", {})),
                "overall_confidence": m.get("overall_confidence", 0),
            })
        
        # Build prompt
        prompt = AI_ANALYSIS_PROMPT + json.dumps(match_data, indent=2)
        
        try:
            # Call AI
            response_text = self._provider.generate(prompt)
            
            # Parse JSON
            result = self._parse_response(response_text, [m["match_id"] for m in match_data])
            
            self.logger.info(f"Successfully analyzed {len(result)} matches for {league}")
            return result
            
        except Exception as e:
            self.logger.error(f"AI analysis failed for {league}: {e}")
            return {}
    
    def _extract_form_summary(self, form: Dict[str, Any]) -> Dict[str, Any]:
        """Extract compact form summary for prompt."""
        if not form:
            return {}
        return {
            "win_rate": form.get("win_rate", 0),
            "over25_rate": form.get("over_25_rate", 0),
            "btts_rate": form.get("btts_rate", 0),
            "avg_scored": form.get("avg_goals_scored", 0),
            "avg_conceded": form.get("avg_goals_conceded", 0),
            "sample": form.get("sample_size", 0),
        }
    
    def _extract_h2h_summary(self, h2h: Dict[str, Any]) -> Dict[str, Any]:
        """Extract compact H2H summary for prompt."""
        if not h2h:
            return {}
        return {
            "matches": h2h.get("total_matches", 0),
            "home_win_rate": h2h.get("home_win_rate", 0),
            "over25_rate": h2h.get("over_25_rate", 0),
            "btts_rate": h2h.get("btts_rate", 0),
            "reliability": h2h.get("h2h_reliability", 0),
        }
    
    def _extract_poisson_summary(self, poisson: Dict[str, Any]) -> Dict[str, Any]:
        """Extract compact Poisson summary for prompt."""
        if not poisson:
            return {}
        return {
            "home_win": poisson.get("home_win", 0),
            "draw": poisson.get("draw", 0),
            "away_win": poisson.get("away_win", 0),
            "over25": poisson.get("over_25", 0),
            "btts": poisson.get("btts", 0),
            "xg_home": poisson.get("expected_home_goals", 0),
            "xg_away": poisson.get("expected_away_goals", 0),
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
                    )
            
        except json.JSONDecodeError as e:
            self.logger.error(f"Failed to parse AI response: {e}")
            self.logger.debug(f"Raw response: {response_text[:500]}")
        
        return results
