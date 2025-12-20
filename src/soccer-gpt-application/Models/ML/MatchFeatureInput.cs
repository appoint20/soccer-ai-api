
using Microsoft.ML.Data;

namespace soccer_gpt_application.Models.ML;

public class MatchFeatureInput
{
    // === MATCH CONTEXT ===
    [LoadColumn(0)] public float Season { get; set; }
    [LoadColumn(1)] public float LeagueId { get; set; }
    [LoadColumn(2)] public float Round { get; set; }
    [LoadColumn(3)] public float HomeDaysRest { get; set; }
    [LoadColumn(4)] public float AwayDaysRest { get; set; }
    [LoadColumn(5)] public bool HomePlayedEuropeLast7d { get; set; }
    [LoadColumn(6)] public bool AwayPlayedEuropeLast7d { get; set; }

    // === GOALS & DEFENSE (ROLLING) ===
    [LoadColumn(7)] public float HomeGoalsForAvg5 { get; set; }
    [LoadColumn(8)] public float HomeGoalsAgainstAvg5 { get; set; }
    [LoadColumn(9)] public float HomeGoalsForAvg10 { get; set; }
    [LoadColumn(10)] public float HomeGoalsAgainstAvg10 { get; set; }
    
    [LoadColumn(11)] public float AwayGoalsForAvg5 { get; set; }
    [LoadColumn(12)] public float AwayGoalsAgainstAvg5 { get; set; }
    [LoadColumn(13)] public float AwayGoalsForAvg10 { get; set; }
    [LoadColumn(14)] public float AwayGoalsAgainstAvg10 { get; set; }

    // === WIN/LOSS/DRAW RATES ===
    [LoadColumn(52)] public float HomeWinRate5 { get; set; }
    [LoadColumn(53)] public float HomeWinRate10 { get; set; }
    [LoadColumn(54)] public float HomeLossRate5 { get; set; }
    [LoadColumn(55)] public float HomeLossRate10 { get; set; }
    
    [LoadColumn(56)] public float AwayWinRate5 { get; set; }
    [LoadColumn(57)] public float AwayWinRate10 { get; set; }
    [LoadColumn(58)] public float AwayLossRate5 { get; set; }
    [LoadColumn(59)] public float AwayLossRate10 { get; set; }
    
    [LoadColumn(15)] public float HomeCleanSheetRate10 { get; set; }
    [LoadColumn(16)] public float AwayCleanSheetRate10 { get; set; }
    [LoadColumn(17)] public float HomeFailedToScoreRate10 { get; set; }
    [LoadColumn(18)] public float AwayFailedToScoreRate10 { get; set; }

    // === BTTS SPECIFIC (ROLLING) ===
    [LoadColumn(47)] public float HomeBTTSFreq5 { get; set; }
    [LoadColumn(48)] public float HomeBTTSFreq10 { get; set; }
    [LoadColumn(49)] public float AwayBTTSFreq5 { get; set; }
    [LoadColumn(50)] public float AwayBTTSFreq10 { get; set; }

    // === DRAW & STALEMATE PROPENSITY ===
    [LoadColumn(19)] public float HomeDrawRate5 { get; set; }
    [LoadColumn(20)] public float HomeDrawRate10 { get; set; }
    [LoadColumn(21)] public float AwayDrawRate5 { get; set; }
    [LoadColumn(22)] public float AwayDrawRate10 { get; set; }
    [LoadColumn(23)] public float CombinedDrawRate10 { get; set; }

    // === HEAD-TO-HEAD (AGGREGATED ONLY) ===
    [LoadColumn(24)] public float H2HMatchesCount { get; set; }
    [LoadColumn(25)] public float H2HAvgTotalGoals { get; set; }
    [LoadColumn(26)] public float H2HUnder25Rate { get; set; }
    [LoadColumn(27)] public float H2HZeroZeroRate { get; set; }
    [LoadColumn(28)] public float H2HTimeDecayWeight { get; set; }

    // === MARKET-DERIVED (NO ADVICE) ===
    [LoadColumn(29)] public float OddsOver15 { get; set; }
    [LoadColumn(30)] public float OddsOver25 { get; set; }
    [LoadColumn(31)] public float OddsDraw { get; set; }
    [LoadColumn(32)] public float OddsOver15ImpliedProb { get; set; }
    [LoadColumn(33)] public float OddsOver25ImpliedProb { get; set; }
    [LoadColumn(34)] public float BookmakerGoalExpectation { get; set; }

    // === ENGINEERED TRAP SIGNALS ===
    [LoadColumn(35)] public bool BothTeamsDefensive { get; set; }
    [LoadColumn(36)] public bool BothTeamsLowScoring { get; set; }
    [LoadColumn(37)] public bool BothTeamsPoorForm { get; set; }
    [LoadColumn(38)] public bool HighDrawBias { get; set; }
    [LoadColumn(39)] public bool EuropeFatigueTrap { get; set; }
    [LoadColumn(40)] public float OddsGoalTrapScore { get; set; }

    // === ADVANCED FEATURES (PYTHON V4 PORT) ===
    [LoadColumn(60)] public float RefereeAvgGoals { get; set; }
    [LoadColumn(61)] public float RefereeOver25Rate { get; set; }
    [LoadColumn(62)] public float HomeDaysSinceEurope { get; set; }
    [LoadColumn(63)] public float AwayDaysSinceEurope { get; set; }
    [LoadColumn(64)] public float HomeDaysUntilEurope { get; set; }
    [LoadColumn(65)] public float AwayDaysUntilEurope { get; set; }

    // === TARGET VARIABLES (LABELS) ===
    // These are what we try to predict during training.
    // They are typically 0/false during inference.
    [LoadColumn(41)] public float LabelTotalGoals { get; set; }
    [LoadColumn(42)] public bool LabelIsOver15 { get; set; }
    [LoadColumn(43)] public bool LabelIsOver25 { get; set; }
    [LoadColumn(44)] public bool LabelIsBTTS { get; set; }
    [LoadColumn(85)] public bool LabelHomeWin { get; set; }
    [LoadColumn(45)] public bool LabelIsDraw { get; set; }
    [LoadColumn(46)] public bool LabelIsZeroZero { get; set; }
    [LoadColumn(51)] public bool LabelIsLowGoalTrap { get; set; }
}

