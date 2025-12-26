using Mediator.Net.Contracts;
using soccer_gpt_application.Models.ML;

namespace soccer_gpt_application.Features.Tickets.Queries;

/// <summary>
/// Query to generate betting tickets
/// </summary>
public class GenerateTicketsQuery : IRequest
{
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public List<string>? Leagues { get; set; }
    public int MinGamesPerTicket { get; set; } = 3;
    public int MaxTickets { get; set; } = 3;
}

/// <summary>
/// DTO for a betting ticket
/// </summary>
public class BettingTicketDto
{
    public int TicketId { get; set; }
    public List<TicketMatchDto> Matches { get; set; } = new();
    public double TotalOdds { get; set; }
    public double TotalConfidence { get; set; }
    public string Strategy { get; set; } = string.Empty;
}

/// <summary>
/// DTO for a match in a ticket
/// </summary>
public class TicketMatchDto
{
    public string HomeTeam { get; set; } = string.Empty;
    public string AwayTeam { get; set; } = string.Empty;
    public string League { get; set; } = string.Empty;
    public DateTime? MatchDate { get; set; }
    public string SelectedMarket { get; set; } = string.Empty;
    public double Odds { get; set; }
    public double Confidence { get; set; }
}
