using SoccerAi.Application.Models;

namespace SoccerAi.Application.Interfaces;

/// <summary>Manual date sync, including the season history needed to score that day.</summary>
public interface IDateFixtureSyncService
{
    Task<SyncResult> SyncLeagueForDateAsync(int leagueId, DateOnly date, CancellationToken ct);
    Task<DateOddsReport> CaptureDateOddsAsync(DateOnly date, CancellationToken ct);
}

public sealed record DateOddsReport(int Candidates, int Checked, int WithFreshBet365Prices, int Skipped);
