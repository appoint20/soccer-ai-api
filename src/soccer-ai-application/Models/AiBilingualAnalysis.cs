namespace SoccerAi.Application.Models;

public sealed class AiBilingualAnalysis
{
    public AiAnalysisDto En { get; init; } = new();
    public AiAnalysisDto De { get; init; } = new();
}
