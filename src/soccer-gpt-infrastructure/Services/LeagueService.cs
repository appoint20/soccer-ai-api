
using soccer_gpt_application.Interfaces;

namespace soccer_gpt_infrastructure.Services;

public class LeagueService : ILeagueService
{
    private static readonly Dictionary<string, string> LeagueMappings = new()
    {
        { "E0", "Premier League" },
        { "E1", "Championship" },
        { "D1", "Bundesliga" },
        { "I1", "Serie A" },
        { "SP1", "La Liga" },
        { "F1", "Ligue 1" }
    };

    public string GetLeagueNameFromCode(string code)
    {
        return LeagueMappings.TryGetValue(code, out var name) ? name : code;
    }

    public bool IsLeagueSupported(string code)
    {
        return LeagueMappings.ContainsKey(code);
    }
}
