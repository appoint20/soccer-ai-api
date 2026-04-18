using System.Collections.Generic;

namespace SoccerAi.Application.Models.Deterministic;

public class Match
{
    public string MatchId { get; set; } = string.Empty;
    public string HomeTeam { get; set; } = string.Empty;
    public string AwayTeam { get; set; } = string.Empty;
    public string League { get; set; } = string.Empty;

    public double HomeWinOdds { get; set; }
    public double AwayWinOdds { get; set; }
    public double DrawOdds { get; set; }

    public double HomeWinProbability { get; set; }
    public double AwayWinProbability { get; set; }
    public double DrawProbability { get; set; }

    public double HomeForm { get; set; }
    public double AwayForm { get; set; }
}

public class Combination
{
    public List<MatchSelection> Matches { get; set; } = new();
    public double TotalOdds { get; set; }
    public double Score { get; set; }
    public double AvgProbability { get; set; }
    public string Reasoning { get; set; } = string.Empty;
}

public class MatchSelection
{
    public Match Match { get; set; } = null!;
    public string BetType { get; set; } = string.Empty; // "home_win", "away_win", etc.
    public double Odds { get; set; }
    public double Probability { get; set; }
}

public class NlpFilters
{
    public List<string> Leagues { get; set; } = new();
    public double MinProbability { get; set; }
}

public class NlpIntent
{
    public List<int> NumMatches { get; set; } = new();
    public string BetType { get; set; } = string.Empty;
    public double MinOdds { get; set; }
    public NlpFilters Filters { get; set; } = new();
}

public class CombinationRequest
{
    public string Query { get; set; } = string.Empty;
    public System.DateTimeOffset? Date { get; set; }
}

public class CombinationResponse
{
    public List<Combination> Combinations { get; set; } = new();
}
