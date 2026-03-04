namespace SoccerAi.Application.Models;

public class GeminiBilingualResult
{
    public int FixtureId { get; set; }
    public string Recommendation { get; set; } = "";
    public int Confidence { get; set; }
    public bool TrapDetected { get; set; }

    public GeminiLanguageBlock En { get; set; } = default!;
    public GeminiLanguageBlock De { get; set; } = default!;
}

public class GeminiLanguageBlock
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
    public string HomeWin { get; set; } = "";
    public string AwayWin { get; set; } = "";
}
