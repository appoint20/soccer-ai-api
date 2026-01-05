import os
import json
import google.generativeai as genai
from typing import List, Dict, Any, Optional
from src.utils.logger import get_logger

class GeminiService:
    def __init__(self, api_key: Optional[str] = None):
        self.logger = get_logger("GeminiService")
        self.api_key = api_key or os.getenv("GEMINI_API_KEY")
        if self.api_key:
            genai.configure(api_key=self.api_key)
            # Validated model from list_models
            self.model_name = "gemini-2.5-pro" 
            self.model = genai.GenerativeModel(self.model_name)
        else:
            self.logger.warning("Gemini API Key not found. AI generation will fail.")

    def generate_tickets(self, prompt_template: str, analysis_data: List[Dict[str, Any]]) -> Dict[str, Any]:
        """
        Generates tickets using Gemini based on analysis data.
        """
        if not self.api_key:
            return {"error": "API Key missing"}

        try:
            # Prepare data context (limit size if needed)
            # data_str = json.dumps(analysis_data, default=str) # Can be large
            # We might need to summarize or send minimal fields
            
            # Construct full prompt
            full_prompt = f"{prompt_template}\n\nINPUT DATA:\n{json.dumps(analysis_data, default=str)}"
            
            # Call API
            response = self.model.generate_content(full_prompt, generation_config={"response_mime_type": "application/json"})
            
            # Parse JSON
            try:
                text = response.text
                # Cleanup markdown if present
                if "```json" in text:
                    text = text.split("```json")[1].split("```")[0]
                elif "```" in text:
                    text = text.split("```")[1].split("```")[0]
                    
                return json.loads(text)
            except Exception as parse_error:
                self.logger.error(f"Failed to parse Gemini response: {parse_error}")
                self.logger.error(f"Raw response: {response.text}")
                return {"error": "Failed to parse AI response", "raw": response.text}
                
        except Exception as e:
            self.logger.error(f"Gemini generation failed: {e}")
            return {"error": str(e)}
    def enrich_matches(self, matches: List[Dict[str, Any]], prompt_template: str) -> List[Dict[str, Any]]:
        """
        Enriches a list of matches with AI insights.
        Modifies the list in-place and returns it.
        """
        if not self.api_key or not matches:
             return matches

        # Optimize data for token usage
        minified_matches = []
        for m in matches:
            minified_matches.append({
                "match_id": m.get("match_id"),
                "home": m.get("home_team"),
                "away": m.get("away_team"),
                "analysis": m.get("analysis"),
                "odds": m.get("odds")
            })

        try:
            # Append strict formatting rules to ensure JSON compatibility with user's prompt
            system_instruction = (
                "\n\nIMPORTANT SYSTEM INSTRUCTION:\n"
                "You MUST return the output as a valid JSON object where keys are the `match_id` of each match.\n"
                "The value for each key should be the object described in your instructions (Verdict, Reasoning, etc).\n"
                "Do not wrap in markdown code blocks if possible, just raw JSON.\n"
                "Example:\n"
                '{"match_id_123": {"verdict": "...", "reasoning": "..."}, "match_id_456": ...}'
            )
            
            full_prompt = f"{prompt_template}\n{system_instruction}\n\nINPUT DATA:\n{json.dumps(minified_matches, default=str)}"
            
            response = self.model.generate_content(
                full_prompt, 
                generation_config={"response_mime_type": "application/json"}
            )
            
            text = response.text
             # Cleanup markdown if present
            if "```json" in text:
                text = text.split("```json")[1].split("```")[0]
            elif "```" in text:
                text = text.split("```")[1].split("```")[0]
                
            insights_map = json.loads(text)
            
            # Map back to original objects
            for match in matches:
                mid = match.get("match_id")
                if mid in insights_map:
                    match["ai_insight"] = insights_map[mid]
            
            return matches
            
        except Exception as e:
            self.logger.error(f"Gemini enrichment failed: {e}")
            # Do not fail the whole request, just return matches without insight
            return matches
