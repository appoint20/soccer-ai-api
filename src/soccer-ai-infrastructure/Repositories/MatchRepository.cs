using SoccerAi.Application.Interfaces;
using SoccerAi.Application.Models.Deterministic;

namespace SoccerAi.Infrastructure.Repositories;

public class MatchRepository : IMatchRepository
{
    public Task<List<Match>> GetUpcomingMatchesAsync(System.DateTimeOffset? date = null)
    {
        var matches = new List<Match>
        {
            new() { MatchId = "101", HomeTeam = "Arsenal", AwayTeam = "Chelsea", League = "Premier League", HomeWinOdds = 1.85, AwayWinOdds = 3.40, DrawOdds = 3.20, HomeWinProbability = 0.72, AwayWinProbability = 0.15, DrawProbability = 0.13, HomeForm = 0.8, AwayForm = 0.4 },
            new() { MatchId = "102", HomeTeam = "Real Madrid", AwayTeam = "Getafe", League = "La Liga", HomeWinOdds = 1.35, AwayWinOdds = 8.00, DrawOdds = 5.00, HomeWinProbability = 0.85, AwayWinProbability = 0.05, DrawProbability = 0.10, HomeForm = 0.9, AwayForm = 0.3 },
            new() { MatchId = "103", HomeTeam = "Bayern Munich", AwayTeam = "Bochum", League = "Bundesliga", HomeWinOdds = 1.20, AwayWinOdds = 12.00, DrawOdds = 7.00, HomeWinProbability = 0.90, AwayWinProbability = 0.02, DrawProbability = 0.08, HomeForm = 0.95, AwayForm = 0.2 },
            new() { MatchId = "104", HomeTeam = "Juventus", AwayTeam = "Lazio", League = "Serie A", HomeWinOdds = 2.10, AwayWinOdds = 3.20, DrawOdds = 3.10, HomeWinProbability = 0.65, AwayWinProbability = 0.20, DrawProbability = 0.15, HomeForm = 0.7, AwayForm = 0.6 },
            new() { MatchId = "105", HomeTeam = "Man City", AwayTeam = "Liverpool", League = "Premier League", HomeWinOdds = 2.05, AwayWinOdds = 3.10, DrawOdds = 3.50, HomeWinProbability = 0.60, AwayWinProbability = 0.25, DrawProbability = 0.15, HomeForm = 0.85, AwayForm = 0.75 },
            new() { MatchId = "106", HomeTeam = "Barcelona", AwayTeam = "Sociedad", League = "La Liga", HomeWinOdds = 1.75, AwayWinOdds = 4.20, DrawOdds = 3.80, HomeWinProbability = 0.68, AwayWinProbability = 0.18, DrawProbability = 0.14, HomeForm = 0.75, AwayForm = 0.65 },
            new() { MatchId = "107", HomeTeam = "PSG", AwayTeam = "Monaco", League = "Ligue 1", HomeWinOdds = 1.45, AwayWinOdds = 6.00, DrawOdds = 4.50, HomeWinProbability = 0.78, AwayWinProbability = 0.12, DrawProbability = 0.10, HomeForm = 0.9, AwayForm = 0.5 },
            new() { MatchId = "108", HomeTeam = "Inter", AwayTeam = "Milan", League = "Serie A", HomeWinOdds = 2.20, AwayWinOdds = 3.10, DrawOdds = 3.30, HomeWinProbability = 0.55, AwayWinProbability = 0.30, DrawProbability = 0.15, HomeForm = 0.8, AwayForm = 0.7 },
            new() { MatchId = "109", HomeTeam = "Dortmund", AwayTeam = "Leipzig", League = "Bundesliga", HomeWinOdds = 2.40, AwayWinOdds = 2.80, DrawOdds = 3.40, HomeWinProbability = 0.45, AwayWinProbability = 0.35, DrawProbability = 0.20, HomeForm = 0.7, AwayForm = 0.75 },
            new() { MatchId = "110", HomeTeam = "Atletico", AwayTeam = "Sevilla", League = "La Liga", HomeWinOdds = 1.90, AwayWinOdds = 4.00, DrawOdds = 3.50, HomeWinProbability = 0.60, AwayWinProbability = 0.20, DrawProbability = 0.20, HomeForm = 0.8, AwayForm = 0.4 },
            new() { MatchId = "111", HomeTeam = "Tottenham", AwayTeam = "Everton", League = "Premier League", HomeWinOdds = 1.65, AwayWinOdds = 5.00, DrawOdds = 4.00, HomeWinProbability = 0.75, AwayWinProbability = 0.10, DrawProbability = 0.15, HomeForm = 0.7, AwayForm = 0.3 },
            new() { MatchId = "112", HomeTeam = "Leverkusen", AwayTeam = "Stuttgart", League = "Bundesliga", HomeWinOdds = 1.55, AwayWinOdds = 6.00, DrawOdds = 4.50, HomeWinProbability = 0.82, AwayWinProbability = 0.10, DrawProbability = 0.08, HomeForm = 0.95, AwayForm = 0.5 },
            new() { MatchId = "113", HomeTeam = "Napoli", AwayTeam = "Roma", League = "Serie A", HomeWinOdds = 2.05, AwayWinOdds = 3.50, DrawOdds = 3.20, HomeWinProbability = 0.62, AwayWinProbability = 0.22, DrawProbability = 0.16, HomeForm = 0.75, AwayForm = 0.6 },
            new() { MatchId = "114", HomeTeam = "Benfica", AwayTeam = "Porto", League = "Primeira Liga", HomeWinOdds = 2.15, AwayWinOdds = 3.30, DrawOdds = 3.20, HomeWinProbability = 0.58, AwayWinProbability = 0.28, DrawProbability = 0.14, HomeForm = 0.85, AwayForm = 0.8 },
            new() { MatchId = "115", HomeTeam = "Ajax", AwayTeam = "PSV", League = "Eredivisie", HomeWinOdds = 2.50, AwayWinOdds = 2.60, DrawOdds = 3.60, HomeWinProbability = 0.42, AwayWinProbability = 0.40, DrawProbability = 0.18, HomeForm = 0.6, AwayForm = 0.9 },
            new() { MatchId = "116", HomeTeam = "Celtic", AwayTeam = "Rangers", League = "Premiership", HomeWinOdds = 1.95, AwayWinOdds = 3.60, DrawOdds = 3.50, HomeWinProbability = 0.65, AwayWinProbability = 0.20, DrawProbability = 0.15, HomeForm = 0.9, AwayForm = 0.8 },
            new() { MatchId = "117", HomeTeam = "Feyenoord", AwayTeam = "AZ", League = "Eredivisie", HomeWinOdds = 1.60, AwayWinOdds = 5.50, DrawOdds = 4.20, HomeWinProbability = 0.78, AwayWinProbability = 0.08, DrawProbability = 0.14, HomeForm = 0.8, AwayForm = 0.5 },
            new() { MatchId = "118", HomeTeam = "Newcastle", AwayTeam = "Brighton", League = "Premier League", HomeWinOdds = 2.30, AwayWinOdds = 3.00, DrawOdds = 3.40, HomeWinProbability = 0.52, AwayWinProbability = 0.28, DrawProbability = 0.20, HomeForm = 0.7, AwayForm = 0.65 },
            new() { MatchId = "119", HomeTeam = "Villa", AwayTeam = "Wolves", League = "Premier League", HomeWinOdds = 1.70, AwayWinOdds = 4.80, DrawOdds = 3.90, HomeWinProbability = 0.74, AwayWinProbability = 0.12, DrawProbability = 0.14, HomeForm = 0.8, AwayForm = 0.4 },
            new() { MatchId = "120", HomeTeam = "Lyon", AwayTeam = "Marseille", League = "Ligue 1", HomeWinOdds = 2.60, AwayWinOdds = 2.50, DrawOdds = 3.40, HomeWinProbability = 0.38, AwayWinProbability = 0.40, DrawProbability = 0.22, HomeForm = 0.5, AwayForm = 0.7 }
        };

        return Task.FromResult(matches);
    }
}
