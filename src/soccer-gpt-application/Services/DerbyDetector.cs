namespace soccer_gpt_application.Services;

/// <summary>
/// Utility for detecting derby matches based on known rivalries
/// </summary>
public static class DerbyDetector
{
    // Known derby pairs (team API IDs)
    private static readonly HashSet<(int, int)> DerbyPairs =
    [
        // England - Premier League
        (33, 34),    // Manchester United vs Manchester City
        (40, 42),    // Arsenal vs Chelsea
        (40, 47),    // Arsenal vs Tottenham (North London Derby)
        (42, 47),    // Chelsea vs Tottenham
        (39, 45),    // Wolves vs West Brom (Black Country Derby)
        (50, 46),    // Liverpool vs Everton (Merseyside Derby)
        (34, 50),    // Manchester City vs Liverpool
        (33, 50),    // Manchester United vs Liverpool
        (66, 36),    // Aston Villa vs Birmingham (Second City Derby)
        (49, 48),    // Sheffield United vs Sheffield Wednesday (Steel City Derby)
        (41, 65),    // Southampton vs Portsmouth (South Coast Derby)
        (35, 52),    // Bournemouth vs Brighton
        (62, 63),    // Fulham vs QPR (West London Derby)
        (60, 62),    // Chelsea vs Fulham
        (71, 69),    // Norwich vs Ipswich (East Anglian Derby)
        (74, 72),    // Nottingham Forest vs Derby (East Midlands Derby)
        (46, 44),    // Leicester vs Nottingham Forest
        (39, 66),    // Wolves vs Aston Villa (West Midlands Derby)
        
        // Spain - La Liga
        (529, 530), // Barcelona vs Real Madrid (El Clasico)
        (541, 531), // Real Madrid vs Atletico Madrid (Madrid Derby)
        (529, 531), // Barcelona vs Atletico Madrid
        (532, 533), // Valencia vs Villarreal (Derby of the Community of Valencia)
        (536, 538), // Sevilla vs Real Betis (Seville Derby)
        (548, 540), // Real Sociedad vs Athletic Bilbao (Basque Derby)
        
        // Italy - Serie A
        (489, 505), // AC Milan vs Inter Milan (Derby della Madonnina)
        (496, 497), // Juventus vs Torino (Derby della Mole)
        (489, 496), // AC Milan vs Juventus
        (492, 494), // Napoli vs Roma
        (497, 505), // Inter vs Torino
        (487, 488), // Lazio vs Roma (Derby della Capitale)
        (500, 502), // Bologna vs Fiorentina
        (499, 511), // Atalanta vs Brescia
        
        // Germany - Bundesliga  
        (157, 165), // Bayern Munich vs Borussia Dortmund (Der Klassiker)
        (157, 159), // Bayern Munich vs Bayern Leverkusen
        (169, 163), // Koln vs Monchengladbach (Rhine Derby)
        (172, 169), // Koln vs Dusseldorf
        (160, 174), // Frankfurt vs Darmstadt
        (165, 176), // Dortmund vs Schalke (Revierderby)
        (173, 172), // Stuttgart vs Karlsruher
        (192, 165), // Bochum vs Dortmund
        
        // France - Ligue 1
        (80, 81),   // Lyon vs Saint-Etienne (Derby Rhône-Alpes)
        (85, 79),   // PSG vs Marseille (Le Classique)
        (91, 94),   // Monaco vs Nice (Derby de la Côte d'Azur)
        (80, 85),   // Lyon vs PSG
        (79, 81),   // Marseille vs Saint-Etienne
        (78, 93),   // Bordeaux vs Toulouse
        (83, 96),   // Nantes vs Rennes (Derby Breton)
    ];

    /// <summary>
    /// Check if a fixture is a derby based on team IDs
    /// </summary>
    public static bool IsDerby(int homeTeamId, int awayTeamId)
    {
        var pair1 = (Math.Min(homeTeamId, awayTeamId), Math.Max(homeTeamId, awayTeamId));
        return DerbyPairs.Contains(pair1);
    }
}
