"""
FastAPI microservice — Soccer AI Python inference layer.

Exposes three endpoints that exactly replace the legacy AI service:
  POST /analyze        → AnalyzeBatchAsync
  POST /parse-intent   → ParseChatIntentAsync
  POST /build-combinations → BuildCombinationsAsync
  POST /nlp/parse      → Deterministic Engine Intent Parser

Model selection via header: X-AI-Model: mistral (default) | llama3
"""
from __future__ import annotations

import json
import logging
import os
os.environ["TOKENIZERS_PARALLELISM"] = "false"
from contextlib import asynccontextmanager

from fastapi import FastAPI, Header, HTTPException, Request
from fastapi.middleware.cors import CORSMiddleware
from fastapi.responses import JSONResponse

import models as m
import prompts
from inference import run_inference, parse_json_output
from engine import CombinationEngine

logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s [%(levelname)s] %(name)s: %(message)s",
)
logger = logging.getLogger("ai_service")


# ─── App setup ────────────────────────────────────────────────────────────────

@asynccontextmanager
async def lifespan(app: FastAPI):
    logger.info("Soccer AI Python service starting up…")
    logger.info(f"Default model: {os.environ.get('DEFAULT_MODEL', 'mistral')}")
    
    # Preload the default model to avoid timeout on first request
    try:
        from inference import _load_pipeline
        logger.info("[Lifespan] Preloading Mistral model...")
        _load_pipeline("mistral")
        logger.info("[Lifespan] Preloading complete.")
    except Exception as e:
        logger.error(f"[Lifespan] Failed to preload model: {e}")
        
    yield
    logger.info("Soccer AI Python service shutting down.")


app = FastAPI(
    title="Soccer AI — Local Inference Service",
    description="Replaces legacy AI providers with Mistral-7B / LLaMA-3 running locally via HuggingFace Transformers.",
    version="1.0.0",
    lifespan=lifespan,
)

app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_methods=["*"],
    allow_headers=["*"],
)


# ─── Helpers ──────────────────────────────────────────────────────────────────

def _resolve_model(header_value: str | None) -> str:
    """Resolve which model to use from the request header."""
    default = os.environ.get("DEFAULT_MODEL", "mistral")
    if header_value is None:
        return default
    key = header_value.strip().lower()
    if key not in ("mistral", "llama3"):
        raise HTTPException(
            status_code=400,
            detail=f"Unknown model '{header_value}'. Allowed: mistral, llama3",
        )
    return key


def _fallback_intent(query: str) -> m.ChatCombinationIntent:
    """Rule-based fallback identical to FallbackParseIntent in C#."""
    import re as _re
    q = query.lower()
    intent = m.ChatCombinationIntent()

    if "three" in q or "3" in q:
        intent.min_matches = 3
        intent.max_matches = 3
    elif "two" in q or "2" in q:
        intent.min_matches = 2
        intent.max_matches = 2

    if "win" in q or "victory" in q:
        intent.preferred_markets += ["HomeWin", "AwayWin"]
    if "btts" in q or "both team" in q:
        intent.preferred_markets.append("BTTS")
    if "over" in q or "2.5" in q:
        intent.preferred_markets.append("Over25")

    match = _re.search(r"\d+\.?\d*", q)
    if match:
        odds = float(match.group(0))
        if odds > 1.0:
            intent.min_total_odds = odds

    intent.reasoning = "Parsed using rule-based fallback logic."
    return intent


# ─── Endpoints ────────────────────────────────────────────────────────────────

@app.get("/health")
async def health():
    return {"status": "healthy", "service": "soccer-ai-python"}


@app.post("/analyze", response_model=m.AnalyzeBatchResponse)
async def analyze_batch(
    request: m.AnalyzeBatchRequest,
    x_ai_model: str | None = Header(default=None, alias="X-AI-Model"),
):
    """
    Replaces legacy IAiAnalysisService.AnalyzeBatchAsync.
    Receives a batch of match data and returns per-fixture analysis
    with trap detection, recommendation, and bilingual summaries.
    """
    model_key = _resolve_model(x_ai_model)

    if not request.items:
        return m.AnalyzeBatchResponse(results=[])

    # Batch into groups of 10 (same as Gemini implementation)
    BATCH_SIZE = 10
    all_results: list[m.AnalysisResult] = []

    for i in range(0, len(request.items), BATCH_SIZE):
        chunk = request.items[i : i + BATCH_SIZE]
        user_content = (
            "MATCH BATCH DATA (JSON):\n"
            + json.dumps([item.model_dump(by_alias=True) for item in chunk], indent=2)
        )

        try:
            raw = run_inference(
                system_prompt=prompts.MATCH_ANALYSIS_SYSTEM_PROMPT,
                user_content=user_content,
                model_key=model_key,
                max_new_tokens=6000,
                temperature=0.7,
                do_sample=True,
            )
            parsed = parse_json_output(raw)

            for result_dict in parsed:
                try:
                    all_results.append(m.AnalysisResult(**result_dict))
                except Exception as e:
                    logger.warning(f"[analyze] Skipping malformed result: {e}")

        except Exception as e:
            logger.error(f"[analyze] Inference failed for chunk {i}: {e}")
            # Return empty results for this chunk — don't crash the whole request
            continue

    return m.AnalyzeBatchResponse(results=all_results)


@app.post("/parse-intent", response_model=m.ChatCombinationIntent)
async def parse_intent(
    request: m.ParseIntentRequest,
    x_ai_model: str | None = Header(default=None, alias="X-AI-Model"),
):
    """
    Replaces legacy IAiAnalysisService.ParseChatIntentAsync.
    Converts a natural language user query into a structured ChatCombinationIntent.
    """
    model_key = _resolve_model(x_ai_model)

    if not request.query.strip():
        raise HTTPException(status_code=400, detail="Query cannot be empty.")

    try:
        raw = run_inference(
            system_prompt=prompts.PARSE_INTENT_SYSTEM_PROMPT,
            user_content=f'USER QUERY: "{request.query}"',
            model_key=model_key,
            max_new_tokens=256,
        )
        parsed = parse_json_output(raw)
        intent = m.ChatCombinationIntent(**parsed)
        logger.info(f"[parse-intent] Parsed intent: {intent.model_dump()}")
        return intent

    except Exception as e:
        logger.error(f"[parse-intent] Model failed, using fallback: {e}")
        return _fallback_intent(request.query)


@app.post("/build-combinations", response_model=m.BuildCombinationsResponse)
async def build_combinations(
    request: m.BuildCombinationsRequest,
    x_ai_model: str | None = Header(default=None, alias="X-AI-Model"),
):
    """
    Replaces legacy IAiAnalysisService.BuildCombinationsAsync.
    Receives analysed matches and asks the model to build 2-4 DOUBLE/TREBLE combos.
    """
    model_key = _resolve_model(x_ai_model)

    if not request.candidates:
        return m.BuildCombinationsResponse(combinations=[])

    user_content = (
        "MATCH BATCH DATA (JSON):\n"
        + json.dumps(
            [c.model_dump() for c in request.candidates], indent=2
        )
    )

    try:
        raw = run_inference(
            system_prompt=prompts.BUILD_COMBINATIONS_SYSTEM_PROMPT,
            user_content=user_content,
            model_key=model_key,
            max_new_tokens=3000,
        )
        parsed = parse_json_output(raw)

        combinations: list[m.CombinationDto] = []
        used_fixture_ids: set[int] = set()
        candidate_ids = {c.Id for c in request.candidates}

        for combo_dict in parsed:
            try:
                combo = m.CombinationDto(**combo_dict)
            except Exception:
                continue

            fixture_ids = [mat.fixtureId for mat in combo.matches]

            # Validate: all fixtures exist and none already used
            if not all(fid in candidate_ids for fid in fixture_ids):
                continue
            if any(fid in used_fixture_ids for fid in fixture_ids):
                continue
            if not (2 <= len(combo.matches) <= 3):
                continue

            used_fixture_ids.update(fixture_ids)
            combinations.append(combo)

        return m.BuildCombinationsResponse(combinations=combinations)

    except Exception as e:
        logger.error(f"[build-combinations] Inference failed: {e}")
        return m.BuildCombinationsResponse(combinations=[])


@app.post("/nlp/parse", response_model=m.NLPIntent)
async def nlp_parse(
    request: m.ParseIntentRequest,
    x_ai_model: str | None = Header(default=None, alias="X-AI-Model"),
):
    """
    Step 1: NLP Intent Parser.
    Converts natural language to the deterministic engine schema.
    """
    model_key = _resolve_model(x_ai_model)
    try:
        raw = run_inference(
            system_prompt=prompts.DETERMINISTIC_PARSE_PROMPT,
            user_content=f'USER QUERY: "{request.query}"',
            model_key=model_key,
            max_new_tokens=256,
        )
        parsed = parse_json_output(raw)
        return m.NLPIntent(**parsed)
    except Exception as e:
        logger.error(f"[nlp/parse] NLP Parsing failed: {e}")
        return m.NLPIntent()


@app.post("/api/combinations", response_model=m.DeterministicCombinationResponse)
async def api_combinations(
    request: m.CombinationRequest,
    x_ai_model: str | None = Header(default=None, alias="X-AI-Model"),
):
    """
    Step 8: Deterministic API Flow.
    1. Parse intent via AI.
    2. Fetch match data (mocked or passed).
    3. Run deterministic engine.
    """
    model_key = _resolve_model(x_ai_model)

    # 1. NLP Layer: Convert natural language to structured intent
    try:
        raw = run_inference(
            system_prompt=prompts.DETERMINISTIC_PARSE_PROMPT,
            user_content=f'USER QUERY: "{request.query}"',
            model_key=model_key,
            max_new_tokens=256,
        )
        parsed = parse_json_output(raw)
        intent = m.NLPIntent(**parsed)
    except Exception as e:
        logger.error(f"[api/combinations] NLP Parsing failed: {e}")
        # Default fallback intent if NLP fails
        intent = m.NLPIntent()

    # 2. Data Layer: Fetch match data
    # (Using request.match_data if provided, else use static mock)
    match_candidates = request.match_data if request.match_data else _get_mock_matches()

    # 3. Combination Engine: Deterministic generation
    engine = CombinationEngine(match_candidates, intent)
    results = engine.run()

    return m.DeterministicCombinationResponse(combinations=results)


def _get_mock_matches() -> list[m.MatchData]:
    """Sample dataset for testing (Step 10)."""
    return [
        m.MatchData(
            match_id="101", home_team="Arsenal", away_team="Chelsea", league="Premier League",
            odds=m.MatchOdds(home_win=1.85, away_win=3.40, draw=3.20),
            probabilities=m.MatchProbabilities(home_win=0.72, away_win=0.15, draw=0.13),
            form=m.MatchForm(home=0.8, away=0.4)
        ),
        m.MatchData(
            match_id="102", home_team="Real Madrid", away_team="Getafe", league="La Liga",
            odds=m.MatchOdds(home_win=1.35, away_win=8.00, draw=5.00),
            probabilities=m.MatchProbabilities(home_win=0.85, away_win=0.05, draw=0.10),
            form=m.MatchForm(home=0.9, away=0.3)
        ),
        m.MatchData(
            match_id="103", home_team="Bayern Munich", away_team="Bochum", league="Bundesliga",
            odds=m.MatchOdds(home_win=1.20, away_win=12.00, draw=7.00),
            probabilities=m.MatchProbabilities(home_win=0.90, away_win=0.02, draw=0.08),
            form=m.MatchForm(home=0.95, away=0.2)
        ),
        m.MatchData(
            match_id="104", home_team="Juventus", away_team="Lazio", league="Serie A",
            odds=m.MatchOdds(home_win=2.10, away_win=3.20, draw=3.10),
            probabilities=m.MatchProbabilities(home_win=0.65, away_win=0.20, draw=0.15),
            form=m.MatchForm(home=0.7, away=0.6)
        )
    ]


# ─── Error handler ────────────────────────────────────────────────────────────

@app.exception_handler(Exception)
async def global_error_handler(request: Request, exc: Exception):
    logger.error(f"Unhandled error on {request.url}: {exc}", exc_info=True)
    return JSONResponse(
        status_code=500,
        content={"error": str(exc), "path": str(request.url)},
    )


# ─── Entry point ──────────────────────────────────────────────────────────────

if __name__ == "__main__":
    import uvicorn
    uvicorn.run(
        "main:app",
        host="0.0.0.0",
        port=int(os.environ.get("PORT", 8100)),
        reload=False,
        workers=1,   # Single worker — models are not fork-safe
    )
