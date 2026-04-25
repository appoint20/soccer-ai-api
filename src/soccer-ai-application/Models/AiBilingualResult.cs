namespace SoccerAi.Application.Models;

public class AiBilingualResult
{
    public int FixtureId { get; set; }
    public string Recommendation { get; set; } = "";
    public int Confidence { get; set; }
    public bool TrapDetected { get; set; }

    // Unified AI Decision Layer flags
    public bool Over25Qualified { get; set; }
    public bool BttsQualified { get; set; }
    public bool Under25Qualified { get; set; }
    public bool Goals23Qualified { get; set; }
    public bool HomeWinQualified { get; set; }
    public bool AwayWinQualified { get; set; }
    public string BestBet { get; set; } = "";
    public int OverallConfidence { get; set; }

    public AiLanguageBlock En { get; set; } = default!;
    public AiLanguageBlock De { get; set; } = default!;
}

public class AiLanguageBlock
{
    public string PredictionReason { get; set; } = "";
    public string Analysis { get; set; } = "";
    public string? TrapReason { get; set; }
    public string ConsensusEvaluation { get; set; } = "";
    public MarketSummaries Summaries { get; set; } = default!;
}

public class MarketSummaries
{
    public string Btts { get; set; } = "";
    public string Over25 { get; set; } = "";
    public string Under25 { get; set; } = "";
    public string Goals23 { get; set; } = "";
    public string HomeWin { get; set; } = "";
    public string AwayWin { get; set; } = "";
}
