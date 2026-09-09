namespace SoccerAi.Application.Services;

/// <summary>
/// The one place an API-Football league id becomes a display name.
/// </summary>
/// <remarks>
/// There were two of these, and they disagreed: one called ids 34, 43, 46 and
/// 154 "National League", the other "English National League", and a fifth id
/// (5) was mapped to the same name again. Names are how the app groups the
/// board into sections, so several ids sharing one name silently merges
/// unrelated competitions into a single league on screen — and two tables that
/// drift mean the same fixture can be labelled differently by two endpoints.
///
/// Only ids this project has actually seen data for are listed. An unknown id
/// renders as "League {id}" rather than being guessed at: a wrong league name
/// is worse than an obviously missing one, because nobody reports it.
/// </remarks>
public static class LeagueCatalog
{
    private static readonly Dictionary<int, string> Names = new()
    {
        // England
        [39] = "Premier League",
        [40] = "Championship",
        [41] = "League One",
        [42] = "League Two",
        [43] = "National League",
        // Germany
        [78] = "Bundesliga",
        [79] = "2. Bundesliga",
        [80] = "3. Liga",
        // Spain
        [140] = "La Liga",
        [141] = "La Liga 2",
        // Italy
        [135] = "Serie A",
        [136] = "Serie B",
        // France
        [61] = "Ligue 1",
        [62] = "Ligue 2",
        // European cups (Tier2 — synced only when IncludeTier2 is on)
        [2] = "Champions League",
        [3] = "Europa League",
        [848] = "Conference League",
    };

    /// <summary>Display name for a league id, or "League {id}" when unknown.</summary>
    public static string Name(int leagueId) =>
        Names.TryGetValue(leagueId, out var name) ? name : $"League {leagueId}";

    /// <summary>True when the id has a real name rather than a placeholder.</summary>
    public static bool IsKnown(int leagueId) => Names.ContainsKey(leagueId);
}
