namespace soccer_gpt_application.Services;

/// <summary>
/// Service to detect if a match is a known derby (rivalry match)
/// </summary>
public class DerbyService
{
    // Known English derby pairs - stored as normalized team name pairs
    // Both directions are checked (home vs away doesn't matter)
    private static readonly HashSet<(string, string)> DerbyPairs = new()
    {
        // Premier League derbies
        ("manchester united", "manchester city"),       // Manchester Derby
        ("man utd", "man city"),
        ("arsenal", "tottenham"),                       // North London Derby
        ("arsenal", "spurs"),
        ("liverpool", "everton"),                       // Merseyside Derby
        ("chelsea", "tottenham"),                       // London Derby
        ("chelsea", "arsenal"),                         // London Derby
        ("chelsea", "west ham"),                        // London Derby
        ("arsenal", "west ham"),                        // London Derby
        ("tottenham", "west ham"),                      // London Derby
        ("newcastle", "sunderland"),                    // Tyne-Wear Derby
        
        // Championship / Lower League derbies
        ("sheffield utd", "sheffield wed"),             // Steel City Derby
        ("sheffield united", "sheffield wednesday"),
        ("nottingham forest", "derby"),                 // East Midlands Derby
        ("nottm forest", "derby"),
        ("aston villa", "birmingham"),                  // Second City Derby
        ("aston villa", "wolves"),                      // West Midlands Derby
        ("wolverhampton", "west brom"),                 // Black Country Derby
        ("wolves", "west brom"),
        ("brighton", "crystal palace"),                 // M23 Derby
        ("southampton", "portsmouth"),                  // South Coast Derby
        ("burnley", "blackburn"),                       // East Lancashire Derby
        ("leeds", "manchester united"),                 // Roses Derby
        ("leeds", "man utd"),
        ("bristol city", "bristol rovers"),             // Bristol Derby
        ("ipswich", "norwich"),                         // East Anglian Derby
        ("stoke", "port vale"),                         // Potteries Derby
        ("bury", "bolton"),                             // Manchester Derby (lower)
        ("huddersfield", "leeds"),                      // West Yorkshire Derby
        ("barnsley", "sheffield utd"),                  // South Yorkshire Derby
        ("barnsley", "sheffield wed"),
        ("swindon", "oxford"),                          // M4 Derby
        ("exeter", "plymouth"),                         // Devon Derby
        ("carlisle", "barrow"),                         // Cumbrian Derby
        ("stockport", "oldham"),                        // Greater Manchester Derby
        ("wrexham", "shrewsbury"),                      // Border Derby
        ("blackpool", "preston"),                       // Lancashire Derby
        ("wigan", "bolton"),                            // Greater Manchester Derby
    };

    /// <summary>
    /// Check if a match is a known derby based on team names
    /// </summary>
    public static bool IsDerby(string homeTeam, string awayTeam)
    {
        if (string.IsNullOrEmpty(homeTeam) || string.IsNullOrEmpty(awayTeam))
            return false;

        var home = NormalizeName(homeTeam);
        var away = NormalizeName(awayTeam);

        // Check both directions
        return DerbyPairs.Contains((home, away)) || DerbyPairs.Contains((away, home));
    }

    /// <summary>
    /// Check if a match is a known derby based on team API IDs
    /// </summary>
    public static bool IsDerbyByApiId(int homeTeamApiId, int awayTeamApiId)
    {
        // Map API IDs to team names for known derbies
        var apiIdMap = new Dictionary<int, string>
        {
            // Premier League
            { 33, "manchester united" },
            { 50, "manchester city" },
            { 42, "arsenal" },
            { 47, "tottenham" },
            { 40, "liverpool" },
            { 45, "everton" },
            { 49, "chelsea" },
            { 48, "west ham" },
            { 34, "newcastle" },
            { 71, "sunderland" },
            // Championship
            { 62, "sheffield utd" },
            { 64, "sheffield wed" },
            { 65, "nottingham forest" },
            { 67, "derby" },
            { 66, "aston villa" },
            { 38, "wolves" },
            { 60, "west brom" },
            { 51, "brighton" },
            { 52, "crystal palace" },
            { 41, "southampton" },
            // Others
            { 44, "burnley" },
            { 59, "blackburn" },
            { 63, "leeds" },
        };

        if (!apiIdMap.TryGetValue(homeTeamApiId, out var homeName) ||
            !apiIdMap.TryGetValue(awayTeamApiId, out var awayName))
        {
            return false;
        }

        return DerbyPairs.Contains((homeName, awayName)) || DerbyPairs.Contains((awayName, homeName));
    }

    private static string NormalizeName(string name)
    {
        return name
            .ToLowerInvariant()
            .Replace(" fc", "")
            .Replace(" afc", "")
            .Replace("hotspur", "")
            .Replace("wanderers", "")
            .Replace("rovers", "")
            .Replace("albion", "")
            .Replace("county", "")
            .Trim();
    }
}
