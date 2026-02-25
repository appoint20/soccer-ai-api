using Mediator.Net.Contracts;
using soccer_gpt_application.Models;

namespace soccer_gpt_application.Features.Analysis;

public class GetMatchAnalysisResponse : IResponse
{
    public List<MatchAnalysis> Matches { get; set; } = [];
    public AnalysisSummary? Summary { get; set; }
}

public class AnalysisSummary
{
    public int TotalMatches { get; set; }
    public int CorrectMatches { get; set; }
    public double AccuracyRate { get; set; }
}
