using Mediator.Net.Contracts;

namespace soccer_gpt_application.Features.Analysis;

public class GetMatchAnalysisQuery : IRequest
{
    public DateTime Date { get; set; }
    public string Language { get; set; } = "en";
}
