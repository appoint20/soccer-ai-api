"""
These must match GeminiBatchItem, ChatCombinationIntent, MatchAnalysis, etc.
Added: NLPIntent, MatchData, CombinationResult for deterministic engine.
"""
from __future__ import annotations
from typing import Optional
from pydantic import BaseModel, Field


# ─── ANALYZE BATCH ───────────────────────────────────────────────────────────

class AiBatchItem(BaseModel):
    fixture_id: int = Field(alias="fixtureId")
    league: str = Field(default="")
    home_team: str = Field(alias="homeTeam", default="")
    away_team: str = Field(alias="awayTeam", default="")
    home_goals_avg: float = Field(alias="homeGoalAvg", default=0.0)
    away_goals_avg: float = Field(alias="awayGoalAvg", default=0.0)
    home_win_prob: float = Field(alias="homeWinProb", default=0.0)
    draw_prob: float = Field(alias="drawProb", default=0.0)
    away_win_prob: float = Field(alias="awayWinProb", default=0.0)
    btts_prob: float = Field(alias="bttsProb", default=0.0)
    over25_prob: float = Field(alias="over25Prob", default=0.0)
    home_form: str = Field(alias="homeForm", default="")
    away_form: str = Field(alias="awayForm", default="")
    home_elo: float = Field(alias="homeElo", default=0.0)
    away_elo: float = Field(alias="awayElo", default=0.0)

    class Config:
        populate_by_name = True


class MarketSummaries(BaseModel):
    btts: str = ""
    over25: str = ""
    under25: str = ""
    homeWin: str = ""
    awayWin: str = ""


class LanguageBlock(BaseModel):
    predictionReason: str = ""
    analysis: str = ""
    trapReason: Optional[str] = None
    consensusEvaluation: str = ""
    summaries: MarketSummaries = Field(default_factory=MarketSummaries)


class AnalysisResult(BaseModel):
    fixtureId: int
    recommendation: str  # "BTTS" | "Over 2.5 Goals" | "Under 2.5 Goals" | "Match Winner (Home)" | "Match Winner (Away)" | "Avoid"
    confidence: float
    trapDetected: bool
    en: LanguageBlock
    de: LanguageBlock


class AnalyzeBatchRequest(BaseModel):
    items: list[AiBatchItem]


class AnalyzeBatchResponse(BaseModel):
    results: list[AnalysisResult]


# ─── PARSE INTENT ────────────────────────────────────────────────────────────

class ParseIntentRequest(BaseModel):
    query: str


class TimeConstraint(BaseModel):
    start_time: Optional[str] = None  # "HH:mm:ss"
    end_time: Optional[str] = None


class MarketGroup(BaseModel):
    match_count: int = 2
    markets: list[str] = Field(default_factory=list)


class ChatCombinationIntent(BaseModel):
    min_matches: int = 2
    max_matches: int = 3
    time_frame: Optional[TimeConstraint] = None
    min_total_odds: float = 1.0
    min_selection_odds: float = 1.0
    max_same_league: int = 1
    preferred_leagues: list[str] = Field(default_factory=list)
    market_groups: list[MarketGroup] = Field(default_factory=list)
    preferred_markets: list[str] = Field(default_factory=list)
    strategy: str = "balanced"
    reasoning: str = ""


# ─── BUILD COMBINATIONS ───────────────────────────────────────────────────────

class PredictionOutcome(BaseModel):
    Probability: float = 0.0


class PredictionResponse(BaseModel):
    HomeWin: PredictionOutcome = Field(default_factory=PredictionOutcome)
    AwayWin: PredictionOutcome = Field(default_factory=PredictionOutcome)
    Draw: PredictionOutcome = Field(default_factory=PredictionOutcome)
    BTTS: PredictionOutcome = Field(default_factory=PredictionOutcome)
    Over25: PredictionOutcome = Field(default_factory=PredictionOutcome)


class AiAnalysisNested(BaseModel):
    Recommendation: str = ""
    Confidence: float = 0.0
    Reasoning: str = ""
    IsTrap: bool = False
    TrapReason: str = ""


class MatchAnalysis(BaseModel):
    Id: int
    League: str = ""
    HomeTeam: str = ""
    AwayTeam: str = ""
    OddsHomeWin: float = 0.0
    OddsAwayWin: float = 0.0
    OddsDraw: float = 0.0
    OddsOver25: float = 0.0
    OddsBttsYes: float = 0.0
    Prediction: Optional[PredictionResponse] = None
    Ai: Optional[AiAnalysisNested] = None


class CombinationMatchDto(BaseModel):
    fixtureId: int
    league: str = ""
    homeTeam: str = ""
    awayTeam: str = ""
    selection: str = ""
    odds: float = 0.0


class CombinationDto(BaseModel):
    combinationId: int
    type: str  # "DOUBLE" | "TREBLE"
    totalOdds: float
    matches: list[CombinationMatchDto]
    reason: str = ""


class BuildCombinationsRequest(BaseModel):
    candidates: list[MatchAnalysis]


class BuildCombinationsResponse(BaseModel):
    combinations: list[CombinationDto]


# ─── DETERMINISTIC ENGINE (STEPS 1, 2, 7) ───────────────────────────────────

class NLPFilters(BaseModel):
    leagues: list[str] = Field(default_factory=list)
    min_probability: float = 0.5


class NLPIntent(BaseModel):
    num_matches: list[int] = [2, 3]
    bet_type: str = "win"
    min_odds: float = 1.0
    filters: NLPFilters = Field(default_factory=NLPFilters)


class MatchOdds(BaseModel):
    home_win: float
    away_win: float
    draw: float


class MatchProbabilities(BaseModel):
    home_win: float
    away_win: float
    draw: float


class MatchForm(BaseModel):
    home: float
    away: float


class MatchData(BaseModel):
    match_id: str
    home_team: str
    away_team: str
    league: str
    odds: MatchOdds
    probabilities: MatchProbabilities
    form: MatchForm


class ScoredCombinationMatch(BaseModel):
    match_id: str
    home_team: str
    away_team: str
    league: str
    selection: str
    odds: float
    probability: float


class ScoredCombination(BaseModel):
    matches: list[ScoredCombinationMatch]
    total_odds: float
    avg_probability: float
    score: float
    reasoning: str


class DeterministicCombinationResponse(BaseModel):
    combinations: list[ScoredCombination]


class CombinationRequest(BaseModel):
    query: str
    match_data: Optional[list[MatchData]] = None  # Allow passing data in request for easier testing
