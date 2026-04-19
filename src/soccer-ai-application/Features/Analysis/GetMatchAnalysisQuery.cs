using Mediator.Net.Contracts;
using SoccerAi.Application.Models;

namespace SoccerAi.Application.Features.Analysis;

public class GetMatchAnalysisQuery : IRequest
{
    public DateTimeOffset? Date { get; set; }
    public string Language { get; set; } = "en";
    public int? Page { get; set; }
    public int? PageSize { get; set; }
}

public class GetMatchAnalysisResponse : IResponse
{
    public List<MatchAnalysis> Matches { get; set; } = new();
    public int TotalCount { get; set; }
    public AnalysisSummary? Summary { get; set; }
}

public class AnalysisSummary
{
    public int TotalMatches { get; set; }
    public int CorrectMatches { get; set; }
    public double AccuracyRate { get; set; }
}
