namespace SoccerAi.Application.Models;

public sealed class GeminiBilingualAnalysis
{
    public GeminiAnalysis En { get; init; } = new();
    public GeminiAnalysis De { get; init; } = new();
}
