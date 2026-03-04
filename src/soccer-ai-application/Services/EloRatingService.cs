namespace SoccerAi.Application.Services;

public static class EloRatingService
{
    private const int KFactor = 32;

    public static (double HomeEloChange, double AwayEloChange) CalculateEloChange(
        double homeElo, double awayElo, int homeGoals, int awayGoals)
    {
        double actualHomeScore;
        double actualAwayScore;

        if (homeGoals > awayGoals)
        {
            actualHomeScore = 1.0;
            actualAwayScore = 0.0;
        }
        else if (homeGoals < awayGoals)
        {
            actualHomeScore = 0.0;
            actualAwayScore = 1.0;
        }
        else
        {
            actualHomeScore = 0.5;
            actualAwayScore = 0.5;
        }

        double expectedHomeScore = 1.0 / (1.0 + Math.Pow(10, (awayElo - homeElo) / 400.0));
        double expectedAwayScore = 1.0 - expectedHomeScore;

        // Multiply K by goal difference factor to account for dominance
        double goalDiffFactor = CalculateGoalDiffFactor(homeGoals, awayGoals);
        double adjustedK = KFactor * goalDiffFactor;

        double homeChange = adjustedK * (actualHomeScore - expectedHomeScore);
        double awayChange = adjustedK * (actualAwayScore - expectedAwayScore);

        return (homeChange, awayChange);
    }

    private static double CalculateGoalDiffFactor(int homeGoals, int awayGoals)
    {
        int diff = Math.Abs(homeGoals - awayGoals);
        if (diff <= 1) return 1.0;
        if (diff == 2) return 1.5;
        return (11.0 + diff) / 8.0; // Standard goal difference adjustment
    }
}
