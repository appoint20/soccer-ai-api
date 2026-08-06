namespace SoccerAi.Application.Options;

/// <summary>
/// One-time import of historical Bet365 prices from football-data.co.uk.
///
/// This exists because API-Football keeps only seven days of pre-match odds, so
/// the backtest could only ever see a fraction of finished fixtures — far too
/// few picks to conclude anything. These free season files carry Bet365 1X2 and
/// Over/Under 2.5 prices going back years.
///
/// They do <b>not</b> carry BTTS prices. That market stays limited to what the
/// worker captures live.
/// </summary>
public sealed class HistoricalOddsOptions
{
    public const string SectionName = "HistoricalOdds";

    public string BaseUrl { get; set; } = "https://www.football-data.co.uk/mmz4281";

    /// <summary>
    /// API-Football league id → football-data.co.uk division code.
    /// Leagues absent from this map are skipped: 3. Liga, for one, is not
    /// published there.
    /// </summary>
    public Dictionary<int, string> Divisions { get; set; } = new()
    {
        [39] = "E0",    // Premier League
        [40] = "E1",    // Championship
        [41] = "E2",    // League One
        [42] = "E3",    // League Two
        [46] = "EC",    // National League
        [78] = "D1",    // Bundesliga
        [79] = "D2",    // 2. Bundesliga
        [140] = "SP1",  // La Liga
        [141] = "SP2",  // La Liga 2
        [135] = "I1",   // Serie A
        [136] = "I2",   // Serie B
        [61] = "F1",    // Ligue 1
        [62] = "F2"     // Ligue 2
    };

    /// <summary>
    /// Minimum similarity before an external team name is accepted.
    ///
    /// Set high deliberately. A missed match is visible in the report and costs
    /// one fixture; a wrong match writes one club's prices onto another club's
    /// game, which corrupts the backtest in a way nobody would ever spot.
    /// </summary>
    public double MinTeamNameSimilarity { get; set; } = 0.85;

    /// <summary>
    /// Days either side of the CSV date to look for the fixture. The files carry
    /// local kickoff dates while fixtures are stored in UTC, so a late kickoff
    /// can legitimately land on the neighbouring day.
    /// </summary>
    public int DateToleranceDays { get; set; } = 1;
}
