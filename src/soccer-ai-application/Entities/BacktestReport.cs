using System.ComponentModel.DataAnnotations;

namespace SoccerAi.Application.Entities;

/// <summary>
/// Persistent cache for historical backtesting reports.
/// Prevents expensive 10-minute recalculations on every request.
/// </summary>
public class BacktestReport
{
    public int Id { get; set; }
    
    /// <summary>The window size in weeks (e.g. 10)</summary>
    public int WeeksBack { get; set; }
    
    /// <summary>The hypothetical stake per bet (e.g. 100.0)</summary>
    public double Stake { get; set; }
    
    /// <summary>The full serialized JSON response of the backtest</summary>
    public string ReportJson { get; set; } = string.Empty;
    
    /// <summary>Creation date to determine cache age (expires after 7 days)</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
