"""
Verification script for QualificationCalculator.
"""
from src.domain.services.calculators.qualification_calculator import QualificationCalculator
from dataclasses import dataclass

# Mocks
@dataclass
class MockStats:
    avg_goals_scored: float = 0.0
    avg_goals_conceded: float = 0.0
    btts_rate: float = 0.0
    over_25_rate: float = 0.0
    
@dataclass
class MockTeamStats:
    last_5: MockStats
    venue_last_3: MockStats = None

@dataclass
class MockModelResult:
    btts: float = 0.0
    over_25: float = 0.0
    adjusted_probability: float = 0.0

@dataclass
class MockModels:
    btts: MockModelResult
    over_25: MockModelResult

@dataclass
class MockMarketConfidence:
    confidence: str = "LOW"

@dataclass
class MockMatchAnalysis:
    btts: MockMarketConfidence

@dataclass
class MockH2H:
    over_25_rate: float = 0.5
    h2h_reliability: float = 0.5

@dataclass
class MockAnalysis:
    match_id: str
    homeStats: MockTeamStats
    awayStats: MockTeamStats
    poisson: MockModelResult
    monte_carlo: MockModels
    match_analysis: MockMatchAnalysis
    h2h_last_5: MockH2H = None
    ai_analysis: dict = None

def verify():
    calc = QualificationCalculator()
    
    print("Running Qualification Verification...")
    
    # CASE 1: Perfect Match (Should Qualify for Both)
    perfect_match = MockAnalysis(
        match_id="PERFECT_MATCH",
        homeStats=MockTeamStats(
            last_5=MockStats(avg_goals_scored=2.5, btts_rate=0.8, over_25_rate=0.8, avg_goals_conceded=1.5),
            venue_last_3=MockStats(btts_rate=0.8, over_25_rate=0.8)
        ),
        awayStats=MockTeamStats(
            last_5=MockStats(avg_goals_scored=2.0, btts_rate=0.8, over_25_rate=0.8, avg_goals_conceded=1.5),
        ),
        poisson=MockModelResult(btts=0.7, over_25=0.7),
        monte_carlo=MockModels(
            btts=MockModelResult(adjusted_probability=0.7),
            over_25=MockModelResult(adjusted_probability=0.7)
        ),
        match_analysis=MockMatchAnalysis(btts=MockMarketConfidence("HIGH")),
        ai_analysis={"trap": ""},
        h2h_last_5=MockH2H(over_25_rate=0.8, h2h_reliability=0.5)
    )
    
    res = calc.calculate(perfect_match)
    print(f"Perfect Match: BTTS={res.qualified_btts} (Exp: True), Over2.5={res.qualified_over25} (Exp: True)")
    
    # CASE 2: Trap Active (Should Fail BTTS unless HIGH confidence - here HIGH)
    trap_match = MockAnalysis(
        match_id="TRAP_MATCH",
        homeStats=MockTeamStats(
            last_5=MockStats(avg_goals_scored=2.5, btts_rate=0.8, over_25_rate=0.8, avg_goals_conceded=1.5),
            venue_last_3=MockStats(btts_rate=0.8, over_25_rate=0.8)
        ),
        awayStats=MockTeamStats(
            last_5=MockStats(avg_goals_scored=2.0, btts_rate=0.8, over_25_rate=0.8, avg_goals_conceded=1.5),
        ),
        poisson=MockModelResult(btts=0.7, over_25=0.7),
        monte_carlo=MockModels(
            btts=MockModelResult(adjusted_probability=0.7),
            over_25=MockModelResult(adjusted_probability=0.7)
        ),
        match_analysis=MockMatchAnalysis(btts=MockMarketConfidence("LOW")), # Low confidence trigger fail
        ai_analysis={"trap": "Trap active"},
        h2h_last_5=MockH2H(over_25_rate=0.8, h2h_reliability=0.8)
    )
    
    res_trap = calc.calculate(trap_match)
    print(f"Trap Match: BTTS={res_trap.qualified_btts} (Exp: False) Reason: {res_trap.reason_btts}")
    
    # CASE 3: Low Poisson (Should Fail)
    poor_match = MockAnalysis(
        match_id="POOR_MATCH",
        homeStats=MockTeamStats(last_5=MockStats(avg_goals_scored=2.0, btts_rate=0.8, over_25_rate=0.4), venue_last_3=MockStats(btts_rate=0.8)),
        awayStats=MockTeamStats(last_5=MockStats(avg_goals_scored=2.0, btts_rate=0.8, over_25_rate=0.4)),
        poisson=MockModelResult(btts=0.4, over_25=0.4), # Low stats
        monte_carlo=MockModels(btts=MockModelResult(0.4), over_25=MockModelResult(0.4)),
        match_analysis=MockMatchAnalysis(btts=MockMarketConfidence("HIGH")),
        ai_analysis={},
        h2h_last_5=MockH2H(over_25_rate=0.4, h2h_reliability=0.5)
    )
    res_poor = calc.calculate(poor_match)
    print(f"Poor Match: BTTS={res_poor.qualified_btts} (Exp: False), Over2.5={res_poor.qualified_over25} (Exp: False) Reason: {res_poor.reason_over25}")

    # CASE 4: Detailed Over 2.5 Trap Check
    # High H2H reliability but low scoring history -> Should Fail
    o25_trap = MockAnalysis(
        match_id="O25_TRAP",
        homeStats=MockTeamStats(
            last_5=MockStats(avg_goals_scored=2.5, btts_rate=0.8, over_25_rate=0.8, avg_goals_conceded=1.5),
            venue_last_3=MockStats(btts_rate=0.8, over_25_rate=0.8)
        ),
        awayStats=MockTeamStats(
            last_5=MockStats(avg_goals_scored=2.0, btts_rate=0.8, over_25_rate=0.8, avg_goals_conceded=1.5),
        ),
        poisson=MockModelResult(btts=0.7, over_25=0.7),
        monte_carlo=MockModels(
            btts=MockModelResult(adjusted_probability=0.7),
            over_25=MockModelResult(adjusted_probability=0.7)
        ),
        match_analysis=MockMatchAnalysis(btts=MockMarketConfidence("HIGH")),
        ai_analysis={},
        h2h_last_5=MockH2H(over_25_rate=0.2, h2h_reliability=0.9) # High reliability, low rate
    )
    res_o25_trap = calc.calculate(o25_trap)
    print(f"O25 H2H Trap: Qualified={res_o25_trap.qualified_over25} (Exp: False) Reason: {res_o25_trap.reason_over25}")

if __name__ == "__main__":
    verify()
