using Mediator.Net.Context;
using Mediator.Net.Contracts;
using soccer_gpt_application.Interfaces;
using soccer_gpt_application.Models;

namespace soccer_gpt_application.Features.Tickets.Queries;

/// <summary>
/// Handler for generating betting tickets
/// </summary>
public class GenerateTicketsQueryHandler(
    IFixtureRepository fixtureRepository)
    : IRequestHandler<GenerateTicketsQuery, GenerateTicketsResponse>
{
    public async Task<GenerateTicketsResponse> Handle(
        IReceiveContext<GenerateTicketsQuery> context, 
        CancellationToken cancellationToken)
    {
        var request = context.Message;
        
        try
        {
            // 1. Get upcoming fixtures
            var fixtures = await fixtureRepository.GetFixturesAsync(0, 200, cancellationToken);
            
            // 2. Convert to MatchFixtureDto
            var matchFixtures = fixtures.Select(f => new MatchFixtureDto
            {
                HomeTeam = f.HomeTeam,
                AwayTeam = f.AwayTeam,
                League = f.League,
                MatchDate = DateTime.TryParse(f.Date, out var date) ? date : null,
                Odds = f.Odds != null ? new MatchOddsDto
                {
                    HomeWin = f.Odds.HomeWin,
                    Draw = f.Odds.Draw,
                    AwayWin = f.Odds.AwayWin,
                    Over25 = f.Odds.Over25,
                    Under25 = f.Odds.Under25,
                    BttsYes = f.Odds.BttsYes
                } : null
            }).ToList();
            
            
            return new GenerateTicketsResponse
            {
                Strategy = "Confidence-Based Greedy Selection"
            };
        }
        catch
        {
            throw;
        }
    }
    
    private List<BettingTicketDto> GenerateTickets(
        List<MatchAnalysisDto> candidates,
        int minGamesPerTicket,
        int maxTickets)
    {
        var tickets = new List<BettingTicketDto>();
        var usedMatches = new HashSet<string>();
        int ticketId = 1;
        
        for (int i = 0; i < maxTickets; i++)
        {
            var ticketMatches = new List<TicketMatchDto>();
            double totalOdds = 1.0;
            double totalConfidence = 0.0;
            
            foreach (var candidate in candidates)
            {
                var matchKey = $"{candidate.HomeTeam}_{candidate.AwayTeam}";
                
                if (usedMatches.Contains(matchKey))
                    continue;
                    
                if (ticketMatches.Count >= minGamesPerTicket)
                    break;
                
                usedMatches.Add(matchKey);
            }
            
            // Only add ticket if it has minimum games
            if (ticketMatches.Count >= minGamesPerTicket)
            {
                tickets.Add(new BettingTicketDto
                {
                    TicketId = ticketId++,
                    Matches = ticketMatches,
                    TotalOdds = Math.Round(totalOdds, 2),
                    TotalConfidence = Math.Round(totalConfidence / ticketMatches.Count, 2),
                    Strategy = "Greedy High-Confidence Selection"
                });
            }
            else
            {
                break; // Not enough matches left for another ticket
            }
        }
        
        return tickets;
    }
}

/// <summary>
/// Response for ticket generation
/// </summary>
public class GenerateTicketsResponse : IResponse
{
    public List<BettingTicketDto> Tickets { get; set; } = new();
    public int TotalCandidates { get; set; }
    public string Strategy { get; set; } = string.Empty;
}
