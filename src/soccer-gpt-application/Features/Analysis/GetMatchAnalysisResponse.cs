using Mediator.Net.Contracts;
using soccer_gpt_application.Models;

namespace soccer_gpt_application.Features.Analysis;

public class GetMatchAnalysisResponse : IResponse
{
    public List<MatchAnalysis> Matches { get; set; } = [];
}
