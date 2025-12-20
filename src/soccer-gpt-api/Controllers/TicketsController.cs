using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using soccer_gpt_application.Interfaces;
using soccer_gpt_application.Models;
using soccer_gpt_infrastructure.Services;

namespace soccer_gpt_api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TicketsController : ControllerBase
{
    private readonly ILogger<TicketsController> _logger;
    private readonly IGeminiService _gemini;
    private readonly IFixtureRepository _fixtureRepository;
    public TicketsController(
        ILogger<TicketsController> logger,
        IGeminiService gemini,
        IFixtureRepository fixtureRepository)
    {
        _logger = logger;
        _gemini = gemini;
        _fixtureRepository = fixtureRepository;
    }



    [HttpGet("generate")]
    public async Task<IActionResult> GenerateTickets([FromQuery] double minOdds = 1.77, [FromQuery] int gamesPerTicket = 3, [FromQuery] int numTickets = 3)
    {
        _logger.LogInformation($"Generating {numTickets} Tickets (Size: {gamesPerTicket}, MinOdds: {minOdds})...");
        
        try
        {
            if (!System.IO.File.Exists("analysis_cache.json")) 
                return BadRequest("No Analysis Cache found. Run /analyze first.");

            var json = await System.IO.File.ReadAllTextAsync("analysis_cache.json");
            var allAnalyzed = JsonSerializer.Deserialize<List<AnalyzedMatchDto>>(json);
            
            // 1. Filter High Confidence
            var candidates = allAnalyzed
                .Where(m => m.AiConfidence > 0.60) // Basic confidence threshold
                .OrderByDescending(m => m.AiConfidence) // Sort by Confidence
                .ToList();

            if (candidates.Count < gamesPerTicket) 
                return BadRequest($"Not enough high-confidence matches found ({candidates.Count}). Needed {gamesPerTicket}.");

            // 2. Deterministic Ticket Generation (Greedy approach)
            var tickets = new List<GeminiTicketResponse>();
            var usedMatchIds = new HashSet<string>();
            int ticketId = 1;

            // Loop to create tickets
            for (int i = 0; i < numTickets; i++)
            {
                var ticketMatches = new List<GeminiTicketMatch>();
                double currentTicketOdds = 1.0;

                foreach (var match in candidates)
                {
                    if (usedMatchIds.Contains(match.MatchId)) continue;
                    if (ticketMatches.Count >= gamesPerTicket) break;

                    // Determine selection and odds
                    string selection = match.AiPrediction;
                    double odds = 0;

                    // Parse Odds based on prediction text
                    // If prediction is "Over 2.5 Goals", get match.Odds.Over25
                    // If "BTTS", get match.Odds.BttsYes
                    if (match.Odds != null)
                    {
                        if (selection.Contains("Over 2.5", StringComparison.OrdinalIgnoreCase))
                        {
                            odds = (double)match.Odds.Over25;
                        }
                        else if (selection.Contains("BTTS", StringComparison.OrdinalIgnoreCase) || selection.Contains("Both Teams", StringComparison.OrdinalIgnoreCase))
                        {
                            odds = (double)match.Odds.BttsYes;
                        }
                        else if (selection.Contains("Home", StringComparison.OrdinalIgnoreCase))
                        {
                            odds = (double)match.Odds.HomeWin;
                        }
                    }

                    // Fallback or specific user constraint: 
                    // If odds are missing or 0, skip.
                    if (odds <= 1.05) continue; 

                    ticketMatches.Add(new GeminiTicketMatch 
                    {
                        Match = $"{match.HomeTeam} vs {match.AwayTeam}",
                        Selection = selection,
                        Odds = odds
                    });

                    currentTicketOdds *= odds;
                    usedMatchIds.Add(match.MatchId);
                }

                // Verify Ticket Constraints
                if (ticketMatches.Count == gamesPerTicket && currentTicketOdds >= minOdds)
                {
                    tickets.Add(new GeminiTicketResponse
                    {
                        TicketId = ticketId++,
                        Matches = ticketMatches,
                        TotalOdds = Math.Round(currentTicketOdds, 2),
                        Analysis = $"Ticket #{ticketId-1} (Confidence-Ranked). Total Odds: {currentTicketOdds:F2}"
                    });
                }
                else
                {
                    // If we failed to fill a ticket, backtrack? 
                    // For simplicity in this greedy approach, we just discard this attempt if we ran out of matches.
                    // But we marked them as used.
                    // In a simple greedy approach, failure means we stop.
                    if (ticketMatches.Count < gamesPerTicket) break;
                    
                    // If failed due to Low Odds but count is OK, we might want to skip this ticket but keep looking?
                    // Actually, since we sort by confidence, later matches have lower confidence.
                    // If top matches result in low odds (favorites), we might need to add checks.
                    // But for now, we just accept valid tickets.
                }
            }
            
            return Ok(new 
            { 
                generated_at = DateTime.Now,
                strategy = "Deterministic Confidence Ranking",
                ticket_count = tickets.Count,
                items = tickets 
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ticket Generation Failed");
            return StatusCode(500, ex.Message);
        }
    }
}
