namespace SoccerAi.Infrastructure.Options;

public class GeminiOptions: ApiBaseOption
{
    public const string SectionName = "Gemini";
    public string Model { get; set; } = "gemini-1.5-flash"; // Default to economical model
}
