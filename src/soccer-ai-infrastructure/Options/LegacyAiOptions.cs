namespace SoccerAi.Infrastructure.Options;

public class LegacyAiOptions: ApiBaseOption
{
    public const string SectionName = "LegacyAi";
    public string Model { get; set; } = "gemini-1.5-flash"; // Keep the model string as is for functionality if used, but rename internal branding
}
