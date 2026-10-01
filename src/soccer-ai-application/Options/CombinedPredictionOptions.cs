namespace SoccerAi.Application.Options;

public sealed class CombinedPredictionOptions
{
    public const string SectionName = "CombinedPrediction";
    public double HistoricalWeight { get; set; } = .45;
    public double MlWeight { get; set; } = .30;
    public double ProviderWeight { get; set; } = .15;
    public double AiWeight { get; set; } = .10;
    public string AiModel { get; set; } = "nvidia/nemotron-3-super-120b-a12b:free";
}
